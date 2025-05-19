#region Using directives
using System;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.UI;
using FTOptix.HMIProject;
using FTOptix.NetLogic;
using FTOptix.WebUI;
using FTOptix.DataLogger;
using FTOptix.OPCUAServer;
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
using FTOptix.Retentivity;
using FTOptix.CoreBase;
using FTOptix.CommunicationDriver;
using FTOptix.Store;
using FTOptix.OPCUAClient;
using FTOptix.Core;
using System.Collections.Generic;
using System.IO;
using FTOptix.SQLiteStore;
using L5Sharp;
using L5Sharp.Core;
using CsvHelper;
using FTOptix.MQTTBroker;
using FTOptix.NativeUI;
using FTOptix.AuditSigning;
using System.Linq;
using System.Threading;
using FTOptix.System;
using FTOptix.InfluxDBStore;
using FTOptix.InfluxDBStoreLocal;
using FTOptix.ODBCStore;
using FTOptix.InfluxDBStoreRemote;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Collections.Immutable;
using System.ComponentModel.Design;
using System.Runtime.CompilerServices;
#endregion

public class TagImporterUIObjectLogic : BaseNetLogic
{
    public override void Start()
    {
        closeDialogWithError = true;        
        if (Owner is not Dialog)
        {
            Log.Error(LogicObject.BrowseName, "Owner is not a Dialog!");
            return;
        }
        ownerDialog = (Dialog)Owner;
        tagsTable = Owner.Find<ColumnLayout>("TagsTable");
        if (tagsTable == null)
        {
            Log.Error(LogicObject.BrowseName, "Unable to found the Tags table");
            ownerDialog.Close();
        }
        currentPageVariable = LogicObject.GetVariable("CurrentPage");
        if (currentPageVariable == null)
        {
            Log.Error(LogicObject.BrowseName, "Unable to found CurrentPage variable");
            ownerDialog.Close();
        }
        importJobRunningVariable = LogicObject.GetVariable("ImportJobRunning");
        if (importJobRunningVariable == null)
        {
            Log.Error(LogicObject.BrowseName, "Unable to found ImportJobRunning variable");
            ownerDialog.Close();
        }
        filterStringVariable = LogicObject.GetVariable("FilterString");
        if (filterStringVariable == null)
        {
            Log.Error(LogicObject.BrowseName, "Unable to found FilterString variable");
            ownerDialog.Close();
        }
        plcStation = LogicObject.GetAlias("TagImporterCommStation");
        if (plcStation == null)
        {
            Log.Error(LogicObject.BrowseName, "Unable to found Plc station");
            ownerDialog.Close();
        }
        selectAllVariableValue = Owner.Find<CheckBox>("SelectAllVariableValue");
        if (selectAllVariableValue == null)
        {
            Log.Error(LogicObject.BrowseName, "Unable to found SelectAllVariableValue CheckBox");
            ownerDialog.Close();
        }
        closeDialogWithError = false;
        switch (plcStation)
        {
            case FTOptix.S7TiaProfinet.Station:
                Owner.Find<Item>("ImportTypeString").Enabled = false;
                Owner.Find<Switch>("ImportTypeValue").Checked = true;
                break;
            case FTOptix.TwinCAT.Station:
            case FTOptix.RAEtherNetIP.Station:
                Owner.Find<Item>("ImportTypeString").Enabled = true;
                Owner.Find<Switch>("ImportTypeValue").Enabled = true;
                break;
        }
        jobReadTagsImported = new LongRunningTask(ReadTagsImported, LogicObject);
        currentPageVariable.VariableChange += CurrentPageVariable_VariableChange;
        filterStringVariable.VariableChange += FilterStringVariable_VariableChange;
        tagsReadFromSource = [];
        tagsReadFromSourceToDisplay = [];
        tagsImported = [];
        jobReadTagsImported.Start();
    }

    public override void Stop()
    {
        if (closeDialogWithError)
        {
            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Error, "Critical error - Unable to open tag importer function, check application logs", durationOnScreen: 5000);
        }
        if (currentPageVariable != null)
        {
            currentPageVariable.VariableChange -= CurrentPageVariable_VariableChange;
        }
        if (filterStringVariable != null)
        {
            filterStringVariable.VariableChange -= FilterStringVariable_VariableChange;
        }
        CommonLogic.DisposeTask(jobImportFromFile);
        CommonLogic.DisposeTask(jobReadTagsImported);
    }

    [ExportMethod]
    public void SetCheckedStatus (bool checkedValue)
    {
        foreach (var tagRow in tagsTable.GetNodesByType<TagCustomGridRow>())
        {
            var tagRowData = tagRow.GetAlias("RowData") as TagCustomGridRowData;
            if (tagRow.Visible)
            {
                tagRowData.Checked = checkedValue;
            }
        }
    }

    [ExportMethod]
    public void ReadTags(string filePath, bool onlineImport)
    {
        if (onlineImport)
        {
            if (plcStation is null)
            {
                Log.Warning("No PLC!!");
                NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Error, "Missing the PLC node in the project! Close the window!", durationOnScreen: 5000);
                return;
            }
            jobImportFromController = new LongRunningTask(ReadFromController, LogicObject);
            jobImportFromController.Start();
        }
        else
        {
            if (filePath.StartsWith('%'))
            {
                filePath = new ResourceUri(filePath).Uri;
            }
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                jobImportFromFile = new LongRunningTask(ReadFromFile, filePath, LogicObject);
                jobImportFromFile.Start();
            }
            else
            {
                Log.Warning(LogicObject.BrowseName, "Missing filePath or file not exist!");
                NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Warning, "Missing the file path or file not exist!");
            }
        }
    }

    [ExportMethod]
    public void SaveAndClose(bool onlineImport)
    {
        if (onlineImport)
        {
            tagsCheckedToImport = [];
            var newTagsToImport = from fullTagsRead in tagsReadFromSource
                                  where fullTagsRead.Checked
                                  select fullTagsRead;
            tagsCheckedToImport.AddRange(newTagsToImport);
            ImportFromBrowse();
        }
        else
        {
            CreateOrUpdateTags();
        }
        ownerDialog.Close();
    }

    private void CurrentPageVariable_VariableChange(object sender, VariableChangeEventArgs e)
    {
        if (e.NewValue <= LogicObject.GetVariable("TotalPages").Value)
        {
            ChangeCurrentPage(e.NewValue);
        }
    }

    private void ChangeCurrentPage(int newPage)
    {
        bool allChecked = true;
        int variableDataOffset = (newPage - 1) * 16;
        for (int rowIndex = 0; rowIndex < 16; rowIndex++)
        {
            int variableDataIndex = rowIndex + variableDataOffset;
            TagCustomGridRow tableRow = tagsTable.Get<TagCustomGridRow>($"TagCustomGridRow{rowIndex + 1}");
            if (variableDataIndex < tagsReadFromSourceToDisplay.Count)
            {
                tableRow.SetAlias("RowData", tagsReadFromSourceToDisplay[variableDataIndex]);
                tableRow.Visible = true;
                allChecked &= tagsReadFromSourceToDisplay[variableDataIndex].Checked;
            }
            else
            {
                tableRow.Visible = false;
            }
        }
        selectAllVariableValue.Checked = allChecked;
    }

    private void FilterStringVariable_VariableChange(object sender, VariableChangeEventArgs e)
    {
        if (!importJobRunningVariable.Value)
        {
            tagsReadFromSourceToDisplay.Clear();
            if (string.IsNullOrEmpty(e.NewValue))
            {
                tagsReadFromSourceToDisplay.AddRange(tagsReadFromSource);
            }
            else
            {
                tagsReadFromSourceToDisplay.AddRange(tagsReadFromSource.Where(x => x.VariableName.StartsWith(e.NewValue, StringComparison.InvariantCultureIgnoreCase) || x.VariableName.Contains(e.NewValue, StringComparison.InvariantCultureIgnoreCase)));
            }
            UpdateDataGrid();                       
        }
    }

    private void ReadFromController()
    {
        try
        {
            importJobRunningVariable.Value = true;
            filterStringVariable.Value = "";
            Struct[] prototypeItems;
            switch (plcStation)
            {
                case FTOptix.S7TiaProfinet.Station profinetStation:
                    profinetStation.Browse(out plcItems, out prototypeItems);
                    break;
                case FTOptix.RAEtherNetIP.Station raEIPStation:
                    raEIPStation.Browse(out plcItems, out prototypeItems);
                    break;
                case FTOptix.TwinCAT.Station twinCatStation:
                    throw new MissingMethodException("TwinCAT Online import method not implemented"); // TO DO vFuture
                default:
                    throw new NotSupportedException("Station not support online import");
            }
            tagsReadFromSource = GetDataFromBrowsedPlcItem();
            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Info, $"Read {tagsReadFromSource.Count} tags from PLC");
            UpdateDataGrid(true);
        }
        catch (InvalidDataException)
        {
            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Warning, "No valid data sent from PLC");
            Log.Warning(LogicObject.BrowseName, "No valid data sent from PLC - Empty response from browse");
        }
        catch (Exception ex)
        {
            Log.Error(LogicObject.BrowseName, $"{ex.Message} \n {ex.StackTrace}");
            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Error, ex.Message);
        }
        importJobRunningVariable.Value = false;
    }

    private void ReadFromFile(LongRunningTask task, object arguments)
    {
        string sourceFilePath = (string)arguments;
        try
        {
            importJobRunningVariable.Value = true;
            filterStringVariable.Value = "";
            if (plcStation == null)
            {
                throw new Exception("PlcStation alias is null!");
            }
            switch (GetJobToRun(sourceFilePath, plcStation))
            {
                case JobToRun.Invalid:
                    throw new InvalidDataException("Unable to match source file extension with plc station");                
                case JobToRun.RAEipL5X:
                    tagsReadFromSource = GetDataFromL5X(sourceFilePath);
                    break;
                case JobToRun.RAEipCSV:
                case JobToRun.S7TCPCSV:                   
                case JobToRun.ModbusCSV:
                    tagsReadFromSource = GetDataFromCSV(sourceFilePath, plcStation);
                    break;
                case JobToRun.ModbusTPY:
                    // TO DO
                    throw new MissingMethodException("Modbus TPY method not implemented");
            }
            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Info, $"Read {tagsReadFromSource.Count} tags from file {Path.GetFileName(sourceFilePath)}");
            UpdateDataGrid(true);
        }
        catch (InvalidDataException)
        {
            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Warning, "No valid data read from file");
            Log.Warning(LogicObject.BrowseName, $"No valid data sent from PLC - No valid data from file {sourceFilePath}");
        }
        catch (Exception ex)
        {
            Log.Error(LogicObject.BrowseName, $"{ex.Message} \n {ex.StackTrace}");
            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Error, ex.Message); 
        }
        importJobRunningVariable.Value = false;
    }

    private void UpdateDataGrid(bool initListToDisplay = false)
    {
        if (initListToDisplay)
        {
            tagsReadFromSourceToDisplay.Clear();
            tagsReadFromSourceToDisplay.AddRange(tagsReadFromSource);
        }
        if (tagsReadFromSourceToDisplay == null || tagsReadFromSourceToDisplay.Count == 0)
        {
            throw new InvalidDataException("No valid data read from file");
        }
        int totalPages = tagsReadFromSourceToDisplay.Count / 16;
        if (tagsReadFromSourceToDisplay.Count % 16 > 0)
        {
            totalPages++;
        }
        LogicObject.GetVariable("TotalPages").Value = totalPages;
        if (currentPageVariable.Value == 1)
        {
            ChangeCurrentPage(1);
        }
        else
        {
            currentPageVariable.Value = 1;
        }        
    }

    private static JobToRun GetJobToRun(string filePath, IUANode plcStation)
    {
        string fileExtension = Path.GetExtension(filePath).ToLower().TrimStart('.');
        return plcStation switch
        {
            FTOptix.Modbus.Station => fileExtension switch
            {
                "csv" => JobToRun.ModbusCSV,
                "tpy" => JobToRun.ModbusTPY,
                _ => JobToRun.Invalid,
            },
            FTOptix.S7TCP.Station => fileExtension == "csv" ? JobToRun.S7TCPCSV : JobToRun.Invalid,
            FTOptix.RAEtherNetIP.Station => fileExtension switch
            {
                "csv" => JobToRun.RAEipCSV,
                "l5x" => JobToRun.RAEipL5X,
                _ => JobToRun.Invalid,
            },
            _ => JobToRun.Invalid,
        };
    }

    private List<TagDataFromCSV> ReadRawDataFromCSV(string filePath)
    {

        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            filePath ??= "";
            throw new FileNotFoundException($"File {filePath} does not exist.");
        }
        using var streamReader = new StreamReader(filePath);
        using var csvReader = new CsvReader(streamReader, CultureInfo.InvariantCulture);
        return csvReader.GetRecords<TagDataFromCSV>().ToList();
    }

    private List<TagCustomGridRowData> GetDataFromCSV(string filePath, IUANode plcStation)
    {
        List<TagCustomGridRowData> returnValue = [];
        try
        {
            int i = 0;
            var ownerCommunicationDriver = (CommunicationDriver)plcStation.Owner;
            foreach (var record in ReadRawDataFromCSV(filePath).Where(x => x.Driver == CommonLogic.CSVDriverMapping.First(y => y.Value == ownerCommunicationDriver.ObjectType.NodeId).Key))
            {             
                var newTagData = ReadCSVTag(record, i.ToString());
                if (newTagData != null)
                {
                    returnValue.Add(newTagData);
                    i++;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(LogicObject.BrowseName, ex.Message);
        }
        return returnValue;
    }

    private List<TagCustomGridRowData> GetDataFromL5X(string filePath)
    {
        List<TagCustomGridRowData> returnValue = [];
        try
        {
            var l5xSourceFile = L5X.Load(filePath);
            int i = 0;
            foreach (var tag in l5xSourceFile.Tags)
            {
                var newTagData = ReadL5XTag(tag, "Controller", i.ToString());
                if (newTagData != null)
                {
                    returnValue.Add(newTagData);
                    i++;
                }
            }
            foreach (var program in l5xSourceFile.Programs)
            {
                foreach (var tag in program.Tags)
                {
                    var newTagData = ReadL5XTag(tag, program.Name, i.ToString());
                    if (newTagData != null)
                    {
                        returnValue.Add(newTagData);
                        i++;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(LogicObject.BrowseName, ex.Message);
        }
        return returnValue;
    }

    private TagCustomGridRowData ReadCSVTag(TagDataFromCSV tagData, string browseName)
    {
        try
        {
            TagCustomGridRowData newTagData = InformationModel.MakeObject<TagCustomGridRowData>(browseName);
            newTagData.Checked = tagsImported.Exists(x => x.BrowseName == tagData.Name);
            newTagData.VariableName = tagData.Name;
            newTagData.VariableDataType = tagData.DataType;
            newTagData.VariableDataTypeNodeId = CommonLogic.CSVDataTypeMapping.GetValueOrDefault(tagData.DataType, NodeId.Empty);
            newTagData.VariableComment = tagData.Description ?? string.Empty;
            newTagData.VariableAddress = tagData.Address;
            newTagData.VariableIsArray = !string.IsNullOrEmpty(tagData.ArrayDimension);
            if (newTagData.VariableIsArray)
            {
                List<uint> dimensions = [];
                if (tagData.ArrayDimension.Contains('-'))
                {
                    foreach (var dimension in tagData.ArrayDimension.Split('-'))
                    {
                        dimensions.Add(uint.Parse(dimension));
                    }
                }
                else
                {
                    dimensions.Add(uint.Parse(tagData.ArrayDimension));
                }
                newTagData.VariableArrayDimension = [.. dimensions];
            }
            if (ushort.TryParse(tagData.StringLength, out ushort stringLength))
            {
                newTagData.VariableStringLenght = stringLength;
            }
            return newTagData;
        }
        catch (Exception ex)
        {
            Log.Error(LogicObject.BrowseName, ex.Message);
            return null;
        }
    }

    private TagCustomGridRowData ReadL5XTag(L5Sharp.Core.Tag tagData, string scopeName, string browseName)
    {
        try
        {
            TagCustomGridRowData newTagData = InformationModel.MakeObject<TagCustomGridRowData>(browseName);
            newTagData.Checked = tagsImported.Exists(x => x.BrowseName == tagData.Name);
            newTagData.VariableDataType = tagData.DataType;
            newTagData.VariableDataTypeNodeId = CommonLogic.LogixDataTypeMapping.GetValueOrDefault(tagData.DataType, NodeId.Empty);
            if (newTagData.VariableDataTypeNodeId is null || newTagData.VariableDataTypeNodeId == NodeId.Empty)
            {
                return null;
            }
            newTagData.VariableComment = tagData.Description ?? string.Empty;
            string variablePath;
            if (scopeName.Equals("controller", StringComparison.InvariantCultureIgnoreCase))
            {
                variablePath = tagData.Name;
            }
            else
            {
                variablePath = $"Program:{scopeName}.{tagData.Name}";
            }
            newTagData.VariableName = variablePath;
            newTagData.VariableAddress = variablePath;
            newTagData.VariableIsArray = !tagData.Dimensions.IsEmpty;
            if (!tagData.Dimensions.IsEmpty)
            {
                List<uint> dimensions = [tagData.Dimensions.X];
                if (tagData.Dimensions.IsMultiDimensional)
                {
                    dimensions.Add(tagData.Dimensions.Y);
                    if (tagData.Dimensions.Z > 0)
                    {
                        dimensions.Add(tagData.Dimensions.Z);
                    }
                }
                newTagData.VariableArrayDimension = [.. dimensions];
            }
            return newTagData;
        }
        catch (Exception ex)
        {
            Log.Error(LogicObject.BrowseName, ex.Message);
            return null;
        }
    }

    private TagCustomGridRowData ReadS7ProfinetTag(FTOptix.CommunicationDriver.TagInfo tagInfo, string browseName)
    {
        try
        {
            var symbolName = tagInfo.TagAttributes.First(x => x.Name.Equals("SymbolName", StringComparison.InvariantCultureIgnoreCase));
            string tagAddress = symbolName.Value.ToString();
            string tagName = tagAddress.Replace("PLC.TAGS.", string.Empty, StringComparison.InvariantCultureIgnoreCase).Replace("PLC.BLOCKS.", string.Empty, StringComparison.InvariantCultureIgnoreCase);
            TagCustomGridRowData newTagData = InformationModel.MakeObject<TagCustomGridRowData>(browseName);
            newTagData.Checked = tagsImported.Exists(x => x.BrowseName == tagName);
            newTagData.VariableName = tagName;
            newTagData.VariableDataType = InformationModel.Get(tagInfo.DataType).BrowseName;
            newTagData.VariableDataTypeNodeId = tagInfo.DataType;
            newTagData.VariableComment = string.Empty;
            newTagData.VariableAddress = tagAddress;
            newTagData.VariableIsArray = tagInfo.ArrayDimensions.Length > 0;
            newTagData.VariableArrayDimension = tagInfo.ArrayDimensions;
            if (tagInfo.DataType == OpcUa.DataTypes.String && tagInfo.TagAttributes.FirstOrDefault(x => x.Name.Equals("MaximumLength", StringComparison.InvariantCultureIgnoreCase), null) is TagAttribute maximumLenght)
            {
                newTagData.VariableStringLenght = Convert.ToUInt16(maximumLenght.Value);
            }
            return newTagData;
        }
        catch (Exception ex)
        {
            Log.Error(LogicObject.BrowseName, ex.Message);
            return null;
        }
    }

    private TagCustomGridRowData ReadRAEIPTag(FTOptix.CommunicationDriver.TagInfo tagInfo, string browseName)
    {
        try
        {
            var symbolName = tagInfo.TagAttributes.First(x => x.Name.Equals("SymbolName", StringComparison.InvariantCultureIgnoreCase));
            string tagAddress = symbolName.Value.ToString();
            TagCustomGridRowData newTagData = InformationModel.MakeObject<TagCustomGridRowData>(browseName);
            newTagData.Checked = tagsImported.Exists(x => x.BrowseName == tagAddress);
            newTagData.VariableName = tagAddress;
            newTagData.VariableDataType = InformationModel.Get(tagInfo.DataType).BrowseName;
            newTagData.VariableDataTypeNodeId = tagInfo.DataType;
            newTagData.VariableComment = string.Empty;
            newTagData.VariableAddress = tagAddress;
            newTagData.VariableIsArray = tagInfo.ArrayDimensions.Length > 0;
            newTagData.VariableArrayDimension = tagInfo.ArrayDimensions;
            if (tagInfo.DataType == OpcUa.DataTypes.String && tagInfo.TagAttributes.FirstOrDefault(x => x.Name.Equals("MaximumLength", StringComparison.InvariantCultureIgnoreCase), null) is TagAttribute maximumLenght)
            {
                newTagData.VariableStringLenght = Convert.ToUInt16(maximumLenght.Value);
            }
            return newTagData;
        }
        catch (Exception ex)
        {
            Log.Error(LogicObject.BrowseName, ex.Message);
            return null;
        }
    }

    private List<TagCustomGridRowData> GetDataFromBrowsedPlcItem()
    {
        List<TagCustomGridRowData> returnValue = [];
        try
        {            
            foreach (var plcItem in plcItems.OfType<BasePlcItem>().ToList())
            {
                ReadFromBrowsedPlc(plcItem, ref returnValue);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex.Message);
        }
        return returnValue;
    }

    private void ReadFromBrowsedPlc(BasePlcItem itemToAnalyze, ref List<TagCustomGridRowData> myList)
    {
        switch (itemToAnalyze)
        {
            case FTOptix.CommunicationDriver.StructureInfo:
            case FTOptix.CommunicationDriver.GenericItem:
                foreach (var item in itemToAnalyze.Items)
                {
                    ReadFromBrowsedPlc(item, ref myList);
                }
                break;
            case FTOptix.CommunicationDriver.TagInfo tagInfo:
                string rowBrowseName = myList.Count > 0 ? $"{myList.Count - 1}" : "0";
                TagCustomGridRowData dataToAdd = plcStation switch
                {
                    FTOptix.S7TiaProfinet.Station => ReadS7ProfinetTag(tagInfo, rowBrowseName),
                    FTOptix.RAEtherNetIP.Station => ReadRAEIPTag(tagInfo, rowBrowseName),
                    FTOptix.TwinCAT.Station => null,
                    _ => null
                };
                if (dataToAdd != null)
                {
                    myList.Add(dataToAdd);
                }
                break;
        }
    }

    private void ReadTagsImported()
    {
        tagsImported = PopulateListTagImported(plcStation.Get<Folder>("Tags"));
    }

    private List<TagDataImported> PopulateListTagImported(Folder tagsFolder)
    {
        List<TagDataImported> returnValue = [];
        if (plcStation != null)
        {
            foreach (var tag in tagsFolder.Children.Where(x => x.NodeClass == NodeClass.Variable).Cast<IUAVariable>())
            {
                var newTagData = new TagDataImported
                {
                    BrowseName = tag.BrowseName,
                    NodeId = tag.NodeId,
                    Description = tag.Description.Text,
                    DataType = tag.DataType,
                    ArrayDimensions = tag.ArrayDimensions
                };
                returnValue.Add(newTagData);
            }
            foreach (var subfolder in tagsFolder.GetNodesByType<Folder>())
            {
                returnValue.AddRange(PopulateListTagImported(subfolder));
            }
        }
        return returnValue;
    }
    #region Generate tags into the station
    private void CreateOrUpdateTags()
    {
        var tagsToImport = tagsReadFromSource.Where(x => x.Checked);
        foreach (var tagData in tagsToImport)
        {
            switch (plcStation)
            {
                case FTOptix.RAEtherNetIP.Station:
                    CreateOrUpdateRAEthernetIPTag(tagData);
                    break;
                case FTOptix.S7TCP.Station:
                    CreateOrUpdateS7TCPTag(tagData);
                    break;
                case FTOptix.Modbus.Station:
                    CreateOrUpdateModbusTag(tagData);
                    break;
                case FTOptix.MelsecFX3U.Station:
                    CreateOrUpdateMelsecFX3UTag(tagData);
                    break;
            }
        }
        var deletedTags = DeleteMissingTag(tagsReadFromSource.Where(x => !x.Checked).ToList());
        NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Success, $"Imported {tagsToImport.Count()}, removed {deletedTags} tags");
    }

    private int DeleteMissingTag(List<TagCustomGridRowData> tagDatasUnchecked)
    {
        int deletedTagsCounter = 0;
        foreach (var tagData in tagsImported.Where(x => tagDatasUnchecked.Exists(y => y.VariableName == x.BrowseName)))
        {
            InformationModel.Get(tagData.NodeId)?.Delete();
            deletedTagsCounter++;
        }
        return deletedTagsCounter;
    }

    private void CreateOrUpdateRAEthernetIPTag(TagCustomGridRowData tagData)
    {
        Folder destinationFolder = plcStation.Get<Folder>("Tags");
        FTOptix.RAEtherNetIP.Tag targetTag = destinationFolder.Get<FTOptix.RAEtherNetIP.Tag>(tagData.VariableName);        
        if (targetTag == null)
        {
            targetTag = MakeTag<FTOptix.RAEtherNetIP.Tag>(tagData.VariableName, tagData.VariableDataTypeNodeId, FTOptix.RAEtherNetIP.VariableTypes.Tag, tagData.VariableIsArray, tagData.VariableArrayDimension, tagData.VariableComment);
            destinationFolder.Add(targetTag);
        }
        else if (targetTag.DataType != tagData.VariableDataTypeNodeId)
        {
            targetTag.DataType = tagData.VariableDataTypeNodeId;
        }
        targetTag.SymbolName = tagData.VariableAddress;
    }

    private void CreateOrUpdateS7TCPTag(TagCustomGridRowData tagData)
    {
        var destinationFolder = plcStation.GetObject("Tags");
        var (dataBlockIndex, byteIndex, bitIndex, memoryArea) = S7ParseAddress(tagData.VariableAddress);
        if (memoryArea == FTOptix.S7TCP.MemoryArea.DataBlock)
        {
            tagData.VariableName = $"DB{dataBlockIndex}.{tagData.VariableName}";
        }
        FTOptix.S7TCP.Tag targetTag = destinationFolder.Get<FTOptix.S7TCP.Tag>(tagData.VariableName);
        NodeId dataType = CommonLogic.CSVDataTypeMapping.GetValueOrDefault(tagData.VariableDataType, OpcUa.DataTypes.Int32);
        if (targetTag == null)
        {
            targetTag = MakeTag<FTOptix.S7TCP.Tag>(tagData.VariableName, dataType, FTOptix.S7TCP.VariableTypes.Tag, tagData.VariableIsArray, tagData.VariableArrayDimension, tagData.VariableComment);
            destinationFolder.Add(targetTag);
        }
        else if (targetTag.DataType != dataType)
        {
            targetTag.DataType = dataType;
        }
        targetTag.MemoryArea = memoryArea;
        targetTag.BlockNumber = dataBlockIndex;
        targetTag.Position = byteIndex;
        targetTag.Bit = bitIndex;
        switch (targetTag.DataType)
        {
            case var _ when targetTag.DataType == OpcUa.DataTypes.String:
                targetTag.Encoding = FTOptix.S7TCP.Encoding.ExtendedString;
                targetTag.MaximumLength = tagData.VariableStringLenght;
                break;
            case var _ when targetTag.DataType == OpcUa.DataTypes.DateTime:
                targetTag.Encoding = FTOptix.S7TCP.Encoding.DateOnly;
                break;
            case var _ when targetTag.DataType == OpcUa.DataTypes.UtcTime:
                targetTag.Encoding = FTOptix.S7TCP.Encoding.LTimeOfDay;
                break;
            case var _ when targetTag.DataType == OpcUa.DataTypes.Duration:
                targetTag.Encoding = FTOptix.S7TCP.Encoding.LTime;
                break;
        }
    }

    private void CreateOrUpdateModbusTag(TagCustomGridRowData tagData)
    {
        Folder destinationFolder = plcStation.Get<Folder>("Tags");
        FTOptix.Modbus.Tag targetTag = destinationFolder.Get<FTOptix.Modbus.Tag>(tagData.VariableName);
        NodeId dataType = CommonLogic.CSVDataTypeMapping.GetValueOrDefault(tagData.VariableDataType, OpcUa.DataTypes.Int32);
        ParseByteBitAddress(tagData.VariableAddress, out uint byteIndex, out uint bitIndex);
        if (targetTag == null)
        {
            targetTag = MakeTag<FTOptix.Modbus.Tag>(tagData.VariableName, dataType, FTOptix.Modbus.VariableTypes.Tag, tagData.VariableIsArray, tagData.VariableArrayDimension, tagData.VariableComment);
            destinationFolder.Add(targetTag);
        }
        else if (targetTag.DataType != dataType)
        {
            targetTag.DataType = dataType;
        }
        targetTag.MemoryArea = ModbusMemoryAreaMapping.FirstOrDefault(x => tagData.VariableAddress.StartsWith(x.Key), new KeyValuePair<string, ModbusMemoryArea>("", ModbusMemoryArea.HoldingRegister)).Value;
        targetTag.NumRegister = (ushort)byteIndex;
        targetTag.BitOffset = bitIndex;
        if (dataType == OpcUa.DataTypes.String && tagData.VariableStringLenght > 0)
        {
            targetTag.MaximumLength = tagData.VariableStringLenght;
        }
    }

    private void CreateOrUpdateMelsecFX3UTag(TagCustomGridRowData tagData)
    {
        throw new NotImplementedException("Missing MelsecFX3 support");
    }

    private void ImportFromBrowse()
    {
        Struct[] tagsToImport = FilterTagsToImport();
        Struct[] prototypeItems = []; // Issue for runtime Datatype on retentivity, i will save only base tag, not structure
        var tagsFolder = plcStation.Get("Tags");
        // Runtime import dosen't support a Tags folder not empty, we need to rebuild the link with datalogger and OPC-UA
        List<TagsToReconnect> tagsToReconnects = [];
        foreach (var tag in tagsFolder.GetNodesByType<IUAVariable>())
        {
            foreach (var targetNode in tag.InverseRefs.GetNodes(FTOptix.Core.ReferenceTypes.Resolves))
            {
                tagsToReconnects.Add(new TagsToReconnect()
                {
                    TargetDynamicLink = targetNode.NodeId,
                    TargetDynamicLinkMode = (targetNode as DynamicLink).Mode,
                    SourceTagBrowseName = tag.BrowseName,
                    ReferenceType = FTOptix.Core.ReferenceTypes.Resolves,                   
                });
            }
        }
        tagsFolder.Children.Clear();
        switch (plcStation)
        {
            case FTOptix.S7TiaProfinet.Station tiaStation:
                tiaStation.Import(tagsToImport, prototypeItems);
                break;
            case FTOptix.RAEtherNetIP.Station raEIPStation:
                raEIPStation.Import(tagsToImport, prototypeItems);
                break;
        }
        // reconnect all new tags to existing broken dynamiclink
        foreach (var tagInformation in tagsToReconnects)
        {
            if (InformationModel.Get(tagInformation.TargetDynamicLink) is DynamicLink targetDynamicLink && tagsFolder.GetVariable(tagInformation.SourceTagBrowseName) is IUAVariable sourceTag)
            {
                (targetDynamicLink.Owner as IUAVariable).Stop(); // Stop the target variable for avoid default value in logger
                targetDynamicLink.Refs.AddReference(tagInformation.ReferenceType, sourceTag);
                targetDynamicLink.Mode = tagInformation.TargetDynamicLinkMode;
                (targetDynamicLink.Owner as IUAVariable).Start();
            }
        }
        // calculate the tags not deleted from the import
        var tagImportedHashSet = new HashSet<string>(tagsImported.Select(x => x.BrowseName));
        var tagsCheckedToImportHashSet = new HashSet<string>(tagsCheckedToImport.Select(x => x.VariableName));
        tagImportedHashSet.IntersectWith(tagsCheckedToImportHashSet);
        var tagsNotDeleted = tagImportedHashSet.Count;
        // calculate only the new element imported
        tagImportedHashSet = new HashSet<string>(tagsImported.Select(x => x.BrowseName));
        tagsCheckedToImportHashSet = new HashSet<string>(tagsCheckedToImport.Select(x => x.VariableName));
        tagImportedHashSet.ExceptWith(tagsCheckedToImportHashSet);
        tagsCheckedToImportHashSet.ExceptWith(new HashSet<string>(tagsImported.Select(x => x.BrowseName)));      
        NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Success, $"Imported {tagsCheckedToImportHashSet.Count}, removed {tagsImported.Count - tagsNotDeleted} tags");
    }

    private T MakeTag<T>(string browseName, NodeId dataType, NodeId variableType, bool isArray, uint[] arrayDimension, string description)
    {
        // if (isArray)
        // {
        //     return (T)InformationModel.MakeVariable(browseName, dataType, variableType, arrayDimension);
        // }
        // else
        // {
        //     return (T)InformationModel.MakeVariable(browseName, dataType, variableType);
        // }
        var newTag = isArray ? InformationModel.MakeVariable(browseName, dataType, variableType, arrayDimension) : InformationModel.MakeVariable(browseName, dataType, variableType);
        newTag.Description = new LocalizedText(description, Session.ActualLocaleId);
        return (T)newTag;
    }

    private static void ParseByteBitAddress(string rawAddress, out uint byteIndex, out uint bitIndex)
    {
        bitIndex = 0u;
        var regexAddressFormat = Regex.Match(rawAddress, @"\d+(\.\d{1,2})?");
        if (regexAddressFormat.Success)
        {
            var addressParts = regexAddressFormat.Value.Split('.');
            byteIndex = uint.Parse(addressParts[0]);
            if (addressParts.Length > 1)
            {
                bitIndex = uint.Parse(addressParts[1]);
            }
        }
        else
        {
            _ = uint.TryParse(rawAddress, out byteIndex);
        }
    }

    private static (uint dataBlockIndex, uint byteIndex, uint bitIndex, FTOptix.S7TCP.MemoryArea memoryArea) S7ParseAddress(string address)
    {
        // Default return a Datablock memory area
        FTOptix.S7TCP.MemoryArea memoryArea = S7MemoryAreaMapping.FirstOrDefault(x => address.StartsWith(x.Key), new KeyValuePair<string, FTOptix.S7TCP.MemoryArea>("", FTOptix.S7TCP.MemoryArea.DataBlock)).Value;
        var dataBlockIndex = 0u;
        string rawAddress = address;
        if (memoryArea == FTOptix.S7TCP.MemoryArea.DataBlock)
        {
            var dbMatch = Regex.Match(rawAddress, @"DB(\d+)");
            if (dbMatch.Success && dbMatch.Groups.Count == 2)
            {
                dataBlockIndex = uint.Parse(dbMatch.Groups[1].Value);
                rawAddress = address.Replace(dbMatch.Groups[0].Value, string.Empty);
            }
        }
        ParseByteBitAddress(rawAddress, out uint byteIndex, out uint bitIndex);
        return (dataBlockIndex, byteIndex, bitIndex, memoryArea);
    }

    private Struct[] FilterTagsToImport()
    {
        List<Struct> returnValue = [];
        try
        {
            var plcRoot = plcItems.OfType<BasePlcItem>().ToList();
            foreach (var plcBaseItem in plcRoot)
            {
                GenerateListToImport(plcBaseItem, ref returnValue);
            }
        }
        catch (Exception ex)
        {
            Log.Error(LogicObject.BrowseName, ex.Message);
        }
        return [.. returnValue];
    }

    private void GenerateListToImport(BasePlcItem itemToAnalyze, ref List<Struct> myList)
    {
        switch (itemToAnalyze)
        {
            case FTOptix.CommunicationDriver.StructureInfo:
            case FTOptix.CommunicationDriver.GenericItem:
                foreach (var item in itemToAnalyze.Items)
                {
                    GenerateListToImport(item, ref myList);
                }
                break;
            case FTOptix.CommunicationDriver.TagInfo tagInfo:
                bool tagToImport = plcStation switch
                {
                    FTOptix.S7TiaProfinet.Station => CheckS7ProfinetTagToImport(ref tagInfo),
                    FTOptix.RAEtherNetIP.Station => CheckRAEIPTagToImport(ref tagInfo),
                    FTOptix.TwinCAT.Station => false,
                    _ => false
                };
                if (tagToImport)
                {
                    myList.Add(tagInfo);
                }
                break;
        }
    }

    private bool CheckS7ProfinetTagToImport(ref TagInfo tagInfo)
    {
        try
        {
            var symbolName = tagInfo.TagAttributes.FirstOrDefault(x => x.Name.Equals("SymbolName", StringComparison.InvariantCultureIgnoreCase), null);
            if (symbolName == null)
            {
                return false;
            }
            string tagAddress = symbolName.Value.ToString();
            string tagName = tagAddress.Replace("PLC.TAGS.", string.Empty, StringComparison.InvariantCultureIgnoreCase).Replace("PLC.BLOCKS.", string.Empty, StringComparison.InvariantCultureIgnoreCase);
            if (tagsCheckedToImport.Any(x => x.VariableName == tagName))
            {
                tagInfo.Name = tagName;
                return true;
            }
            else
            {
                return false;
            }
        }
        catch (Exception ex)
        {
            Log.Error(LogicObject.BrowseName, ex.Message);
            return false;
        }
    }

    private bool CheckRAEIPTagToImport(ref TagInfo tagInfo)
    {
        try
        {
            var symbolName = tagInfo.TagAttributes.FirstOrDefault(x => x.Name.Equals("SymbolName", StringComparison.InvariantCultureIgnoreCase), null);
            if (symbolName == null)
            {
                return false;
            }
            string tagAddress = symbolName.Value.ToString();
            if (tagsCheckedToImport.Any(x => x.VariableName == tagAddress))
            {
                tagInfo.Name = tagAddress;
                return true;
            }
            else
            {
                return false;
            }
        }
        catch (Exception ex)
        {
            Log.Error(LogicObject.BrowseName, ex.Message);
            return false;
        }
    }


    #endregion
    private enum JobToRun
    {
        Invalid = -1,
        RAEipCSV = 0,
        RAEipL5X = 1,
        S7TCPCSV = 2,
        ModbusCSV = 3,
        ModbusTPY = 4
    }

    private static readonly ImmutableDictionary<string, FTOptix.S7TCP.MemoryArea> S7MemoryAreaMapping = new Dictionary<string, FTOptix.S7TCP.MemoryArea>
    {
        { "DB", FTOptix.S7TCP.MemoryArea.DataBlock },
        { "P", FTOptix.S7TCP.MemoryArea.Peripheral },
        { "M", FTOptix.S7TCP.MemoryArea.Marker },
        { "I", FTOptix.S7TCP.MemoryArea.Input },
        { "E", FTOptix.S7TCP.MemoryArea.Input },
        { "Q", FTOptix.S7TCP.MemoryArea.Output },
        { "A", FTOptix.S7TCP.MemoryArea.Output },
        { "C", FTOptix.S7TCP.MemoryArea.Counter },
        { "T", FTOptix.S7TCP.MemoryArea.Timer }
    }.ToImmutableDictionary();

    private static readonly ImmutableDictionary<string, ModbusMemoryArea> ModbusMemoryAreaMapping = new Dictionary<string, ModbusMemoryArea>
    {
        { "HR", ModbusMemoryArea.HoldingRegister },
        { "CO", ModbusMemoryArea.Coil },
        { "IR", ModbusMemoryArea.InputRegister },
        { "DI", ModbusMemoryArea.DiscreteInput },
    }.ToImmutableDictionary();

    private List<TagCustomGridRowData> tagsReadFromSource;
    private List<TagCustomGridRowData> tagsCheckedToImport;
    private List<TagCustomGridRowData> tagsReadFromSourceToDisplay;
    private List<TagDataImported> tagsImported;
    private ColumnLayout tagsTable;
    private IUAVariable currentPageVariable;
    private IUAVariable importJobRunningVariable;
    private IUAVariable filterStringVariable;
    private IUANode plcStation;
    private LongRunningTask jobImportFromFile;
    private LongRunningTask jobImportFromController;
    private LongRunningTask jobReadTagsImported;
    private Struct[] plcItems;
    private Dialog ownerDialog;
    private CheckBox selectAllVariableValue;
    private bool closeDialogWithError;
    
    private class TagsToReconnect
    {
        public NodeId TargetDynamicLink { get; set; }
        public string SourceTagBrowseName { get; set; }
        public NodeId ReferenceType { get; set; }
        public DynamicLinkMode TargetDynamicLinkMode {get; set; }
    }
}

