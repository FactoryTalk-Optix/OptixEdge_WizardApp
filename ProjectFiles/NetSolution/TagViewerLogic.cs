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
using System.Runtime.CompilerServices;
#endregion

public class TagViewerLogic : BaseNetLogic
{
    public override void Start()
    {
        if (Owner is not Panel)
        {
            Log.Error(LogicObject.BrowseName, "Owner is not a Panel!");
            return;
        }
        ownerDialog = (Panel)Owner;
        tagsTable = ownerDialog.Find<ColumnLayout>("TagsTable");
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
        filterStringVariable = LogicObject.GetVariable("FilterString");
        if (filterStringVariable == null)
        {
            Log.Error(LogicObject.BrowseName, "Unable to found FilterString variable");
            return;
        }
        selectAllVariableValue = Owner.Find<CheckBox>("SelectAllVariableValue");
        if (selectAllVariableValue == null)
        {
            Log.Error(LogicObject.BrowseName, "Unable to found SelectAllVariableValue CheckBox");
            return;
        }
        if (LogicObject.GetAlias("TagSourceDataCollector") is not IUAObject sourceObjectNode)
        {
            Log.Error(LogicObject.BrowseName, "Unable to found MQTT publisher/Datalogger/DataGrid/PlcStation source node");
            return;
        }
        LogicObject.GetVariable("EnableLinkDirection").Value = sourceObjectNode is NodesToPublishConfigurationEntry;
        sourceDataCollector = sourceObjectNode;
        jobReadTagsConfigured = new LongRunningTask(ReadTagsImported, LogicObject);
        currentPageVariable.VariableChange += CurrentPageVariable_VariableChange;
        filterStringVariable.VariableChange += FilterStringVariable_VariableChange;
        InitCollections();
        RegisterObserverToDataSource();
    }

    public override void Stop()
    {
        if (currentPageVariable != null)
        {
            currentPageVariable.VariableChange -= CurrentPageVariable_VariableChange;
        }
        if (filterStringVariable != null)
        {
            filterStringVariable.VariableChange -= FilterStringVariable_VariableChange;
        }
        CommonLogic.DisposeTask(jobReadTagsConfigured);
        CommonLogic.DisposeTask(childrenObserverTask);
    }

    [ExportMethod]
    public void RefreshGrid()
    {
        if (!runningUpdate)
        {
            InitCollections();
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
    public void DeleteTags()
    {
        if (!tagsReadFromField.Any(x=> x.Checked))
        {
            return;
        }
        foreach (var tagRowData in tagsReadFromField.Where(x => x.Checked))
        {
            Log.Debug(LogicObject.BrowseName, $"Deleting tag {tagRowData.VariableName} from the list");
            var tagData = tagsConfigured.FirstOrDefault(x => x.BrowseName == tagRowData.VariableName);
            if (tagData != null && InformationModel.GetVariable(tagData.NodeId) is IUAVariable tagVariable)
            {
                try
                {
                    foreach (var targetNode in tagVariable.InverseRefs.GetNodes(FTOptix.Core.ReferenceTypes.Resolves))
                    {
                        if (targetNode is DynamicLink dynamicLink)
                        {
                            Log.Debug(LogicObject.BrowseName, $"Deleting dynamic link {dynamicLink.Owner.BrowseName} pointing to tag {tagRowData.VariableName}");
                            dynamicLink.Delete();
                        }
                    }
                    tagVariable.Delete();
                }
                catch (Exception ex)
                {
                    Log.Error(LogicObject.BrowseName, $"Error deleting tag {tagRowData.VariableName}: {ex.Message}");
                }
            }
        }
        InitCollections();
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
            if (variableDataIndex < tagsReadFromField.Count)
            {
                UpdateTagRowData(rowIndex, variableDataIndex);
                tableRow.Visible = true;
            }
            else
            {
                tableRow.Visible = false;
                ResetTagRowData(rowIndex);
            }
        }
        UpdateCheckBoxSelectedAll();
    }

    private void UpdateTagRowData(int rowIndex, int variableDataIndex)
    {
        var rowData = LogicObject.Get<TagCustomGridRowData>($"GridData/{rowIndex + 1}");
        rowData.CheckedVariable.VariableChange -= OnRowSettingsChanged;
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
    }

    private void ResetTagRowData(int rowIndex)
    {
        var rowData = LogicObject.Get<TagCustomGridRowData>($"GridData/{rowIndex + 1}");
        rowData.CheckedVariable.VariableChange -= OnRowSettingsChanged;
        rowData.Checked = false;
        rowData.VariableName = string.Empty;
        rowData.VariableDataType = string.Empty;
        rowData.VariableComment = string.Empty;
        rowData.VariableAddress = string.Empty;
        rowData.VariableIsArray = false;
        rowData.VariableArrayDimension = [];
        rowData.VariableDataTypeNodeId = NodeId.Empty;
        rowData.VariableStringLength = 0;
        rowData.CheckedVariable.VariableChange += OnRowSettingsChanged;
    }

    private void OnRowSettingsChanged(object sender, VariableChangeEventArgs e)
    {
        int currentPage = currentPageVariable.Value;
        int variableDataOffset = (currentPage - 1) * 16;
        var rowData = e.Variable.Owner as TagCustomGridRowData;
        int rowIndex = int.Parse(rowData.BrowseName) - 1;
        int variableDataIndex = rowIndex + variableDataOffset;
        tagsReadFromField[variableDataIndex].Checked = e.NewValue;
        LogicObject.GetVariable("TagSelected").Value = tagsReadFromField.Any(x => x.Checked);
        // Check if all visible rows are checked - read directly from UI
        UpdateCheckBoxSelectedAll();
    }

    private void UpdateCheckBoxSelectedAll()
    {
        selectAllVariableValue.Checked = LogicObject.GetObject("GridData").GetNodesByType<TagCustomGridRowData>()
                .Where(rowData => tagsTable.Get<TagCustomGridRow>($"TagCustomGridRow{rowData.BrowseName}").Visible)
                .All(rowData => rowData.Checked);
    }

    private void FilterStringVariable_VariableChange(object sender, VariableChangeEventArgs e)
    {
        tagsReadFromFieldToDisplay.Clear();
        if (string.IsNullOrEmpty(e.NewValue))
        {
            tagsReadFromFieldToDisplay.AddRange(tagsReadFromField);
        }
        else
        {
            tagsReadFromFieldToDisplay.AddRange(tagsReadFromField.Where(x => x.VariableName.StartsWith(e.NewValue, StringComparison.InvariantCultureIgnoreCase) || x.VariableName.Contains(e.NewValue, StringComparison.InvariantCultureIgnoreCase)));
        }
        UpdateDataGrid();
    }

    private void UpdateDataGrid(bool initListToDisplay = false)
    {
        if (initListToDisplay)
        {
            tagsReadFromFieldToDisplay.Clear();
            tagsReadFromFieldToDisplay.AddRange(tagsReadFromField);
        }
        if (tagsReadFromFieldToDisplay == null || tagsReadFromFieldToDisplay.Count == 0)
        {
            ChangeCurrentPage(1);
            return;
        }
        int totalPages = tagsReadFromFieldToDisplay.Count / 16;
        if (tagsReadFromFieldToDisplay.Count % 16 > 0)
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

    private void ReadTagsImported()
    {
        runningUpdate = true;
        tagsReadFromField = [];
        tagsConfigured = sourceDataCollector switch
        {
            FTOptix.CommunicationDriver.CommunicationStation => PopulateListTagImported(sourceDataCollector.Get<Folder>("Tags"), sourceDataCollector),
            FTOptix.MQTTClient.MQTTPublisher => CommonLogic.ReadTagsFromSourceDataCollector(Project.Current.Get<MQTTPublisherDataConfiguration>($"{CommonLogic.MQTTPublishersDataConfigurationPath}/{sourceDataCollector.Owner.BrowseName}_{sourceDataCollector.BrowseName}").Data, sourceDataCollector),
            FTOptix.DataLogger.DataLogger => CommonLogic.ReadTagsFromSourceDataCollector(sourceDataCollector.GetObject("VariablesToLog"), sourceDataCollector),
            FTOptix.OPCUAServer.NodesToPublishConfigurationEntry => CommonLogic.ReadTagsFromSourceDataCollector(Project.Current.GetObject($"{CommonLogic.OPCUAServerDataFolderPath}/{sourceDataCollector.Owner.Owner.BrowseName}/{sourceDataCollector.BrowseName}"), sourceDataCollector),
            MQTTPublisherDataConfiguration dataConfiguration => CommonLogic.ReadTagsFromSourceDataCollector(dataConfiguration.Data, sourceDataCollector),
            MQTTPayloadObject payloadObject => CommonLogic.ReadTagsFromSourceDataCollector(MqttClientLogic.GetSourceDataFromPayloadObject(payloadObject), sourceDataCollector),
            _ => [],
        };
        foreach (var tag in tagsConfigured)
        {
            var tagDisplay = new InternalTagCustomGridRowData
            {
                VariableName = tag.BrowseName,
                VariableDataType = InformationModel.Get(tag.DataType).BrowseName,
                VariableDataTypeNodeId = tag.DataType,
                VariableComment = tag.Description,
                VariableAddress = string.Empty,
                VariableIsArray = tag.ArrayDimensions.Length > 0,
                VariableArrayDimension = tag.ArrayDimensions,
                VariableLinkDirection = tag.LinkDirection,
                VariableNodeId = tag.NodeId,
            };
            tagsReadFromField.Add(tagDisplay);
        }
        UpdateDataGrid(true);
        runningUpdate = false;
    }

    private List<TagDataImported> PopulateListTagImported(Folder tagsFolder, IUANode plcStation)
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
                    ArrayDimensions = tag.ArrayDimensions,
                    LinkDirection = DynamicLinkMode.Read
                };
                returnValue.Add(newTagData);
            }
            foreach (var subfolder in tagsFolder.GetNodesByType<Folder>())
            {
                returnValue.AddRange(PopulateListTagImported(subfolder, plcStation));
            }
        }
        return returnValue;
    }

    private void InitCollections()
    {
        tagsReadFromField = [];
        tagsReadFromFieldToDisplay = [];
        tagsConfigured = [];
        LogicObject.GetVariable("TagSelected").Value = false;
        jobReadTagsConfigured.Start();
        Owner.GetVariable("CounterDeleteAction").Value += 1;
    }

    private void RegisterObserverToDataSource()
    {
        var sourceData = sourceDataCollector switch
        {
            FTOptix.CommunicationDriver.CommunicationStation => sourceDataCollector.Get<Folder>("Tags"),
            FTOptix.MQTTClient.MQTTPublisher => Project.Current.Get<MQTTPublisherDataConfiguration>($"{CommonLogic.MQTTPublishersDataConfigurationPath}/{sourceDataCollector.Owner.BrowseName}_{sourceDataCollector.BrowseName}").Data,
            FTOptix.DataLogger.DataLogger => sourceDataCollector.GetObject("VariablesToLog"),
            FTOptix.OPCUAServer.NodesToPublishConfigurationEntry => Project.Current.GetObject($"{CommonLogic.OPCUAServerDataFolderPath}/{sourceDataCollector.Owner.Owner.BrowseName}/{sourceDataCollector.BrowseName}"),
            MQTTPublisherDataConfiguration dataConfiguration => dataConfiguration.Data,
            MQTTPayloadObject payloadObject => MqttClientLogic.GetSourceDataFromPayloadObject(payloadObject),
            _ => null,
        };        
        if (sourceData != null)
        {
            memoryChildrenCount = sourceData.Children.Count;
            childrenObserverTask = new PeriodicTask(ObserveChildrenNodesChange, sourceData, TimeSpan.FromMilliseconds(500), LogicObject);
            childrenObserverTask.Start();
        }
    }

    private void ObserveChildrenNodesChange(PeriodicTask task, object arguments)
    {
        if (arguments is IUAObject sourceNode)
        {
            if (sourceNode.Children.Count != memoryChildrenCount)
            {
                Log.Debug(LogicObject.BrowseName, $"Change in the source data {sourceNode.BrowseName} collector detected. Refreshing the list.");
                if (!runningUpdate)
                {
                    InitCollections();
                }
                memoryChildrenCount = sourceNode.Children.Count;
            }
        }  
    }

    private List<InternalTagCustomGridRowData> tagsReadFromField;
    private List<InternalTagCustomGridRowData> tagsReadFromFieldToDisplay;
    private List<TagDataImported> tagsConfigured;
    private ColumnLayout tagsTable;
    private IUAVariable currentPageVariable;
    private IUAVariable filterStringVariable;
    private IUAObject sourceDataCollector;
    private LongRunningTask jobReadTagsConfigured;
    private PeriodicTask childrenObserverTask;
    private Panel ownerDialog;
    private int memoryChildrenCount;
    private CheckBox selectAllVariableValue;
    bool runningUpdate = false;
}


