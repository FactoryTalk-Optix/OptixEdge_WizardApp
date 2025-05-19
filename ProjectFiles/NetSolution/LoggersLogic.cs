#region Using directives
using System;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.UI;
using FTOptix.HMIProject;
using FTOptix.CoreBase;
using FTOptix.NetLogic;
using FTOptix.WebUI;
using FTOptix.DataLogger;
using FTOptix.Store;
using FTOptix.Core;
using System.Threading;
using FTOptix.InfluxDBStoreRemote;
using System.Linq;
using System.Collections.Immutable;
using System.Collections.Generic;
using FTOptix.Report;
using FTOptix.AuditSigning;
using System.Threading.Tasks;
using System.Globalization;
using System.Security.Cryptography;
using FTOptix.NativeUI;
#endregion

public class LoggersLogic : BaseNetLogic
{
    public static LoggersLogic Instance { get; private set; }

    public override void Start()
    {
        Instance = this;
    }

    public override void Stop()
    {
        Instance = null;
        CommonLogic.DisposeTask(removeStationTask);
    }

    #region Public methods
    [ExportMethod]
    public void CreateNewDatalogger(NodeId widgetOwner)
    {
        var loggersFolder = Project.Current.Get<Folder>(CommonLogic.LoggersFolderPath);
        int countCurrentLoggers = loggersFolder.GetNodesByType<DataLogger>().Count();
        string browseName = $"DataLogger{countCurrentLoggers + 1}";
        var store = Project.Current.Get<Store>("DataStores/EdgeEmbeddedDatabase");
        if (loggersFolder.Get(browseName) == null)
        {
            var newLogger = InformationModel.Make<DataLogger>(browseName);
            newLogger.Store = store.NodeId;
            newLogger.SamplingMode = FTOptix.DataLogger.SamplingMode.Periodic;
            newLogger.SamplingPeriod = 1000;
            newLogger.PollingPeriod = 1000;
            newLogger.LogLocalTime = true;
            newLogger.TableName = browseName;
            if (InformationModel.Get(widgetOwner) is ColumnLayout verticalLayout)
            {
                var newWidget = InformationModel.MakeObject<DataloggerUIObj>(browseName);
                newWidget.SetAlias(CommonLogic.editAliasNameMapping.GetValueOrDefault(newLogger.ObjectType.NodeId), newLogger);
                verticalLayout.Add(newWidget);
                newWidget.FindByType<StationProps>().GetVariable("EnableSave").Value = true;
                newWidget.FindByType<StationProps>().GetVariable("EnableImport").Value = false;
            }
        }
        else
        {
            NotificationsMessageHandlerLogic.Instance.RequestBannerNotification(ToastBannerNotificationLevel.Warning, "Cannot add the new datalogger, already exist in the system");
        }
    }

    [ExportMethod]
    public void SaveProperties(NodeId logger, NodeId widget)
    {
        try
        {
            if (InformationModel.GetObject(logger) is DataLogger editStation && InformationModel.GetObject(widget) is IUAObject widgetNode)
            {
                string stationNodeAlias = CommonLogic.sourceAliasNameMapping.GetValueOrDefault(editStation.ObjectType.NodeId);
                var sourceStation = (DataLogger)widgetNode.GetAlias(stationNodeAlias);
                if (sourceStation == null)
                {
                    sourceStation = InformationModel.Make<DataLogger>(editStation.BrowseName);
                    ApplyProperties(sourceStation, editStation, 0);
                    Project.Current.Get<Folder>(CommonLogic.LoggersFolderPath).Add(sourceStation);
                    widgetNode.SetAlias(stationNodeAlias, sourceStation);
                    if (InformationModel.GetObject(sourceStation.Store) is Store loggerStore && loggerStore.Tables.Get(sourceStation.BrowseName) is Table loggerStoreTable)
                    {
                        loggerStoreTable.RecordLimit = widgetNode.GetVariable("TableRecordLimits").Value;
                    }
                    NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Success, $"New datalogger successfully created");
                }
                else
                {
                    sourceStation.Stop();
                    ApplyProperties(sourceStation, editStation, widgetNode.GetVariable("TableRecordLimits").Value);
                    sourceStation.Start();
                    NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Success, $"Parameters for Datalogger {sourceStation.BrowseName} successfully saved.");
                }
                widgetNode.Find("StationActions").GetVariable("EnableImport").Value = true;
                widgetNode.Find("StationActions").GetVariable("EnableSave").Value = false;
            }
            else
            {
                throw new NullReferenceException("Missing logger node!");
            }
        }
        catch (Exception ex)
        {
            NotificationsMessageHandlerLogic.Instance.RequestBannerNotification(ToastBannerNotificationLevel.Error, "Critical error - Check application logs");
            Log.Error(LogicObject.BrowseName, $"{ex.Message} - Stack: {ex.StackTrace}");
        }
    }

    [ExportMethod]
    public void DeleteStation(NodeId station, NodeId widget)
    {
        IUANode[] nodesToDelete = { InformationModel.Get(station), InformationModel.Get(widget) };
        removeStationTask = new DelayedTask(DeleteStationTask, nodesToDelete, 100, LogicObject);
        removeStationTask.Start();
    }

    public void SaveTagsConfiguration(NodeId logger)
    {
        try
        {
            if (InformationModel.GetObject(logger) is DataLogger loggerNode)
            {
                loggerNode.Stop();
                CreateOrUpdateTags(loggerNode);
                loggerNode.Start();
            }
            else
            {
                throw new NullReferenceException("Missing logger node!");
            }
        }
        catch (Exception ex)
        {
            NotificationsMessageHandlerLogic.Instance.RequestBannerNotification(ToastBannerNotificationLevel.Error, "Critical error - Check application logs");
            Log.Error(LogicObject.BrowseName, $"{ex.Message} - Stack: {ex.StackTrace}");
        }
    }
    #endregion

    private void DeleteStationTask(DelayedTask task, object arguments)
    {
        var nodesToDelete = (IUANode[])arguments;
        if (nodesToDelete[0] is DataLogger editStation && nodesToDelete[1] is Item loggerWidget)
        {
            string stationNodeAlias = CommonLogic.sourceAliasNameMapping.GetValueOrDefault(editStation.ObjectType.NodeId);
            var sourceStation = (DataLogger)loggerWidget.GetAlias(stationNodeAlias);
            try
            {
                loggerWidget.Delete();
            }
            catch
            {
                // nothing important
            }
            try
            {
                editStation.Delete();                
            }
            catch
            {
                // nothing important
            }
            try
            {              
                if (InformationModel.GetObject(sourceStation?.Store) is Store loggerStore && loggerStore.Tables.Get(sourceStation.BrowseName) is Table loggerStoreTable)
                {
                    sourceStation.Stop();                    
                    loggerStoreTable.Delete();
                    try
                    {
                        loggerStore.Query($"DROP TABLE {sourceStation.BrowseName}", out _, out _);
                    }
                    catch
                    {
                        // Query failed
                    }
                }
                if (sourceStation != null)
                {
                    string sourceStationName = sourceStation.BrowseName;     
                    sourceStation.Delete();               
                    NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Success, $"Data logger {sourceStationName} successfully deleted.");
                } 
            }
            catch
            {
                // nothing important
            }
        }
        task.Dispose();
    }

    private void CreateOrUpdateTags(IUAObject logger)
    {
        if (Project.Current.Get($"Model/{logger.BrowseName}") is not IUAObject temporaryFolder)
        {
            NotificationsMessageHandlerLogic.Instance.RequestBannerNotification(ToastBannerNotificationLevel.Warning, $"Cannot add variables to logger {logger.BrowseName} - Check application logs");
            Log.Warning(LogicObject.BrowseName, $"Missing temporary folder for logger {logger.BrowseName}");
            return;
        }
        int createdTags = 0;
        var variablesToLog = logger.GetObject("VariablesToLog");
        var loggerStore = InformationModel.GetObject(logger.GetVariable("Store").Value) as Store;
        var loggerStoreTable = loggerStore.GetObject("Tables").Get<Table>(logger.BrowseName);
        loggerStoreTable.RecordLimit = 10000;
        int deletedTags = 0;
        foreach (var sourceFieldFolder in temporaryFolder.GetNodesByType<Folder>())
        {
            var dataFromTagImporter = sourceFieldFolder.GetNodesByType<TagCustomGridRowData>();
            foreach (var tagData in dataFromTagImporter.Where(x => x.Checked))
            {
                string variableName = $"{sourceFieldFolder.BrowseName}.{tagData.VariableName}";
                var targetTag = variablesToLog.Get<VariableToLog>(variableName);
                if (targetTag == null)
                {
                    if (tagData.VariableIsArray)
                    {
                        // to handle, for now skip
                    }
                    else
                    {
                        targetTag = InformationModel.MakeVariable(variableName, OpcUa.DataTypes.BaseDataType, FTOptix.DataLogger.VariableTypes.VariableToLog) as VariableToLog;                        
                    }
                    targetTag.Description = new(tagData.VariableComment, Session.ActualLocaleId);
                    variablesToLog.Add(targetTag);
                    if (InformationModel.GetVariable(tagData.VariableNodeId) is IUAVariable sourceTag)
                    {
                        targetTag.SetDynamicLink(sourceTag);
                    }
                    else
                    {
                        Log.Warning(LogicObject.BrowseName, $"sourceTag {tagData.VariableName} not found!");
                    }
                    createdTags++;
                }
            }
            deletedTags += DeleteMissingTag(logger, dataFromTagImporter.Where(x => !x.Checked).ToList());            
        }
        NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Info, $"Added {createdTags}, removed {deletedTags} variables on the logger {logger.BrowseName}");
    }

    private static int DeleteMissingTag(IUAObject logger, List<TagCustomGridRowData> tagDatasUnchecked)
    {
        int deletedTagsCounter = 0;
        List<TagDataImported> tagsImported = CommonLogic.ReadTagsFromSourceDataCollector(logger.GetObject("VariablesToLog"), logger);
        foreach (var tagData in tagsImported.Where(x => tagDatasUnchecked.Exists(y => y.VariableName == x.BrowseName)))
        {
            InformationModel.Get(tagData.NodeId)?.Delete();
            deletedTagsCounter++;
        }
        return deletedTagsCounter;
    }

    private static void ApplyProperties(DataLogger stationNode, DataLogger editNode, uint tableRecordLimits)
    {
        stationNode.SamplingMode = editNode.SamplingMode;
        stationNode.SamplingPeriod = editNode.SamplingPeriod;
        stationNode.PollingPeriod = editNode.PollingPeriod;
        stationNode.Store = editNode.Store;
        stationNode.LogLocalTime = editNode.LogLocalTime;
        stationNode.TableName = editNode.TableName;
        if (InformationModel.GetObject(stationNode.Store) is Store loggerStore && loggerStore.Tables.Get(stationNode.BrowseName) is Table loggerStoreTable)
        {
            loggerStoreTable.RecordLimit = tableRecordLimits;
        }
    }

    private DelayedTask removeStationTask;
}
