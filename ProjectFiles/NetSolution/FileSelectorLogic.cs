#region Using directives
using System;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.UI;
using FTOptix.HMIProject;
using FTOptix.NetLogic;
using FTOptix.CoreBase;
using FTOptix.WebUI;
using FTOptix.SQLiteStore;
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
using FTOptix.OPCUAServer;
using FTOptix.MQTTClient;
using FTOptix.DataLogger;
using FTOptix.OPCUAClient;
using FTOptix.Core;
using System.IO;
using System.Xml;
using System.Collections.Generic;
using System.Linq;
#endregion

public class FileSelectorLogic : BaseNetLogic
{
    public override void Start()
    {
        ownerDialog = (Dialog)Owner;
        if (ownerDialog == null)
        {
            InitializationErrorToLog("Owner dialog is null, cannot initialize FileSelectorLogic.");
            return;
        }
        if (!InitializeDialogAliases() || !InitializeLocalVariables())
        {
            return;
        }
        if (LogicObject.GetVariable("ActualLocation") is not IUAVariable _actualLocation)
        {
            InitializationErrorToLog("ActualLocation variable not found or invalid!");
            return;
        }
        if (LogicObject.GetObject("Locations") is not IUAObject locations)
        {
            InitializationErrorToLog("Locations object not found!");
            return;
        }
        actualLocation = _actualLocation;
        InizializeUSBDevices(locations);
        InizializeEntries();
        actualLocation.VariableChange += ActualLocation_VariableChange;
    }

    public override void Stop()
    {
        if (actualLocation != null)
        {
            actualLocation.VariableChange -= ActualLocation_VariableChange;
        }
    }

    [ExportMethod]
    public void CreateFolder(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            Log.Error(LogicObject.BrowseName, "Folder name cannot be empty.");
            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Error, "Folder name cannot be empty.");
            return;
        }
        if (selectedEntry is null || string.IsNullOrWhiteSpace(selectedEntry.FileFullPath))
        {
            Log.Error(LogicObject.BrowseName, "No valid folder selected to create a new folder in.");
            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Error, "No valid folder selected to create a new folder in.");
            return;
        }
        string currentFolder = IsFile(selectedEntry.FileFullPath) ? Path.GetDirectoryName(selectedEntry.FileFullPath) : selectedEntry.FileFullPath;
        string fullPath = Path.Combine(currentFolder, folderName);
        if (Directory.Exists(fullPath))
        {
            Log.Error(LogicObject.BrowseName, $"Folder '{folderName}' already exists at {fullPath}.");
            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Warning, $"Folder '{folderName}' already exists.");
            return;
        }
        try
        {
            Directory.CreateDirectory(fullPath);
            Log.Debug(LogicObject.BrowseName, $"Folder '{folderName}' created successfully at {fullPath}.");
            folderAnalyzerTask = new LongRunningTask(UpdateDataGridEntryList, selectedEntry.EntryUri, LogicObject);
            folderAnalyzerTask.Start();
        }
        catch (Exception ex)
        {
            Log.Error(LogicObject.BrowseName, $"Error creating folder '{folderName}': {ex.Message}");
            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Error, $"Error creating folder: {ex.Message}");
        }
    }

    [ExportMethod]
    public void OpenFolder(NodeId entryListNodeId)
    {
        if (InformationModel.Get(entryListNodeId) is not FileListEntry entrySelectedVoice)
        {
            Log.Error(LogicObject.BrowseName, "Invalid entry list node ID provided.");
            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Error, "Error during open File selector, please check the log for more details.");
            return;
        }
        folderAnalyzerTask = new LongRunningTask(UpdateDataGridEntryList, entrySelectedVoice.EntryUri, LogicObject);
        folderAnalyzerTask.Start();
    }

    [ExportMethod]
    public void UpdateEntryList(NodeId entryListNodeId)
    {
        if (entryListNodeId is null || entryListNodeId == NodeId.Empty)
        {
            // As Allow Deselection is true, we can safely return here, because the user has deselected the entry.
            return;
        }
        if (InformationModel.Get(entryListNodeId) is not FileListEntry entrySelectedVoice)
        {
            Log.Debug(LogicObject.BrowseName, "Invalid entry list node ID provided.");
            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Error, "Error during open File selector, please check the log for more details.");
            return;
        }
        if (!folderSelectionMode.Value)
        {
            switch ((EntryType)entrySelectedVoice.EntryType)
            {
                case EntryType.File:
                    selectedEntryFullPath.Value = entrySelectedVoice.FileFullPath;
                    break;
                case EntryType.Folder:
                case EntryType.CurrentFolder:
                    selectedEntryFullPath.Value = string.Empty;
                    folderAnalyzerTask = new LongRunningTask(UpdateDataGridEntryList, entrySelectedVoice.EntryUri, LogicObject);
                    folderAnalyzerTask.Start();
                    break;
                default:
                    Log.Error(LogicObject.BrowseName, "Invalid entry type selected.");
                    NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Error, "Invalid entry type selected, please check the log for more details.");
                    break;
            }
        }
        else
        {
            if ((EntryType)entrySelectedVoice.EntryType == EntryType.CurrentFolder)
            {
                selectedEntryFullPath.Value = string.Empty;
                folderAnalyzerTask = new LongRunningTask(UpdateDataGridEntryList, entrySelectedVoice.EntryUri, LogicObject);
                folderAnalyzerTask.Start();
            }
        }

    }

    [ExportMethod]
    public void ConfirmSelection()
    {
        if (selectedEntry is null || string.IsNullOrWhiteSpace(selectedEntry.FileFullPath))
        {
            Log.Error(LogicObject.BrowseName, "No valid file or folder selected.");
            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Error, "No valid file or folder selected.");
            return;
        }
        // if the selected entry is a file on usb drive, need to copy to the application folder
        if (selectedEntry.EntryUri.UriType == UriType.USBRelative && (EntryType)selectedEntry.EntryType == EntryType.File)
        {
            // Request destination path from the user
            if (!newLocationForFileSelected)
            {
                Log.Debug(LogicObject.BrowseName, "Requesting destination path for file copy.");
                NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Info, $"Please select the destination folder for the file {selectedEntry.EntryName}", durationOnScreen: 3000);
                titleForSelectDestinationFolder.Value = new LocalizedText($"Select destination folder for {selectedEntry.EntryName}", "en-US");
                LogicObject.Get<MethodInvocation>("InvokeSelectFolderDestination").Invoke();
                return;
            }
            var destinationFolder = new ResourceUri(LogicObject.GetVariable("DestinationFolderForUSBFile").Value);
            var destinationPath = Path.Combine(destinationFolder.Uri, selectedEntry.EntryName);
            // If the file already exists, request confirmation to overwrite
            if (File.Exists(destinationPath) && !overwriteConfirmed)
            {
                Log.Debug(LogicObject.BrowseName, $"File {selectedEntry.EntryName} already exists in the application folder, prompting for confirmation.");
                LogicObject.Get<MethodInvocation>("InvokeConfirmOverwriteFile").Invoke();
                return;
            }
            try
            {
                File.Copy(selectedEntry.FileFullPath, destinationPath, true);
                Log.Debug(LogicObject.BrowseName, $"File copied to application folder: {destinationPath}");
                filePathVariable.Value = GenerateResourceUriFromPath(destinationPath);
            }
            catch (Exception ex)
            {
                Log.Error(LogicObject.BrowseName, $"Error copying file to application folder: {ex.Message}");
                NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Error, $"Error copying file: {ex.Message}");
                return;
            }
        }
        else
        {
            filePathVariable.Value = selectedEntry.EntryUri;
        }
        ownerDialog.Close();
    }

    [ExportMethod]
    public void ConfirmOverwriteFile()
    {
        var confirmOverwriteFileResult = (ConfirmOverwriteFileResult)LogicObject.GetVariable("OverwriteReturnResult").Value.Value;
        if (confirmOverwriteFileResult == ConfirmOverwriteFileResult.Confirmed)
        {
            overwriteConfirmed = true;
            Log.Debug(LogicObject.BrowseName, "Overwrite confirmed by user.");
            ConfirmSelection();
        }
        else if (confirmOverwriteFileResult == ConfirmOverwriteFileResult.Cancelled)
        {
            newLocationForFileSelected = false;
            LogicObject.GetVariable("DestinationFolderForUSBFile").Value = "";
            Log.Debug(LogicObject.BrowseName, "Overwrite cancelled by user.");
        }
        LogicObject.GetVariable("OverwriteReturnResult").Value = (int)ConfirmOverwriteFileResult.NoEntry;
    }

    [ExportMethod]
    public void ConfirmDestinationForUSBFile()
    {
        if (IsResourceUriValid(new ResourceUri(LogicObject.GetVariable("DestinationFolderForUSBFile").Value)))
        {
            newLocationForFileSelected = true;
            ConfirmSelection();
        }
    }

    private void ActualLocation_VariableChange(object sender, VariableChangeEventArgs e)
    {
        ResourceUri newLocation = (uint)e.NewValue switch
        {
            0 => ResourceUri.FromApplicationRelativePath(""),
            1 => ResourceUri.FromProjectRelativePath(""),
            _ => ResourceUri.FromUSBRelativePath(e.NewValue - USB_DRIVE_START_INDEX, ""),
        };
        if (!IsResourceUriValid(newLocation))
        {
            Log.Error(LogicObject.BrowseName, $"Invalid ResourceUri: {newLocation.Uri}");
            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Error, "Invalid location selected, please check the log for more details.");
            return;
        }
        overwriteConfirmed = false;
        newLocationForFileSelected = false;
        folderAnalyzerTask = new LongRunningTask(UpdateDataGridEntryList, newLocation, LogicObject);
        folderAnalyzerTask.Start();

    }

    private void InizializeEntries()
    {
        ResourceUri filePathUri;
        if (filePathVariable != null && filePathVariable.DataType == FTOptix.Core.DataTypes.ResourceUri)
        {
            filePathUri = new ResourceUri(filePathVariable.Value);
            if (Path.Exists(filePathUri.Uri))
            {
                string directroy = Path.GetDirectoryName(filePathUri.Uri);
                actualLocation.Value = filePathUri.UriType switch
                {
                    UriType.ProjectRelative => PROJECT_FOLDER_INDEX,
                    UriType.USBRelative => USB_DRIVE_START_INDEX + filePathUri.DeviceNumber,
                    _ => APPLICATION_FOLDER_INDEX,
                };
                if (IsFile(filePathUri.Uri))
                {
                    filePathUri = GenerateResourceUriFromPath(new FileInfo(filePathUri.Uri).DirectoryName);
                }
            }
            else
            {
                filePathUri = SetDefaultResourceUri();
            }
        }
        else
        {
            filePathUri = SetDefaultResourceUri();
        }
        folderAnalyzerTask = new LongRunningTask(UpdateDataGridEntryList, filePathUri, LogicObject);
        folderAnalyzerTask.Start();
    }

    private void UpdateDataGridEntryList(LongRunningTask task, object argument)
    {
        if (argument is not ResourceUri filePathUri)
        {
            Log.Error(LogicObject.BrowseName, "Invalid argument passed to UpdateDataGridEntryList.");
            return;
        }
        try
        {
            var dataGridEntryList = LogicObject.GetObject("DataGridEntryList");
            dataGridEntryList.Children.Clear();
            updateFileListRunning.Value = true;
            if (Path.Exists(filePathUri.Uri))
            {
                var currentFolder = new DirectoryInfo(filePathUri.Uri);
                bool usbRoot = IsUsbRootDirectory(filePathUri);
                // If the current directory is not the root, include a '..' entry to enable navigation to the parent directory.
                if (currentFolder.Name != "ProjectFiles" && currentFolder.Name != "ApplicationFiles" && !usbRoot)
                {
                    string parentFullPath = currentFolder.Parent?.FullName ?? currentFolder.FullName;
                    dataGridEntryList.Add(GenerateNewFileListEntry("..", parentFullPath, EntryType.CurrentFolder));
                }
                // Update the selected entry with the current folder information
                var currentEntry = GenerateNewFileListEntry(currentFolder.Name, currentFolder.FullName, EntryType.Folder);
                selectedEntry.EntryName = currentEntry.EntryName;
                selectedEntry.FileFullPath = currentEntry.FileFullPath;
                selectedEntry.FileSize = currentEntry.FileSize;
                selectedEntry.EntryUri = currentEntry.EntryUri;
                selectedEntry.EntryRelativePath = currentEntry.EntryRelativePath;
                selectedEntry.EntryType = currentEntry.EntryType;
                Log.Debug(LogicObject.BrowseName, $"Current folder: {currentFolder.FullName}");
                Log.Debug(LogicObject.BrowseName, "Searching for subdirectories in the current directory.");
                foreach (var subDirectory in Directory.GetDirectories(filePathUri.Uri))
                {
                    var subFolderInfo = new DirectoryInfo(subDirectory);
                    dataGridEntryList.Add(GenerateNewFileListEntry(subFolderInfo.Name, subFolderInfo.FullName, EntryType.Folder));
                    if (task.IsCancellationRequested)
                    {
                        Log.Debug(LogicObject.BrowseName, "Task was cancelled, stopping directory scan.");
                        return;
                    }
                }
                if (!folderSelectionMode.Value)
                {
                    Log.Debug(LogicObject.BrowseName, "Searching for files in the current directory.");
                    string extensionFilter = fileExtensionFilterVariable.Value ?? "";
                    IEnumerable<string> filesToProcess;
                    // Check if filter is empty, "*", or contains "*" - if so, get all files
                    if (string.IsNullOrWhiteSpace(extensionFilter) || extensionFilter.Contains('*'))
                    {
                        filesToProcess = Directory.EnumerateFiles(filePathUri.Uri);
                    }
                    else
                    {
                        // Parse the extension filter (assuming comma-separated like "txt,csv,docx")
                        string[] allowedExtensions = extensionFilter.Split(',').Select(ext => ext.Trim().StartsWith('.') ? ext.Trim() : "." + ext.Trim()).ToArray();
                        filesToProcess = Directory.EnumerateFiles(filePathUri.Uri).Where(file => allowedExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()));
                    }
                    foreach (var fileFound in filesToProcess)
                    {
                        var fileFoundInfo = new FileInfo(fileFound);
                        dataGridEntryList.Add(GenerateNewFileListEntry(fileFoundInfo.Name, fileFoundInfo.FullName, EntryType.File, fileFoundInfo.Length));
                        if (task.IsCancellationRequested)
                        {
                            Log.Debug(LogicObject.BrowseName, "Task was cancelled, stopping file scan.");
                            return;
                        }
                    }
                }
            }
            else
            {
                throw new DirectoryNotFoundException($"The directory {filePathUri.Uri} does not exist.");
            }
        }
        catch (Exception ex)
        {
            Log.Error(LogicObject.BrowseName, $"Error while updating data grid entries: {ex.Message}");
            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Error, "Error during update file list entry, please check the log for more details.");
        }
        updateFileListRunning.Value = false;
        task.Dispose();
    }

    private void InizializeUSBDevices(IUAObject locations)
    {
        for (uint index = 1; index <= MAX_USB_DRIVE_MAPPING; index++)
        {
            var usbTestLocation = ResourceUri.FromUSBRelativePath(index, "");
            if (IsResourceUriValid(usbTestLocation))
            {
                Log.Debug(LogicObject.BrowseName, $"USB device found at index {index} with URI: {usbTestLocation.Uri}");
                locations.Add(GenerateNewUSBLocationEntry(index));
            }
        }
    }

    private void InitializationErrorToLog(string logMessage)
    {
        Log.Error(LogicObject.BrowseName, logMessage);
        NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Error, "Error during open File selector, please check the log for more details.");
        ownerDialog.Close();
    }

    private bool InitializeDialogAliases()
    {
        if (ownerDialog.GetAlias("FilePathVariable") is not IUAVariable _filePathVariable)
        {
            InitializationErrorToLog("FilePathVariable alias not found! Or invalid argument passed");
            return false;
        }
        if (ownerDialog.GetAlias("FolderSelectionMode") is not IUAVariable _folderSelectionMode)
        {
            InitializationErrorToLog("FolderSelectionModeVariable alias not found! Or invalid argument passed");
            return false;
        }
        if (ownerDialog.GetAlias("FileExtensionFilter") is not IUAVariable _fileExtensionFilterVariable)
        {
            InitializationErrorToLog("FileExtensionFilterVariable alias not found! Or invalid argument passed");
            return false;
        }
        filePathVariable = _filePathVariable;
        folderSelectionMode = _folderSelectionMode;
        fileExtensionFilterVariable = _fileExtensionFilterVariable;
        return true;
    }

    private bool InitializeLocalVariables()
    {
        if (LogicObject.GetVariable("UpdateFileListRunning") is not IUAVariable _updateFileListRunning)
        {
            InitializationErrorToLog("UpdateFileListRunning variable not found or invalid!");
            return false;
        }
        if (LogicObject.GetVariable("SelectedEntryFullPath") is not IUAVariable _selectedEntryFullPath)
        {
            InitializationErrorToLog("SelectedEntryFullPath variable not found or invalid!");
            return false;
        }
        if (LogicObject.GetObject("SelectedEntry") is not FileListEntry _selectedEntry)
        {
            InitializationErrorToLog("SelectedEntry object not found or invalid!");
            return false;
        }
        if (LogicObject.GetVariable("TitleForSelectDestinationFolder") is not IUAVariable _titleForSelectDestinationFolder)
        {
            InitializationErrorToLog("TitleForSelectDestinationFolder variable not found or invalid!");
            return false;
        }
        selectedEntry = _selectedEntry;
        selectedEntryFullPath = _selectedEntryFullPath;
        updateFileListRunning = _updateFileListRunning;
        titleForSelectDestinationFolder = _titleForSelectDestinationFolder;
        return true;
    }

    private bool IsUsbRootDirectory(ResourceUri filePathUri)
    {
        if (filePathUri.UriType == UriType.ProjectRelative || filePathUri.UriType == UriType.ApplicationRelative)
        {
            Log.Debug(LogicObject.BrowseName, $"The provided ResourceUri {filePathUri.Uri} is a project or application relative URI.");
            return false;
        }
        for (uint index = 1; index <= MAX_USB_DRIVE_MAPPING; index++)
        {
            var usbTestLocation = ResourceUri.FromUSBRelativePath(index, "");
            if (IsResourceUriValid(usbTestLocation) && usbTestLocation.Uri == filePathUri.Uri)
            {
                Log.Debug(LogicObject.BrowseName, $"USB root directory detected for USB index {index} with URI: {filePathUri.Uri}");
                return true;
            }
        }
        Log.Debug(LogicObject.BrowseName, $"The provided ResourceUri {filePathUri.Uri} is not a valid USB root directory.");
        return false;      
    }

    private ResourceUri SetDefaultResourceUri()
    {
        Log.Debug(LogicObject.BrowseName, $"File path variable points to a non-existing path or is not set, resetting to default.");
        actualLocation.Value = 0; // Reset to default location
        return ResourceUri.FromApplicationRelativePath("");
    }

    private static IUAVariable GenerateNewUSBLocationEntry(uint usbIndex)
    {
        var newLocation = InformationModel.MakeVariable($"USB_{usbIndex}", FTOptix.Core.DataTypes.ResourceUri);
        newLocation.Value = USB_DRIVE_START_INDEX + usbIndex;
        newLocation.DisplayName = new LocalizedText($"USB Drive {usbIndex}", "en-US");
        return newLocation;
    }

    private static FileListEntry GenerateNewFileListEntry(string name, string fullPath, EntryType entryType, long fileSize = 0)
    {
        var newEntry = InformationModel.MakeObject<FileListEntry>(name);
        newEntry.EntryName = name;
        newEntry.FileSize = entryType == EntryType.File ? string.Format("{0} KB", fileSize) : string.Empty;
        newEntry.EntryType = entryType;
        newEntry.FileFullPath = fullPath;
        newEntry.EntryUri = GenerateResourceUriFromPath(fullPath);
        newEntry.EntryRelativePath = GetRelativePathFromResourceUri(newEntry.EntryUri);
        return newEntry;
    }

    private static bool IsFile(string path)
    {
        return new FileInfo(path).Exists;
    }

    private static ResourceUri GenerateResourceUriFromPath(string fullPath)
    {
        ResourceUri newResource = null;
        string applicationFolder = ResourceUri.FromApplicationRelativePath("").Uri;
        string projectFolder = ResourceUri.FromProjectRelativePath("").Uri;
        if (fullPath.Contains(applicationFolder))
        {
            newResource = ResourceUri.FromApplicationRelativePath(fullPath.Replace(applicationFolder, string.Empty).TrimStart('\\', '/'));
        }
        else if (fullPath.Contains(projectFolder))
        {
            newResource = ResourceUri.FromProjectRelativePath(fullPath.Replace(projectFolder, string.Empty).TrimStart('\\', '/'));
        }
        else
        {
            for (uint index = 1; index <= MAX_USB_DRIVE_MAPPING; index++)
            {
                var usbTestLocation = ResourceUri.FromUSBRelativePath(index, "");
                if (IsResourceUriValid(usbTestLocation) && fullPath.Contains(usbTestLocation.Uri))
                {
                    newResource = ResourceUri.FromUSBRelativePath(index, fullPath.Replace(usbTestLocation.Uri, string.Empty).TrimStart('\\', '/'));
                    break;
                }
            }
        }
        newResource ??= ResourceUri.FromAbsoluteFilePath(fullPath);
        return newResource;
    }

    private static string GetRelativePathFromResourceUri(ResourceUri resourceUri)
    {
        if (IsFile(resourceUri.Uri))
        {
            resourceUri = GenerateResourceUriFromPath(new FileInfo(resourceUri.Uri).DirectoryName);
        }
        return resourceUri.UriType switch
        {
            UriType.USBRelative => resourceUri.Uri.Replace(ResourceUri.FromUSBRelativePath(resourceUri.DeviceNumber, "").Uri, string.Empty).TrimStart('\\', '/'),
            UriType.ProjectRelative => resourceUri.Uri.Replace(ResourceUri.FromProjectRelativePath("").Uri, string.Empty).TrimStart('\\', '/'),
            UriType.ApplicationRelative => resourceUri.Uri.Replace(ResourceUri.FromApplicationRelativePath("").Uri, string.Empty).TrimStart('\\', '/'),
            _ => resourceUri.Uri.Replace(ResourceUri.FromAbsoluteFilePath("").Uri, string.Empty).TrimStart('\\', '/'),
        };
    }

    /// <summary>
    /// This method checks if the provided ResourceUri is valid by resolving its path and verifying that the directory exists.
    /// </summary>
    /// <param name="resourceUri">The resource URI to validate.</param>
    /// <returns>
    /// A boolean value indicating whether the resource URI is valid. Returns <see langword="true"/> if the directory exists, otherwise <see langword="false"/>.
    /// </returns>
    private static bool IsResourceUriValid(ResourceUri resourceUri)
    {
        // Check that the start folder resource uri can be resolved (i.e. non existing USB device or mispelled path)
        string resolvedPath;
        try
        {
            resolvedPath = resourceUri.Uri;
        }
        catch (Exception)
        {
            return false;
        }

        if (!Directory.Exists(resolvedPath))
        {
            return false;
        }
        // Return true because the ResourceUri is valid and the directory/file exists.
        return true;
    }

    Dialog ownerDialog;
    IUAVariable filePathVariable;
    IUAVariable folderSelectionMode;
    IUAVariable fileExtensionFilterVariable;
    IUAVariable updateFileListRunning;
    IUAVariable titleForSelectDestinationFolder;
    IUAVariable actualLocation;
    IUAVariable selectedEntryFullPath;
    FileListEntry selectedEntry;
    LongRunningTask folderAnalyzerTask;
    bool overwriteConfirmed;
    bool newLocationForFileSelected;

    const uint USB_DRIVE_START_INDEX = 100;
    const uint APPLICATION_FOLDER_INDEX = 0;
    const uint PROJECT_FOLDER_INDEX = 1;
    const int MAX_USB_DRIVE_MAPPING = 5;

    public enum EntryType
    {
        File = 0,
        Folder = 1,
        CurrentFolder = 2
    }
}
