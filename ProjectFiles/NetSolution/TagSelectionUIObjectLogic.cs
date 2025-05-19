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
using FTOptix.OPCUAClient;
using FTOptix.DataLogger;
using FTOptix.Core;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using FTOptix.OPCUAServer;
using FTOptix.MQTTBroker;
using FTOptix.MQTTClient;
using FTOptix.AuditSigning;
using FTOptix.NativeUI;
#endregion

public class TagSelectionUIObjectLogic : BaseNetLogic
{
    public override void Start()
    {
        if (Owner is not Dialog)
        {
            Log.Error(LogicObject.BrowseName, "Owner is not a Dialog!");
            return;
        }
        tagsTable = Owner.Find<ColumnLayout>("TagsTable");
        if (tagsTable == null)
        {
            Log.Error(LogicObject.BrowseName, "Unable to found the Tags table");
            return;
        }
        currentPageVariable = LogicObject.GetVariable("CurrentPage");
        if (currentPageVariable == null)
        {
            Log.Error(LogicObject.BrowseName, "Unable to found CurrentPage variable");
            return;
        }
        sourceDataCollector = LogicObject.GetAlias("TagSourceDataCollector");
        if (sourceDataCollector == null)
        {
            Log.Error(LogicObject.BrowseName, "Unable to found MQTT publisher/Datalogger/DataGrid source node");
            return;
        }
        IUANode fatherNode = Project.Current.Get("Model");
        switch (sourceDataCollector)
        {
            case MQTTPublisher:
                temporarySourceDataFolder = fatherNode.Get<Folder>(sourceDataCollector.Owner.BrowseName);
                if (temporarySourceDataFolder == null)
                {
                    temporarySourceDataFolder = InformationModel.MakeObject<Folder>(sourceDataCollector.Owner.BrowseName);
                    fatherNode.Add(temporarySourceDataFolder);
                }
                fatherNode = temporarySourceDataFolder;
                break;
            case NodesToPublishConfigurationEntry:
                temporarySourceDataFolder = fatherNode.Get<Folder>(sourceDataCollector.Owner.Owner.BrowseName);
                if (temporarySourceDataFolder == null)
                {
                    temporarySourceDataFolder = InformationModel.MakeObject<Folder>(sourceDataCollector.Owner.Owner.BrowseName);
                    fatherNode.Add(temporarySourceDataFolder);
                }
                fatherNode = temporarySourceDataFolder;
                break;
        }
        temporarySourceDataFolder = fatherNode.Get<Folder>(sourceDataCollector.BrowseName);
        if (temporarySourceDataFolder == null)
        {
            temporarySourceDataFolder = InformationModel.MakeObject<Folder>(sourceDataCollector.BrowseName);
            fatherNode.Add(temporarySourceDataFolder);
        }
        // cleanup temporary Folder
        foreach (var childrenNode in temporarySourceDataFolder.Children)
        {
            temporarySourceDataFolder.Remove(childrenNode);
        }
        jobImportFromField = new LongRunningTask(ReadFromField, LogicObject);
        jobReadTagsConfigured = new LongRunningTask(ReadTagsConfigured, LogicObject);
        currentPageVariable.VariableChange += CurrentPageVariable_VariableChange;
        tagsReadFromField = [];
        tagsConfigured = [];        
        jobReadTagsConfigured.Start();
    }

    public override void Stop()
    {
        if (currentPageVariable != null)
        {
            currentPageVariable.VariableChange -= CurrentPageVariable_VariableChange;
        }
        try
        {
            jobImportFromField?.Cancel();
            jobReadTagsConfigured?.Cancel();
        }
        catch
        {
            // Job is not running
        }
        jobImportFromField?.Dispose();
        jobReadTagsConfigured?.Dispose();
    }

    [ExportMethod]
    public void ReadTagsFromSource(NodeId fieldSource)
    {
        var fieldSourceNode = InformationModel.GetObject(fieldSource);
        if (fieldSourceNode is CommunicationStation || fieldSourceNode is OPCUAClient )
        {
            sourceField = fieldSourceNode;
            jobImportFromField.Start();
        }
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
    public void SaveAndClose()
    {
        switch (sourceDataCollector)
        {
            case FTOptix.DataLogger.DataLogger:
                LoggersLogic.Instance?.SaveTagsConfiguration(sourceDataCollector.NodeId);
                break;
            case FTOptix.MQTTClient.MQTTPublisher:
                MqttClientLogic.Instance?.CreateOrUpdateTagsToPublish(sourceDataCollector.NodeId);
                break;
            case FTOptix.OPCUAServer.NodesToPublishConfigurationEntry:
                OpcUaServerLogic.Instance?.SaveConfiguration(sourceDataCollector.NodeId);
                break;
        }
        (Owner as Dialog).Close();
    }

    private void ReadFromField()
    {
        try
        {
            if (sourceField.IsInstanceOf(FTOptix.CommunicationDriver.ObjectTypes.CommunicationStation))
            {
                int i = 0;
                var tagsFolder = sourceField.Get<Folder>("Tags");
                tagsReadFromField = ReadTagsFromPlc(tagsFolder, "", ref i);
            }
            else
            {
                tagsReadFromField = ReadTagsFromOpcUa(sourceField);
            }
            if (tagsReadFromField == null)
            {
                throw new InvalidDataException("No valid data read from the source");
            }
            // Made temporary nodes
            if (temporarySourceDataFolder == null)
            {
                throw new InvalidDataException("temporaryFolder is null!");
            }
            temporaryFolder = temporarySourceDataFolder.Get<Folder>(sourceField.BrowseName);
            if (temporaryFolder == null)
            {
                temporaryFolder = InformationModel.Make<Folder>(sourceField.BrowseName);
                temporarySourceDataFolder.Add(temporaryFolder);
            }
            foreach (var tagRead in tagsReadFromField)
            {
                if (temporaryFolder.Get(tagRead.BrowseName) is TagCustomGridRowData currentTag)
                {
                    currentTag.Checked = tagsConfigured.Exists(x => x.BrowseName == tagRead.VariableName);
                    currentTag.VariableName = tagRead.VariableName;
                    currentTag.VariableComment = tagRead.VariableComment;
                    currentTag.VariableDataType = tagRead.VariableDataType;
                    currentTag.VariableAddress = tagRead.VariableAddress;
                    currentTag.VariableIsArray = tagRead.VariableIsArray;
                    currentTag.VariableArrayDimension = tagRead.VariableArrayDimension;
                }
                else
                {
                    temporaryFolder.Add(tagRead);
                }
            }
            int totalPages = tagsReadFromField.Count / 16;
            if (tagsReadFromField.Count % 16 > 0)
            {
                totalPages++;
            }
            LogicObject.GetVariable("TotalPages").Value = totalPages;
            // this trick can ensure the refresh in case of i change from a Plc to another and the current page is 1
            if (currentPageVariable.Value == 1)
            {
                ChangeCurrentPage(1);
            }
            currentPageVariable.Value = 1;
        }
        catch (Exception ex)
        {
            Log.Error(LogicObject.BrowseName, $"{ex.Message} \n {ex.StackTrace}");
        }
    }

    private List<TagCustomGridRowData> ReadTagsFromPlc(IUANode tagsFolder, string variableNamePrefix, ref int counter)
    {
        List<TagCustomGridRowData> returnValue = [];
        try
        {                     
            foreach (var tag in tagsFolder.GetNodesByType<IUAVariable>())
            {
                string fullVariableName = string.IsNullOrEmpty(variableNamePrefix) ? tag.BrowseName : $"{variableNamePrefix}.{tag.BrowseName}"; 
                var newTagData = InformationModel.MakeObject<TagCustomGridRowData>(counter.ToString());
                newTagData.Checked = tagsConfigured.Exists(x => x.BrowseName == fullVariableName);
                newTagData.VariableName = fullVariableName;
                newTagData.VariableDataType = InformationModel.Get(tag.DataType).BrowseName;
                newTagData.VariableDataTypeNodeId = tag.DataType;
                newTagData.VariableComment = tag.Description?.Text ?? string.Empty;
                newTagData.VariableIsArray = tag.ArrayDimensions.Length > 0;
                newTagData.VariableArrayDimension = tag.ArrayDimensions;
                newTagData.VariableNodeId = tag.NodeId;
                returnValue.Add(newTagData);
                counter++;
            }
            foreach (var subFolder in tagsFolder.GetNodesByType<Folder>())
            {
                var subFolderTags = ReadTagsFromPlc(subFolder, subFolder.BrowseName, ref counter);
                returnValue.AddRange(subFolderTags);
            }
        }
        catch (Exception ex)
        {
            Log.Error(LogicObject.BrowseName, ex.Message);
        }
        return returnValue;
    }

    private List<TagCustomGridRowData> ReadTagsFromOpcUa(IUANode clientStation)
    {
        List<TagCustomGridRowData> returnValue = [];
        try
        {
            _ = clientStation.Get("ToDoSection");
        }
        catch (Exception ex)
        {
            Log.Error(LogicObject.BrowseName, ex.Message);
        }
        return returnValue;
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
        int variableDataOffset = (newPage - 1) * 16;
        for (int rowIndex = 0; rowIndex < 16; rowIndex++)
        {
            int variableDataIndex = rowIndex + variableDataOffset;
            TagCustomGridRow tableRow = tagsTable.Get<TagCustomGridRow>($"TagCustomGridRow{rowIndex + 1}");
            if (temporaryFolder.Get(variableDataIndex.ToString()) is TagCustomGridRowData rowData)
            {
                tableRow.SetAlias("RowData", rowData);
                tableRow.Visible = true;
            }
            else
            {
                tableRow.Visible = false;
            }
        }
    }

    private void ReadTagsConfigured()
    {
        if (sourceDataCollector is IUAObject sourceDataCollectorNode)
        {
            IUAObject sourceNodeToDiscover = sourceDataCollectorNode switch
            {
                FTOptix.MQTTClient.MQTTPublisher => Project.Current.GetObject($"{CommonLogic.MQTTDataFolderPath}/{sourceDataCollectorNode.Owner.BrowseName}/{sourceDataCollectorNode.BrowseName}"),
                FTOptix.DataLogger.DataLogger => sourceDataCollector.GetObject("VariablesToLog"),
                FTOptix.OPCUAServer.NodesToPublishConfigurationEntry => Project.Current.GetObject($"{CommonLogic.OPCUAServerDataFolderPath}/{sourceDataCollectorNode.Owner.Owner.BrowseName}/{sourceDataCollectorNode.BrowseName}"),
                _ => null,
            };
            tagsConfigured = CommonLogic.ReadTagsFromSourceDataCollector(sourceNodeToDiscover, sourceDataCollectorNode);
        }
        if (Owner.Find<ComboBox>("SourceStationValue") is ComboBox comboBoxSourceStation)
        {
            ReadTagsFromSource(comboBoxSourceStation.SelectedValueVariable.Value);
        }
    }

    private List<TagCustomGridRowData> tagsReadFromField;
    private List<TagDataImported> tagsConfigured;
    private ColumnLayout tagsTable;
    private IUAVariable currentPageVariable;
    private IUANode sourceDataCollector;
    private Folder temporarySourceDataFolder;
    private Folder temporaryFolder;
    private LongRunningTask jobImportFromField;
    private LongRunningTask jobReadTagsConfigured;
    private IUAObject sourceField;
}
