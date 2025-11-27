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
            Log.Error(LogicObject.BrowseName, "Unable to found the source node");
            return;
        }
        selectAllVariableValue = Owner.Find<CheckBox>("SelectAllVariableValue");
        if (selectAllVariableValue == null)
        {
            Log.Error(LogicObject.BrowseName, "Unable to found SelectAllVariableValue CheckBox");
            return;
        }
        IUANode fatherNode = Project.Current.Get("Model");
        isOnlyOneSelectionAllowed = LogicObject.GetVariable("IsOnlyOneSelectionAllowed");
        isOnlyOneSelectionAllowed.Value = sourceDataCollector is MQTTPayloadInfoEdit;
        fatherNode = GetOrGenerateFatherNode(fatherNode);
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
        if (isOnlyOneSelectionAllowed.Value)
        {
            CheckBoxChangingValueHandler(false);
        }
    }

    public override void Stop()
    {
        if (isOnlyOneSelectionAllowed.Value)
        {
            CheckBoxChangingValueHandler(true);
        }
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
        if (fieldSourceNode is CommunicationStation || fieldSourceNode is OPCUAClient)
        {
            sourceField = fieldSourceNode;
            jobImportFromField.Start();
        }
    }

    [ExportMethod]
    public void SetCheckedStatus(bool checkedValue)
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
        try
        {
            GenerateTagRowDataForImport();
            switch (sourceDataCollector)
            {
                case FTOptix.DataLogger.DataLogger:
                    LoggersLogic.Instance?.SaveTagsConfiguration(sourceDataCollector.NodeId);
                    break;
                case MQTTPublisherDataConfiguration:
                    MqttClientLogic.Instance?.CreateOrUpdateTagsToPublish(sourceDataCollector.NodeId);
                    break;
                case FTOptix.OPCUAServer.NodesToPublishConfigurationEntry:
                    OpcUaServerLogic.Instance?.SaveConfiguration(sourceDataCollector.NodeId);
                    break;
                case MQTTPayloadInfoEdit:
                    MQTTPayloadInfoEditDialogLogic.LinkVariableToPayloadField(sourceDataCollector.NodeId, GetSelectedEntryVariable());
                    break;
                case MQTTPayloadObject mqttPayloadObject:
                    mqttPayloadObject.Content.GetByType<NetLogicObject>()?.ExecuteMethod("GenerateTagsList");
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error(LogicObject.BrowseName, $"Error during SaveAndClose: {ex.Message} \n {ex.StackTrace}");
            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Error, "An error occurred during saving the selected tags. Please check the logs for more details.");
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
            int totalPages = tagsReadFromField.Count / 16;
            if (tagsReadFromField.Count % 16 > 0)
            {
                totalPages++;
            }
            LogicObject.GetVariable("TotalPages").Value = totalPages;
            // this trick can ensure the refresh in case of i change from a Plc to another and the current page is 1
            if (currentPageVariable.Value == 1)
            {
                currentPageVariable.Value = 0;
            }
            currentPageVariable.Value = 1;
        }
        catch (Exception ex)
        {
            Log.Error(LogicObject.BrowseName, $"{ex.Message} \n {ex.StackTrace}");
        }
    }

    private List<InternalTagCustomGridRowData> ReadTagsFromPlc(IUANode tagsFolder, string variableNamePrefix, ref int counter)
    {
        List<InternalTagCustomGridRowData> returnValue = [];
        try
        {
            foreach (var tag in tagsFolder.GetNodesByType<IUAVariable>())
            {
                string fullVariableName = string.IsNullOrEmpty(variableNamePrefix) ? tag.BrowseName : $"{variableNamePrefix}.{tag.BrowseName}";
                var newTagData = new InternalTagCustomGridRowData
                {
                    BrowseName = counter.ToString(),
                    Checked = tagsConfigured.Exists(x => x.BrowseName == fullVariableName),
                    VariableName = fullVariableName,
                    VariableDataType = InformationModel.Get(tag.DataType).BrowseName,
                    VariableDataTypeNodeId = tag.DataType,
                    VariableComment = tag.Description?.Text ?? string.Empty,
                    VariableIsArray = tag.ArrayDimensions.Length > 0,
                    VariableArrayDimension = tag.ArrayDimensions,
                    VariableNodeId = tag.NodeId
                };
                newTagData.VariableLinkDirection = newTagData.Checked ? tagsConfigured.First(x => x.BrowseName == fullVariableName).LinkDirection : DynamicLinkMode.Read;
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

    private List<InternalTagCustomGridRowData> ReadTagsFromOpcUa(IUANode clientStation)
    {
        List<InternalTagCustomGridRowData> returnValue = [];
        // TO DO: implement OPC UA tag reading when OPC UA Client Tag Importer is implemented at Runtime in Optix
        return returnValue;
    }

    private void CurrentPageVariable_VariableChange(object sender, VariableChangeEventArgs e)
    {
        if ((int)e.NewValue > 0 && e.NewValue <= LogicObject.GetVariable("TotalPages").Value)
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
            if (variableDataIndex < tagsReadFromField.Count)
            {
                UpdateTagRowData(rowIndex, variableDataIndex);
                tableRow.Visible = true;
            }
            else
            {
                tableRow.Visible = false;
            }
        }
        UpdateCheckBoxSelectedAll();
    }

    private void UpdateTagRowData(int rowIndex, int variableDataIndex)
    {
        var rowData = LogicObject.Get<TagCustomGridRowData>($"GridData/{rowIndex + 1}");
        rowData.CheckedVariable.VariableChange -= OnRowSettingsChanged;
        rowData.VariableLinkDirectionVariable.VariableChange -= OnRowSettingsChanged;
        rowData.Checked = tagsReadFromField[variableDataIndex].Checked;
        rowData.VariableName = tagsReadFromField[variableDataIndex].VariableName;
        rowData.VariableDataType = tagsReadFromField[variableDataIndex].VariableDataType;
        rowData.VariableComment = tagsReadFromField[variableDataIndex].VariableComment;
        rowData.VariableAddress = tagsReadFromField[variableDataIndex].VariableAddress;
        rowData.VariableIsArray = tagsReadFromField[variableDataIndex].VariableIsArray;
        rowData.VariableArrayDimension = tagsReadFromField[variableDataIndex].VariableArrayDimension;
        rowData.VariableDataTypeNodeId = tagsReadFromField[variableDataIndex].VariableDataTypeNodeId;
        rowData.VariableLinkDirection = tagsReadFromField[variableDataIndex].VariableLinkDirection;
        rowData.CheckedVariable.VariableChange += OnRowSettingsChanged;
        rowData.VariableLinkDirectionVariable.VariableChange += OnRowSettingsChanged;
    }

    private void OnRowSettingsChanged(object sender, VariableChangeEventArgs e)
    {
        int currentPage = currentPageVariable.Value;
        int variableDataOffset = (currentPage - 1) * 16;
        var rowData = e.Variable.Owner as TagCustomGridRowData;
        int rowIndex = int.Parse(rowData.BrowseName) - 1;
        int variableDataIndex = rowIndex + variableDataOffset;
        switch (e.Variable.BrowseName)
        {
            case "Checked":
                tagsReadFromField[variableDataIndex].Checked = e.NewValue;
                UpdateCheckBoxSelectedAll();
                break;
            case "VariableLinkDirection":
                tagsReadFromField[variableDataIndex].VariableLinkDirection = (DynamicLinkMode)e.NewValue.Value;
                break;
        }
    }

    private void UpdateCheckBoxSelectedAll()
    {
        selectAllVariableValue.Checked = LogicObject.GetObject("GridData").GetNodesByType<TagCustomGridRowData>()
                .Where(rowData => tagsTable.Get<TagCustomGridRow>($"TagCustomGridRow{rowData.BrowseName}").Visible)
                .All(rowData => rowData.Checked);
    }

    private NodeId GetSelectedEntryVariable()
    {
        var selectedTag = tagsReadFromField.FirstOrDefault(x => x.Checked, null);
        return selectedTag?.VariableNodeId ?? NodeId.Empty;
    }

    private void CheckBoxChangingValueHandler(bool unsubscribe)
    {
        for (int rowIndex = 0; rowIndex < 16; rowIndex++)
        {
            TagCustomGridRow tableRow = tagsTable.Get<TagCustomGridRow>($"TagCustomGridRow{rowIndex + 1}");
            if (unsubscribe)
            {
                tableRow.Find<CheckBox>("SelectVariableValue").OnUserValueChanged -= OnCheckBoxChange;
            }
            else
            {
                tableRow.Find<CheckBox>("SelectVariableValue").OnUserValueChanged += OnCheckBoxChange;
            }
        }
    }

    private void OnCheckBoxChange(object sender, UserValueChangedEvent e)
    {
        bool checkedValue = (bool)e.NewValue;

        if (isOnlyOneSelectionAllowed.Value)
        {
            LogicObject.GetVariable("DisableMoreSelections").Value = checkedValue;
        }
    }

    private void ReadTagsConfigured()
    {
        if (sourceDataCollector is IUAObject sourceDataCollectorNode)
        {
            IUAObject sourceNodeToDiscover = sourceDataCollectorNode switch
            {
                FTOptix.MQTTClient.MQTTPublisher => Project.Current.Get<MQTTPublisherDataConfiguration>($"{CommonLogic.MQTTPublishersDataConfigurationPath}/{sourceDataCollectorNode.Owner.BrowseName}_{sourceDataCollectorNode.BrowseName}").Data,
                FTOptix.DataLogger.DataLogger => sourceDataCollector.GetObject("VariablesToLog"),
                FTOptix.OPCUAServer.NodesToPublishConfigurationEntry => Project.Current.GetObject($"{CommonLogic.OPCUAServerDataFolderPath}/{sourceDataCollectorNode.Owner.Owner.BrowseName}/{sourceDataCollectorNode.BrowseName}"),
                MQTTPublisherDataConfiguration dataConfiguration => dataConfiguration.Data,
                MQTTPayloadInfoEdit => null,
                MQTTPayloadObject payloadObject => MqttClientLogic.GetSourceDataFromPayloadObject(payloadObject),
                _ => null,
            };
            tagsConfigured = CommonLogic.ReadTagsFromSourceDataCollector(sourceNodeToDiscover, sourceDataCollectorNode);
        }
        if (Owner.Find<ComboBox>("SourceStationValue") is ComboBox comboBoxSourceStation)
        {
            ReadTagsFromSource(comboBoxSourceStation.SelectedValueVariable.Value);
        }
    }

    private void GenerateTagRowDataForImport()
    {
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
        temporaryFolder.Children.Clear();
        foreach (var tagRead in tagsReadFromField.Where(x => x.Checked))
        {
            temporaryFolder.Add(GenerateRowDataFromInternal(tagRead));
        }
    }

    private static TagCustomGridRowData GenerateRowDataFromInternal(InternalTagCustomGridRowData internalData)
    {
        var rowData = InformationModel.MakeObject<TagCustomGridRowData>(internalData.VariableName);
        rowData.Checked = internalData.Checked;
        rowData.VariableName = internalData.VariableName;
        rowData.VariableComment = internalData.VariableComment;
        rowData.VariableDataType = internalData.VariableDataType;
        rowData.VariableAddress = internalData.VariableAddress;
        rowData.VariableIsArray = internalData.VariableIsArray;
        rowData.VariableArrayDimension = internalData.VariableArrayDimension;
        rowData.VariableDataTypeNodeId = internalData.VariableDataTypeNodeId;
        rowData.VariableNodeId = internalData.VariableNodeId;
        rowData.VariableStringLength = internalData.VariableStringLength;
        rowData.VariableLinkDirection = internalData.VariableLinkDirection;
        return rowData;
    }

    private IUANode GetOrGenerateFatherNode(IUANode fatherNode)
    {
        switch (sourceDataCollector)
        {
            case MQTTPayloadObject:
            case MQTTPayloadInfoEdit:
                temporarySourceDataFolder = fatherNode.Get<Folder>(sourceDataCollector.Owner.BrowseName);
                if (temporarySourceDataFolder == null)
                {
                    temporarySourceDataFolder = InformationModel.MakeObject<Folder>(sourceDataCollector.Owner.BrowseName);
                    fatherNode.Add(temporarySourceDataFolder);
                }
                fatherNode = temporarySourceDataFolder;
                break;
            case NodesToPublishConfigurationEntry:
                LogicObject.GetVariable("EnableLinkDirection").Value = true;
                temporarySourceDataFolder = fatherNode.Get<Folder>(sourceDataCollector.Owner.Owner.BrowseName);
                if (temporarySourceDataFolder == null)
                {
                    temporarySourceDataFolder = InformationModel.MakeObject<Folder>(sourceDataCollector.Owner.Owner.BrowseName);
                    fatherNode.Add(temporarySourceDataFolder);
                }
                fatherNode = temporarySourceDataFolder;
                break;
        }

        return fatherNode;
    }

    private List<InternalTagCustomGridRowData> tagsReadFromField;
    private List<TagDataImported> tagsConfigured;
    private ColumnLayout tagsTable;
    private IUAVariable currentPageVariable;
    private IUANode sourceDataCollector;
    private Folder temporarySourceDataFolder;
    private Folder temporaryFolder;
    private LongRunningTask jobImportFromField;
    private LongRunningTask jobReadTagsConfigured;
    private IUAObject sourceField;
    private IUAVariable isOnlyOneSelectionAllowed;
    private CheckBox selectAllVariableValue;
}
