#region Using directives
using System;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.SQLiteStore;
using FTOptix.UI;
using FTOptix.HMIProject;
using FTOptix.WebUI;
using FTOptix.NetLogic;
using FTOptix.CoreBase;
using FTOptix.Store;
using FTOptix.Modbus;
using FTOptix.MelsecFX3U;
using FTOptix.S7TCP;
using FTOptix.OmronEthernetIP;
using FTOptix.MelsecQ;
using FTOptix.OmronFins;
using FTOptix.CODESYS;
using FTOptix.TwinCAT;
using FTOptix.RAEtherNetIP;
using FTOptix.MicroController;
using FTOptix.S7TiaProfinet;
using FTOptix.System;
using FTOptix.Retentivity;
using FTOptix.CommunicationDriver;
using FTOptix.MQTTClient;
using FTOptix.OPCUAServer;
using FTOptix.DataLogger;
using FTOptix.OPCUAClient;
using FTOptix.Core;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FTOptix.EventLogger;
#endregion

public class FileTransferManagementLogic : BaseNetLogic
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
    public void RequestUploadTask(int operationRequest, string destinationRequestedFilePath,  string fileExtension, NodeId netlogicToNotify)
    {
        string destinationFilePath;
        bool copyToDestination = false;
        // Validate the provided destination file path. It must not be null or empty, and the directory portion of the path must exist.
        if (string.IsNullOrEmpty(destinationRequestedFilePath) || !Directory.Exists(destinationRequestedFilePath))
        {
            Log.Error(LogicObject.BrowseName, "Destination file path for upload operation is null or empty or the directory does not exist.");
            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Error, "File upload failed: Destination directory is invalid.");
            if (InformationModel.Get<NetLogicObject>(netlogicToNotify) is NetLogicObject netLogicToNotify)
            {
                netLogicToNotify.ExecuteMethod("UploadCompleteHandler", new object[] { 99, string.Empty, operationRequest });
            }
            return;
        }
        // If the destination file path contains the project relative path, remove the absolute portion to get the relative path for the upload API. 
        // Otherwise, treat the provided path as relative to a default directory (e.g., "tempUploads") within the project.
        if (destinationRequestedFilePath.Contains(ResourceUri.FromProjectRelativePath("").Uri))
        {
            destinationFilePath = destinationRequestedFilePath
                .Replace(ResourceUri.FromProjectRelativePath("").Uri, "")
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        else
        {
            // If the destination path is not within the project directory, we will upload the file to a temporary location and then copy it to the requested destination upon completion of the upload. 
            // This is because the upload API requires a project-relative path, and we want to support uploads to arbitrary locations on the file system.
            destinationFilePath = "tempUpload";
            if (!Directory.Exists(ResourceUri.FromProjectRelativePath(destinationFilePath).Uri))
            {
                Directory.CreateDirectory(ResourceUri.FromProjectRelativePath(destinationFilePath).Uri);
            }
            copyToDestination = true;
        }
        if (string.IsNullOrEmpty(fileExtension))
        {
            fileExtension = "*";
        }
        // Create a file operation request and add it to the pending operations list. 
        // The actual upload will be handled by the session UI, which will notify this logic upon completion.
        var fileOperationRequest = new FileOperationRequest
        {
            Operation = (FileOperationRequest.OperationType)operationRequest,
            // The session UI generate a unique operation ID for the upload task, which will be used to track and correlate the upload completion notification with the original request.
            OperationID = sessionUi.UploadFile(ResourceUri.FromProjectRelativePath(destinationFilePath)),
            NetLogicToNotify = netlogicToNotify,
            FullDestinationPath = destinationRequestedFilePath,
            CopyFileToDestination = copyToDestination,
            FileExtensionFilter = fileExtension
        };
        // Add the file operation request to the pending operations list so that it can be tracked and handled upon completion of the upload task.
        pendingFileOperations.Add(fileOperationRequest);
    }

    [ExportMethod]
    public void RequestDownloadTask(int operationRequest, string sourceFileRelativePath, NodeId netlogicToNotify)
    {
        if (string.IsNullOrEmpty(sourceFileRelativePath))
        {
            Log.Error(LogicObject.BrowseName, "Source file path for download operation is null or empty.");
            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Error, "File download failed: Source file path is invalid.");
            if (InformationModel.Get<NetLogicObject>(netlogicToNotify) is NetLogicObject netLogicToNotify)
            {
                netLogicToNotify.ExecuteMethod("DownloadCompleteHandler", new object[] { 99 });
            }
            return;
        }
        var sourceFilePath = ResourceUri.FromProjectRelativePath(sourceFileRelativePath);
        if (!File.Exists(sourceFilePath.Uri))
        {
            Log.Error(LogicObject.BrowseName, $"Source file for download operation does not exist. Path: {sourceFilePath.Uri}");
            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Error, "File download failed: Source file does not exist.");
            if (InformationModel.Get<NetLogicObject>(netlogicToNotify) is NetLogicObject netLogicToNotify)
            {
                netLogicToNotify.ExecuteMethod("DownloadCompleteHandler", new object[] { 99 });
            }
            return;
        }
        var fileOperationRequest = new FileOperationRequest
        {
            Operation = (FileOperationRequest.OperationType)operationRequest,
            OperationID = sessionUi.DownloadFile(sourceFilePath),
            NetLogicToNotify = netlogicToNotify
        };
        pendingFileOperations.Add(fileOperationRequest);
    }

    [ExportMethod]
    public void UploadCompleteHandler(int statusCode, Guid uploadId, string uploadedFileUri)
    {
        var uploadedFile = new ResourceUri(uploadedFileUri);
        var returnPath = uploadedFile.Uri;
        var completedRequest = pendingFileOperations.Find(request => request.OperationID == uploadId);
        var operationType = 0;
        var invalidFileExtension = false;
        var transferOk = (UploadFileCompletedResultCode)statusCode == UploadFileCompletedResultCode.Success;
        NetLogicObject netLogicObject = null;        
        if (completedRequest != null)
        {
            operationType = (int)completedRequest.Operation;
            switch (completedRequest.Operation)
            {
                case FileOperationRequest.OperationType.Restore:
                case FileOperationRequest.OperationType.RestoreData:
                    // Trigger restore operation with the uploaded file URI                    
                    netLogicObject = sessionUi.Get<NetLogicObject>("BackupRestoreLogic");
                    break;
                case FileOperationRequest.OperationType.FileUpload:
                    if (InformationModel.Get<NetLogicObject>(completedRequest.NetLogicToNotify) is NetLogicObject netLogicToNotify)
                    {
                        netLogicObject= netLogicToNotify;
                    }
                    break;
            }
            if (transferOk && Path.GetExtension(uploadedFile.Uri) is string fileExtension && !string.IsNullOrEmpty(completedRequest.FileExtensionFilter))
            {
                string[] allowedExtensions = completedRequest.FileExtensionFilter.Split(',').Select(ext => ext.Trim().StartsWith('.') ? ext.Trim() : "." + ext.Trim()).ToArray();
                invalidFileExtension =!allowedExtensions.Contains(fileExtension, StringComparer.InvariantCultureIgnoreCase);                                     
            }
            // If the original upload request specified that the uploaded file should be copied to a specific destination path, perform the file copy operation now.
            // This allows the upload to complete successfully to a temporary location, and then we can move the file to the desired location on the file system.
            if (completedRequest.CopyFileToDestination && transferOk && !invalidFileExtension)
            {
                try
                {
                    var destinationFilePath = Path.Combine(completedRequest.FullDestinationPath, Path.GetFileName(uploadedFile.Uri));
                    File.Copy(uploadedFile.Uri, destinationFilePath, true);
                    Log.Info(LogicObject.BrowseName, $"Uploaded file copied to destination successfully. Destination path: {destinationFilePath}");
                    File.Delete(uploadedFile.Uri);
                    returnPath = destinationFilePath;
                }
                catch (Exception ex)
                {
                    Log.Error(LogicObject.BrowseName, $"Failed to copy uploaded file to destination. Error: {ex.Message}");
                    NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Error, "File upload succeeded but failed to move to destination. Check the log for details.");
                }
            }
            if (transferOk && invalidFileExtension)
            {
                statusCode = (int)UploadFileCompletedResultCode.ContentExtensionMismatch;
                File.Delete(uploadedFile.Uri);
            }
            pendingFileOperations.Remove(completedRequest);
        }
        else
        {
            Log.Error(LogicObject.BrowseName, $"No matching file operation request found for completed upload with ID: {uploadId}");
        }
        if ((UploadFileCompletedResultCode)statusCode != UploadFileCompletedResultCode.Success)
        {
            var messageLT = new LocalizedText(FTOptix.UI.ObjectTypes.Panel.NamespaceIndex, $"UploadFileCompletedResult{statusCode+1}Description");
            Log.Error(LogicObject.BrowseName, $"File upload failed with status code: {statusCode}. Message: {InformationModel.LookupTranslation(messageLT, ["en-US"]).Text}");
            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Error, "File upload failed: " + InformationModel.LookupTranslation(messageLT).Text);
        }
        var inputArguments = new object[] { statusCode, returnPath, operationType };
        netLogicObject?.ExecuteMethod("UploadCompleteHandler", inputArguments);
    }

    [ExportMethod]
    public void DownloadCompleteHandler(int statusCode, Guid downloadId)
    {
        var completedRequest = pendingFileOperations.Find(request => request.OperationID == downloadId);
        switch ((DownloadFileStartedResultCode)statusCode)
        {
            case DownloadFileStartedResultCode.Success:
                if (completedRequest != null)
                {
                    switch (completedRequest.Operation)
                    {
                        case FileOperationRequest.OperationType.Backup:
                        case FileOperationRequest.OperationType.BackupData:
                            Log.Info(LogicObject.BrowseName, $"Backup file downloaded successfully. Download ID: {downloadId}");
                            sessionUi.Get<NetLogicObject>("BackupRestoreLogic").ExecuteMethod("DownloadCompleteHandler", new object[] { statusCode, (int)completedRequest.Operation });
                            break;
                        case FileOperationRequest.OperationType.FileDownload:
                            Log.Info(LogicObject.BrowseName, $"File downloaded successfully. Download ID: {downloadId}");
                            if (InformationModel.Get<NetLogicObject>(completedRequest.NetLogicToNotify) is NetLogicObject netLogicToNotify)
                            {
                                netLogicToNotify.ExecuteMethod("DownloadCompleteHandler", new object[] { statusCode });
                            }
                            break;
                    }
                }
                else
                {
                    Log.Error(LogicObject.BrowseName, $"No matching file operation request found for completed download with ID: {downloadId}");
                }
                break;
            default:
                var messageLT = new LocalizedText(FTOptix.UI.ObjectTypes.Panel.NamespaceIndex, $"DownloadFileStartedResult{statusCode}Description");
                Log.Error(LogicObject.BrowseName, $"File download failed with status code: {statusCode}. Message: {InformationModel.LookupTranslation(messageLT, ["en-US"]).Text}");
                NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Error, "File download failed: " + InformationModel.LookupTranslation(messageLT).Text);
                break;
        }
    }

    private UISession sessionUi;
    private List<FileOperationRequest> pendingFileOperations = new List<FileOperationRequest>();

}
