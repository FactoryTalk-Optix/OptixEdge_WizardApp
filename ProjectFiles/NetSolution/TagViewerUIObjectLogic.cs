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

public class TagViewerUIObjectLogic : BaseNetLogic
{
    public override void Start()
    {
         closeDialogWithError = true;
        if (Owner is not Dialog)
        {
            Log.Error(LogicObject.BrowseName, "Owner is not a Dialog!");
            return;
        }
        ownerDialog = (Dialog) Owner;
        tagsTable = ownerDialog.Find<ColumnLayout>("TagsTable");
        if (tagsTable == null)
        {
            Log.Error(LogicObject.BrowseName, "Unable to found the Tags table");
            ownerDialog.Close();
            return;
        }
        currentPageVariable = LogicObject.GetVariable("CurrentPage");
        if (currentPageVariable == null)
        {
            Log.Error(LogicObject.BrowseName, "Unable to found CurrentPage variable");
            ownerDialog.Close();
            return;
        }
        filterStringVariable = LogicObject.GetVariable("FilterString");
        if (filterStringVariable == null)
        {
            Log.Error(LogicObject.BrowseName, "Unable to found FilterString variable");
            ownerDialog.Close();
            return;
        }
        if (LogicObject.GetAlias("TagSourceDataCollector") is not IUAObject sourceObjectNode)
        {
            Log.Error(LogicObject.BrowseName, "Unable to found MQTT publisher/Datalogger/DataGrid/PlcStation source node");
            ownerDialog.Close();
            return;
        }
        sourceDataCollector = sourceObjectNode;
        closeDialogWithError = false;
        jobReadTagsConfigured = new LongRunningTask(ReadTagsImported, LogicObject);
        currentPageVariable.VariableChange += CurrentPageVariable_VariableChange;
        filterStringVariable.VariableChange += FilterStringVariable_VariableChange;
        tagsReadFromField = [];
        tagsReadFromFieldToDisplay = [];
        tagsConfigured = [];
        jobReadTagsConfigured.Start();
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
        CommonLogic.DisposeTask(jobReadTagsConfigured);
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
            if (variableDataIndex < tagsReadFromFieldToDisplay.Count)
            {
                tableRow.SetAlias("RowData", tagsReadFromFieldToDisplay[variableDataIndex]);
                tableRow.Visible = true;
            }
            else
            {
                tableRow.Visible = false;
            }
        }
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
        tagsReadFromField = [];
        tagsConfigured = sourceDataCollector switch
        {
            FTOptix.CommunicationDriver.CommunicationStation => PopulateListTagImported(sourceDataCollector.Get<Folder>("Tags"), sourceDataCollector),
            FTOptix.MQTTClient.MQTTPublisher => CommonLogic.ReadTagsFromSourceDataCollector(Project.Current.GetObject($"{CommonLogic.MQTTDataFolderPath}/{sourceDataCollector.Owner.BrowseName}/{sourceDataCollector.BrowseName}"), sourceDataCollector),
            FTOptix.DataLogger.DataLogger => CommonLogic.ReadTagsFromSourceDataCollector(sourceDataCollector.GetObject("VariablesToLog"), sourceDataCollector),
            FTOptix.OPCUAServer.NodesToPublishConfigurationEntry => CommonLogic.ReadTagsFromSourceDataCollector(Project.Current.GetObject($"{CommonLogic.OPCUAServerDataFolderPath}/{sourceDataCollector.Owner.Owner.BrowseName}/{sourceDataCollector.BrowseName}"), sourceDataCollector),
            _ => [],
        };
        foreach (var tag in tagsConfigured)
        {
            var tagDisplay = InformationModel.MakeObject<TagCustomGridRowData>(tag.BrowseName);
            tagDisplay.VariableName = tag.BrowseName;
            tagDisplay.VariableDataType = InformationModel.Get(tag.DataType).BrowseName;
            tagDisplay.VariableDataTypeNodeId = tag.DataType;
            tagDisplay.VariableComment = tag.Description;
            tagDisplay.VariableAddress = string.Empty;
            tagDisplay.VariableIsArray = tag.ArrayDimensions.Length > 0;
            tagDisplay.VariableArrayDimension = tag.ArrayDimensions;
            tagsReadFromField.Add(tagDisplay);            
        }
        UpdateDataGrid(true);
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
                    ArrayDimensions = tag.ArrayDimensions
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

    private List<TagCustomGridRowData> tagsReadFromField;
    private List<TagCustomGridRowData> tagsReadFromFieldToDisplay;
    private List<TagDataImported> tagsConfigured;
    private ColumnLayout tagsTable;
    private IUAVariable currentPageVariable;
    private IUAVariable filterStringVariable;    
    private IUAObject sourceDataCollector;
    private LongRunningTask jobReadTagsConfigured;
    private Dialog ownerDialog;
    private bool closeDialogWithError;
}
