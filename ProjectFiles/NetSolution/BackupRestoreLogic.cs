#region Using directives
using System;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.UI;
using FTOptix.HMIProject;
using FTOptix.NetLogic;
using FTOptix.Retentivity;
using FTOptix.Core;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ICSharpCode.SharpZipLib.Zip;
using System.Threading;
using FTOptix.SQLiteStore;
using System.ComponentModel;
using FTOptix.EventLogger;
#endregion

public class BackupRestoreLogic : BaseNetLogic
{
    public override void Start()
    {
        sessionUi = Owner as UISession;
    }

    public override void Stop()
    {
        sessionUi = null;
    }

    [ExportMethod]
    public void ExecuteBackup()
    {
        if (InformationModel.Get<RetentivityStorage>(LogicObject.GetVariable("RetentivityStorage").Value) is RetentivityStorage retentivityStorage)
        {
            new LongRunningTask(BackupAndDownloadTask, retentivityStorage, LogicObject).Start();
        }
    }

    [ExportMethod]
    public void ExecuteBackupData()
    {
        if (InformationModel.Get<SQLiteStore>(LogicObject.GetVariable("EmbeddedDatabase").Value) is SQLiteStore embeddedDatabase)
        {
            new LongRunningTask(BackupAndDownloadTask, embeddedDatabase, LogicObject).Start();
        }
    }

    [ExportMethod]
    public void ExecuteRestore()
    {
        new LongRunningTask(CompressAndUploadTask, FileOperationRequest.OperationType.Restore, LogicObject).Start();
    }

    [ExportMethod]
    public void ExecuteRestoreData()
    {
        new LongRunningTask(CompressAndUploadTask, FileOperationRequest.OperationType.RestoreData, LogicObject).Start();
    }

    [ExportMethod]
    public void DownloadCompleteHandler(int statusCode, int operationType)
    {
        if ((DownloadFileStartedResultCode)statusCode == DownloadFileStartedResultCode.Success)
        {
            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Success, "Backup execute successfully.");
            ClearTempBackupFiles();
        }
    }

    [ExportMethod]
    public void UploadCompleteHandler(int statusCode, string uploadedFileUri, int operationType)
    {
        if ((UploadFileCompletedResultCode)statusCode == UploadFileCompletedResultCode.Success)
        {
            var zipPathUri = new ResourceUri(uploadedFileUri);
            var extractFolderPath = ResourceUri.FromProjectRelativePath($"tempUpload");
            if (!Directory.Exists(extractFolderPath.Uri))
            {
                Directory.CreateDirectory(extractFolderPath.Uri);
            }
            ExtractBackupFile(zipPathUri, extractFolderPath, (FileOperationRequest.OperationType)operationType);
        }
    }

    private void BackupAndDownloadTask(LongRunningTask task, object argument)
    {
        try
        {
            string filePathRelativeUri;
            if (Owner is UISession userSession)
            {
                switch (argument)
                {
                    case RetentivityStorage retentivityStorage:
                        if (BackupAndCompress(retentivityStorage, out filePathRelativeUri) && filePathRelativeUri != null)
                        {
                            var inputArguments = new object[] { FileOperationRequest.OperationType.Backup, filePathRelativeUri, LogicObject.NodeId };
                            sessionUi.Get<NetLogicObject>("FileTransferManagementLogic").ExecuteMethod("RequestDownloadTask", inputArguments);
                        }
                        break;
                    case SQLiteStore sqliteStore:
                        if (BackupAndCompress(sqliteStore, out filePathRelativeUri) && filePathRelativeUri != null)
                        {
                            var inputArguments = new object[] { FileOperationRequest.OperationType.BackupData, filePathRelativeUri, LogicObject.NodeId };
                            userSession.Get<NetLogicObject>("FileTransferManagementLogic").ExecuteMethod("RequestDownloadTask", inputArguments);
                        }
                        break;
                    default:
                        Log.Warning(LogicObject.BrowseName, "Invalid argument passed to backup task. Expected RetentivityStorage or SQLiteStore.");
                        break;
                }
            }
            else
            {
                Log.Warning(LogicObject.BrowseName, "Invalid argument passed to backup task. Expected RetentivityStorage.");
            }
        }
        catch (Exception ex)
        {
            Log.Error(LogicObject.BrowseName, "An error occurred during backup and download task. Exception: " + ex.Message);
        }
        task.Dispose();
    }

    private void CompressAndUploadTask(LongRunningTask task, object argument)
    {
        try
        {
            if (argument is FileOperationRequest.OperationType operationType)
            {
                var zipFolderPath = ResourceUri.FromProjectRelativePath("tempUpload");
                if (!Directory.Exists(zipFolderPath.Uri))
                {
                    Directory.CreateDirectory(zipFolderPath.Uri);
                }
                var inputArguments = new object[] { operationType, zipFolderPath.Uri, ".zip", LogicObject.NodeId };
                sessionUi.Get<NetLogicObject>("FileTransferManagementLogic").ExecuteMethod("RequestUploadTask", inputArguments);
            }
            else
            {
                throw new InvalidEnumArgumentException("Invalid argument passed to upload task. Expected FileOperationRequest.OperationType.");
            }
        }
        catch (Exception ex)
        {
            Log.Error(LogicObject.BrowseName, "An error occurred while executing restore operation. Exception: " + ex.Message);
            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Error, "Failed to execute restore operation. Check the log for details.");
        }
        task.Dispose();
    }

    private bool BackupAndCompress(IUANode storageToBackup, out string filePathRelativeUri)
    {
        try
        {
            filePathRelativeUri = $"tempOperations/{storageToBackup.BrowseName}_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
            var tempFilePath = ResourceUri.FromProjectRelativePath("tempOperations");          
            var userFilesFolder = ResourceUri.FromProjectRelativePath("tempOperations/UserFiles");            
            if (!Directory.Exists(tempFilePath.Uri))
            {
                Directory.CreateDirectory(tempFilePath.Uri);
            }
            // Generate manifest file with backup metadata
            var manifestPath =ResourceUri.FromProjectRelativePath("tempOperations/manifest.json"); 
            if (File.Exists(manifestPath.Uri))
            {
                File.Delete(manifestPath.Uri);
            }
            GenerateManifest(manifestPath);
            // Create backup of storage to a temporary file
            tempFilePath = ResourceUri.FromProjectRelativePath($"tempOperations/{storageToBackup.BrowseName}.bak");
            if (File.Exists(tempFilePath.Uri))
            {
                File.Delete(tempFilePath.Uri);
            }
            // Perform backup based on the type of storage
            switch (storageToBackup)
            {
                case RetentivityStorage retentivityStorage:                    
                    retentivityStorage.Backup(tempFilePath);
                     // Collect user files (certificates, keys, ...) referenced by ResourceUri variables
                    CollectUserFiles(userFilesFolder);
                    break;
                case SQLiteStore sqliteStore:
                    sqliteStore.Backup(tempFilePath);
                    break;
                default:
                    Log.Error(LogicObject.BrowseName, "Unsupported storage type for backup.");
                    filePathRelativeUri = null;
                    return false;
            }
            Thread.Sleep(200);           
            // Compress backup file, manifest and user files into a zip archive
            var zipFilePath = ResourceUri.FromProjectRelativePath(filePathRelativeUri);
            CompressFiles(new List<ResourceUri> { tempFilePath, manifestPath }, userFilesFolder, zipFilePath);
            Thread.Sleep(200);
            // Clean up temporary files
            File.Delete(tempFilePath.Uri);
            File.Delete(manifestPath.Uri);
            if (Directory.Exists(userFilesFolder.Uri))
            {
                Directory.Delete(userFilesFolder.Uri, true);
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(LogicObject.BrowseName, "An error occurred during backup and compression. Exception: " + ex.Message);
            filePathRelativeUri = null;
            return false;
        }
    }

    private static void GenerateManifest(ResourceUri manifestPath)
    {
        // Create manifest file with version info
        var appSettings = Project.Current.GetObject("Model/ApplicationSettings") as ApplicationSettings;
        var runtimeVersion = CommonLogic.GetRuntimeVersion();
        var manifest = new BackupManifest
        {
            WizardAppVersion = appSettings?.DefaultProjectVersion ?? "unknown",
            RuntimeVersion = runtimeVersion.Contains("Error", StringComparison.InvariantCultureIgnoreCase) ? "unknown" : runtimeVersion,
            BackupDate = DateTime.Now.ToString("o"),
            OS = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            Architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
        };
        var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(manifestPath.Uri, manifestJson);
    }

    private void CompressFiles(List<ResourceUri> backupFilePaths, ResourceUri userFilesFolder, ResourceUri zipFilePath)
    {
        using var zipOutputStream = new ZipOutputStream(File.Create(zipFilePath.Uri));
        zipOutputStream.SetLevel(9); // Set compression level (0-9)
        foreach (var backupFilePath in backupFilePaths)
        {
            AddFileToZip(zipOutputStream, backupFilePath.Uri, Path.GetFileName(backupFilePath.Uri));
        }

        if (Directory.Exists(userFilesFolder.Uri))
        {
            foreach (var file in Directory.GetFiles(userFilesFolder.Uri))
            {
                AddFileToZip(zipOutputStream, file, $"{UserFilesFolderName}/{Path.GetFileName(file)}");
            }
        }
    }

    private static void AddFileToZip(ZipOutputStream zipOutputStream, string filePath, string entryName)
    {
        var fileInfo = new FileInfo(filePath);
        var newEntry = new ZipEntry(entryName)
        {
            DateTime = DateTime.Now,
            Size = fileInfo.Length
        };
        zipOutputStream.PutNextEntry(newEntry);
        using (var fileStream = File.OpenRead(filePath))
        {
            fileStream.CopyTo(zipOutputStream);
        }
        zipOutputStream.CloseEntry();
    }

    private void CollectUserFiles(ResourceUri userFilesFolder)
    {
        if (!Directory.Exists(userFilesFolder.Uri))
        {
            Directory.CreateDirectory(userFilesFolder.Uri);
        }

        var manifest = new UserFilesManifest();
        int index = 0;

        foreach (var folderPath in UserFilesFoldersToScan)
        {
            var rootNode = Project.Current.Get(folderPath);
            if (rootNode == null)
            {
                continue;
            }

            foreach (var variable in CollectResourceUriVariables(rootNode))
            {
                try
                {
                    var resourceUriValue = (string)variable.Value;
                    if (string.IsNullOrEmpty(resourceUriValue))
                    {
                        continue;
                    }

                    var sourceUri = new ResourceUri(variable.Value);
                    if (string.IsNullOrEmpty(sourceUri.Uri) || !File.Exists(sourceUri.Uri))
                    {
                        continue;
                    }

                    var storedFileName = $"{index:D4}_{Path.GetFileName(sourceUri.Uri)}";
                    File.Copy(sourceUri.Uri, Path.Combine(userFilesFolder.Uri, storedFileName), true);

                    manifest.Files.Add(new UserFileEntry
                    {
                        ResourceUriValue = resourceUriValue,
                        StoredFileName = storedFileName
                    });
                    index++;
                }
                catch (Exception ex)
                {
                    Log.Warning(LogicObject.BrowseName, $"Failed to collect user file for variable {variable.BrowseName}. Exception: {ex.Message}");
                }
            }
        }

        var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(userFilesFolder.Uri, UserFilesManifestName), manifestJson);
    }

    private static IEnumerable<IUAVariable> CollectResourceUriVariables(IUANode node)
    {
        foreach (var child in node.Children)
        {
            if (child is IUAVariable variable && variable.DataType == FTOptix.Core.DataTypes.ResourceUri)
            {
                yield return variable;
            }

            foreach (var descendant in CollectResourceUriVariables(child))
            {
                yield return descendant;
            }
        }
    }

    private void RestoreUserFiles(ResourceUri extractFolderPath)
    {
        try
        {
            var userFilesDir = Path.Combine(extractFolderPath.Uri, UserFilesFolderName);
            var manifestPath = Path.Combine(userFilesDir, UserFilesManifestName);
            if (!File.Exists(manifestPath))
            {
                return;
            }

            var manifest = JsonSerializer.Deserialize<UserFilesManifest>(File.ReadAllText(manifestPath));
            if (manifest?.Files == null)
            {
                return;
            }

            foreach (var entry in manifest.Files)
            {
                try
                {
                    var storedFilePath = Path.Combine(userFilesDir, entry.StoredFileName);
                    if (!File.Exists(storedFilePath))
                    {
                        Log.Warning(LogicObject.BrowseName, $"Restore: stored user file not found {entry.StoredFileName}");
                        continue;
                    }

                    var targetUri = new ResourceUri(entry.ResourceUriValue);
                    // Normalize separators: stored URIs may contain backslashes that Windows treats
                    // as directory separators but Linux does not, causing GetDirectoryName to return
                    // the wrong folder and the file to be written with a literal backslash in its name.
                    var targetPath = targetUri.Uri.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar);
                    var targetDir = Path.GetDirectoryName(targetPath);
                    if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    File.Copy(storedFilePath, targetPath, true);
                }
                catch (Exception ex)
                {
                    Log.Warning(LogicObject.BrowseName, $"Failed to restore user file '{entry.StoredFileName}'. Exception: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(LogicObject.BrowseName, "Failed to restore user files. Exception: " + ex.Message);
        }
    }

    private void ExtractBackupFile(ResourceUri zipFilePath, ResourceUri extractFolderPath, FileOperationRequest.OperationType operationType)
    {
        ResourceUri extractedFilePath = null;        
        try
        {
            RetentivityStorage retentivityStorage = InformationModel.Get<RetentivityStorage>(LogicObject.GetVariable("RetentivityStorage").Value);
            SQLiteStore embeddedDatabase = InformationModel.Get<SQLiteStore>(LogicObject.GetVariable("EmbeddedDatabase").Value);
            string nodeFileName = operationType == FileOperationRequest.OperationType.Restore ? retentivityStorage?.BrowseName : embeddedDatabase?.BrowseName;
              
            using (var zipInputStream = new ZipInputStream(File.OpenRead(zipFilePath.Uri)))
            {
                ZipEntry entry;
                while ((entry = zipInputStream.GetNextEntry()) != null)
                {
                    if (entry.IsDirectory)
                    {
                        continue;
                    }
                    var entryFilePath = Path.Combine(extractFolderPath.Uri, entry.Name);
                    var entryDir = Path.GetDirectoryName(entryFilePath);
                    if (!string.IsNullOrEmpty(entryDir) && !Directory.Exists(entryDir))
                    {
                        Directory.CreateDirectory(entryDir);
                    }
                    using var fileStream = File.Create(entryFilePath);
                    zipInputStream.CopyTo(fileStream);
                    if (entry.Name.Equals($"{nodeFileName}.bak", StringComparison.OrdinalIgnoreCase))
                    {
                        extractedFilePath = ResourceUri.FromAbsoluteFilePath(entryFilePath);
                    }
                }
            }

                if (extractedFilePath != null)
                {
                    if (!IsBackupVersionCompatible(extractFolderPath))
                    {
                        return;
                    }
                    switch (operationType)
                    {
                        case FileOperationRequest.OperationType.Restore:
                            RestoreUserFiles(extractFolderPath);
                            RestoreFromBackupFile(extractedFilePath, retentivityStorage); 
                            break;
                        case FileOperationRequest.OperationType.RestoreData:
                            RestoreFromBackupFile(extractedFilePath, embeddedDatabase);
                            break;
                        default:
                            throw new InvalidOperationException($"Unsupported operation type for restore: {operationType}");
                    }
                }
                else
                {
                    throw new InvalidOperationException($"No matching backup file ({nodeFileName}.bak) found in archive.");
                }
        }
        catch (Exception ex)
        {
            Log.Error(LogicObject.BrowseName, "Failed to extract backup files from zip archive. Exception: " + ex.Message);
            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Error, "Failed to extract backup file. Check the log for details.");
        }
    }
    
    private bool IsBackupVersionCompatible(ResourceUri extractFolderPath)
    {
        try
        {
            var manifestPath = Path.Combine(extractFolderPath.Uri, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                Log.Warning(LogicObject.BrowseName, "Backup manifest not found. Aborting restore operation.");
                return false;
            }

            var manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(manifestPath));
            var appSettings = Project.Current.GetObject("Model/ApplicationSettings") as ApplicationSettings;
            var currentVersionText = appSettings?.DefaultProjectVersion;

            if (!Version.TryParse(manifest?.WizardAppVersion, out var backupVersion) ||
                !Version.TryParse(currentVersionText, out var currentVersion))
            {
                Log.Warning(LogicObject.BrowseName, $"Unable to compare versions (backup: '{manifest?.WizardAppVersion}', current: '{currentVersionText}'). Aborting restore operation.");
                return false;
            }

            if (backupVersion > currentVersion)
            {
                Log.Error(LogicObject.BrowseName, $"Restore aborted: backup version {backupVersion} is newer than the running application version {currentVersion}.");
                NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Error, $"Backup version ({backupVersion}) is newer than the current application version ({currentVersion}). Restore aborted.");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Error(LogicObject.BrowseName, "Failed to verify backup version compatibility. Exception: " + ex.Message);
            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Error, "Failed to verify backup version. Restore aborted.");
            return false;
        }
    }

    private bool RestoreFromBackupFile(ResourceUri backupFilePath, IUANode storageToRestore)
    {
        try
        {
            if (!File.Exists(backupFilePath.Uri))
            {
                Log.Error(LogicObject.BrowseName, "Backup file not found at path: " + backupFilePath.Uri);
                NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Error, "Backup file not found. Check the log for details.");
                return false;
            }
            switch (storageToRestore)
            {
                case RetentivityStorage retentivityStorage:
                    retentivityStorage.Restore(backupFilePath);
                    Log.Info(LogicObject.BrowseName, "Successfully restored retentivity storage: " + storageToRestore.BrowseName + " from backup file: " + backupFilePath.Uri);
                    NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Success, "Restore configuration executed successfully.");
                    break;
                case SQLiteStore sqliteStore:
                    sqliteStore.Restore(backupFilePath);
                    Log.Info(LogicObject.BrowseName, "Successfully restored Embedded Database: " + storageToRestore.BrowseName + " from backup file: " + backupFilePath.Uri);
                    NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Success, "Restore data executed successfully.");   
                    break;
                default:
                    Log.Error(LogicObject.BrowseName, "Unsupported storage type for restore.");
                    NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Error, "Unsupported storage type for restore. Check the log for details.");
                    return false;
            }           
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(LogicObject.BrowseName, "Failed to restore from backup file for storage: " + storageToRestore.BrowseName + ". Exception: " + ex.Message);
            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Error, "Failed to restore from backup file. Check the log for details.");
            return false;
        }
    }

    private void ClearTempBackupFiles()
    {
        try
        {
            var tempFolderPath = ResourceUri.FromProjectRelativePath("tempOperations");
            if (Directory.Exists(tempFolderPath.Uri))
            {
                var files = Directory.GetFiles(tempFolderPath.Uri);
                foreach (var file in files)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(LogicObject.BrowseName, "Failed to clear temporary backup files. Exception: " + ex.Message);
        }
    }

    private static readonly string[] UserFilesFoldersToScan =
    {
        "CommDrivers",
        "MQTT",
        "OPC-UA",
        "Loggers",
        "Model/ApplicationSettings"   // WPEConfiguration -> CertificateFile + PrivateKey
    };

    private const string UserFilesFolderName = "UserFiles";
    private const string UserFilesManifestName = "user_files_manifest.json";

    private UISession sessionUi;
}

public record UserFilesManifest
{
    public List<UserFileEntry> Files { get; set; } = new();
}

public record UserFileEntry
{
    public string ResourceUriValue { get; set; }
    public string StoredFileName { get; set; }
}


