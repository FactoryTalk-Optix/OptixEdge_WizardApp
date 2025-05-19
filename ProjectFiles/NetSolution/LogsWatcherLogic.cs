#region Using directives
using System;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.UI;
using FTOptix.HMIProject;
using FTOptix.CoreBase;
using FTOptix.NetLogic;
using FTOptix.SQLiteStore;
using FTOptix.WebUI;
using FTOptix.Store;
using FTOptix.System;
using FTOptix.Retentivity;
using FTOptix.Core;
using System.IO;
using System.Runtime.InteropServices;
using System.Diagnostics;
using CsvHelper;
using System.Net.Http.Headers;
using CsvHelper.Configuration;
using System.Globalization;
using System.Threading;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Security.Cryptography.X509Certificates;
using FTOptix.NativeUI;
#endregion

public class LogsWatcherLogic : BaseNetLogic
{
    public override void Start()
    {
        var serverNode = LogicObject.Context.GetObject(OpcUa.Objects.Server);
        storeTable = InformationModel.Get<Table>(LogicObject.GetVariable("StoreTable").Value);
        var logsObserver = new LogsEventObserver(storeTable);
        runtimeLogsRegistration = serverNode.RegisterUAEventObserver(logsObserver, FTOptix.Core.ObjectTypes.LogEvent);
        string logFilePath = FindParentFolder(Project.Current.ApplicationDirectory, "FTOptixApplication");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            string osRelease = File.ReadAllText("/etc/os-release").ToLower();
            if (!osRelease.Contains("ubuntu"))
            {
                logFilePath = "/persistent/log/Rockwell_Automation/FactoryTalk_Optix/FTOptixApplication";
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            logFilePath ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Rockwell Automation\FactoryTalk Optix\Emulator\Log", Project.Current.BrowseName);
        }
        logFilePath = Path.Combine(logFilePath, "FTOptixRuntime.0.log");
        readLogFileTask = new LongRunningTask(ReadLogFile, logFilePath, LogicObject);
        readLogFileTask.Start();
    }

    public override void Stop()
    {
        runtimeLogsRegistration?.Dispose();
        CommonLogic.DisposeTask(readLogFileTask);
        CommonLogic.DisposeTask(updateMinutesRemainingatStopTask);
        storeTable = null;
    }

    [ExportMethod]
    public void StartCountDownToStop()
    {
        if (updateMinutesRemainingatStopTask == null)
        {
            updateMinutesRemainingatStopTask = new PeriodicTask(UpdateMinutesRemainingatStop, DateTime.Now, 20000, LogicObject);
            updateMinutesRemainingatStopTask.Start();
        }
    }

    public static void CheckLicenseManagerMessage(string message)
    {
        if (message.Contains("Demo mode"))
        {
            Project.Current.GetVariable("Model/RuntimeExceedBannerVisibilty").Value = true;
        }
        Match match = Regex.Match(message, @"(\d+)\s+feature\s+tokens");
        if (match.Success)
        { 
            Project.Current.GetVariable("Model/CurrentTokenUsage").Value = match.Groups[1].Value;
        }
    }

    private void ReadLogFile(LongRunningTask task, object argument)
    {
        string filePath = (string)argument;
        try
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("The path to the log file was not found! Unable to check token and demo mode");
            }
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fileStream);
            using var csvReader = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                Delimiter = ";",
                HasHeaderRecord = false,
                MissingFieldFound = null,
                BadDataFound = null
            });
            var minimumValidDate = DateTime.Now.AddSeconds(-20);
            while (csvReader.Read())
            {
                var logRecord = csvReader.GetRecord<LogsCSV>();           
                if (DateTime.TryParseExact(logRecord.Timestamp, "dd-MM-yyyy HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime timeStamp))
                {                    
                    if (timeStamp >= minimumValidDate && logRecord.ModuleName.Contains("LicenseManager"))
                    {
                        CheckLicenseManagerMessage(logRecord.Message);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(LogicObject.BrowseName, ex.Message);
        }
        task.Dispose();
    }

    private void UpdateMinutesRemainingatStop(PeriodicTask task, object argument)
    {
        var startDateTime = (DateTime)argument;
        var minutesElapsed = DateTime.Now - startDateTime;
        int resultValue = 120 - minutesElapsed.Minutes;
        Project.Current.GetVariable("Model/MinutesRemainingToStop").Value = resultValue;
        if (resultValue < 0)
        {
            task.Cancel();
            task.Dispose();
        }
    }

    private static string FindParentFolder(string startPath, string targetFolderName)
    {
        string currentPath = startPath;
        while (currentPath != null)
        {
            var directoryInfo = new DirectoryInfo(currentPath);
            if (directoryInfo.Name.Equals(targetFolderName, StringComparison.OrdinalIgnoreCase))
            {
                return currentPath;
            }
            currentPath = directoryInfo.Parent?.FullName;
        }
        return null;
    }

    private LongRunningTask readLogFileTask;
    private PeriodicTask updateMinutesRemainingatStopTask;
    private IEventRegistration runtimeLogsRegistration;
    private Table storeTable;

    private class LogsCSV
    {
        public string Timestamp { get; set; }
        public string LogLevel { get; set; }
        public string ModuleName { get; set; }
        public string ModuleCode { get; set; }
        public string Message { get; set; }
        public string StackTrace { get; set; }
    }

    private class LogsEventObserver(Table _storeTable) : IUAEventObserver
    {
        public void OnEvent(IUAObject eventNotifier, IUAObjectType eventType, IReadOnlyList<object> args, ulong senderId)
        {
            // DON'T CREATE ANY LOG ENTRY IN THIS VOID
            try
            {
                LocalizedText message = null;
                string moduleName = string.Empty;
                DateTime utcTime = DateTime.MinValue;
                LogLevel level = LogLevel.Benchmark;
                if (args[7] is LocalizedText _message)
                {
                    message = _message;
                    if (message.Text.Contains("192.168.254.254/Backplane/WorkAround"))
                    {
                        return;
                    }                  
                }
                if (args[4] is DateTime _utcTime)
                {
                    utcTime = _utcTime;
                }
                if (args[13] is int _level)
                {
                    level = (LogLevel)_level;
                }
                if (args[15] is string _moduleName)
                {
                    moduleName = _moduleName;
                }
                if (_storeTable != null && message != null && !string.IsNullOrEmpty(moduleName) && utcTime != DateTime.MinValue && level > LogLevel.Fatal && level < LogLevel.Benchmark)
                {
                    if (moduleName.Contains("LicenseManager"))
                    {
                       CheckLicenseManagerMessage(message.Text);
                    }
                    var newRecord = new object[1, storeTableColums.Length];
                    newRecord[0, 0] = utcTime;
                    newRecord[0, 1] = TimeZoneInfo.ConvertTimeFromUtc(utcTime, TimeZoneInfo.Local);
                    newRecord[0, 2] = level switch
                    {
                        LogLevel.Error => ToastBannerNotificationLevel.Error,
                        LogLevel.Warning => ToastBannerNotificationLevel.Warning,
                        LogLevel.Info => ToastBannerNotificationLevel.Info,
                        _ => ToastBannerNotificationLevel.Info
                    };
                    newRecord[0, 3] = moduleName.Replace("urn:FTOptix:", string.Empty).Trim();
                    newRecord[0, 4] = message.Text;
                    _storeTable.Insert(storeTableColums, newRecord);
                }
            }
            catch
            {
                // Cannot log here!
            }
        }

        private readonly string[] storeTableColums = ["Timestamp", "LocalTimeStamp", "LogLevel", "ModuleName", "Message"];

        private enum LogLevel
        {
            Fatal,
            Error,
            Warning,
            Info,
            Benchmark,
            Debug,
            Verbose1,
            Verbose2
        }
    }
}
