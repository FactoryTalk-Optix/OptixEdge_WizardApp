#region Using directives
using System;
using System.Linq;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.UI;
using FTOptix.HMIProject;
using FTOptix.CoreBase;
using FTOptix.NetLogic;
using FTOptix.CommunicationDriver;
using System.Security.Cryptography;
using FTOptix.Core;
#endregion

public class AddWidgetDialogLogic : BaseNetLogic
{
    public override void Start()
    {
        nodeFactory = LogicObject.Context.NodeFactory as NodeFactory;
        ownerDialog = (Dialog)Owner;
        if (DashboardLogic.Instance == null)
        {
            Log.Error(LogicObject.BrowseName, "DashboardLogic instance is null! Fatal error!");
            ownerDialog.Close();
        }
        widgetObjectType = LogicObject.GetVariable("WidgetObjectType");
        if (widgetObjectType == null)
        {
            Log.Error(LogicObject.BrowseName, "WidgetObjectType variable is null! Fatal error!");
            ownerDialog.Close();
            return;
        }
        if (Owner.GetAlias("WidgetUIObjAlias") is not WidgetData widgetData)
        {
            var widgetNumber = CommonLogic.FindMissingNumber(DashboardLogic.Instance.GetListTotalWidgetsBrowseName());
            NodeId widgetDefaultTypeNodeId = OptixEdge_WizardApp.ObjectTypes.DataGridUIObj;
            if (InformationModel.Get(widgetObjectType.Value) is IUAObjectType objectType)
            {
                widgetDefaultTypeNodeId = objectType.NodeId;
            }
            editModelWidgetData = InformationModel.MakeObject<WidgetData>($"Widget{widgetNumber}");
            editModelWidgetData.WidgetType = widgetDefaultTypeNodeId;
            Owner.SetAlias("WidgetUIObjAlias", editModelWidgetData);
            toAdd = true;
        }
        else
        {
            editModelWidgetData = nodeFactory.CloneNode(widgetData, widgetData.NodeId.NamespaceIndex, NamingRuleType.None);
            Owner.Find<ComboBox>("WidgetSelectionValue").Enabled = false;
            Owner.SetAlias("WidgetUIObjAlias", editModelWidgetData);
            InitSourceDriver(editModelWidgetData.WidgetType, editModelWidgetData);
        }
        if (!InitializeVariables())
        {
            ownerDialog.Close();
            return;
        }
        CheckWidgetType(editModelWidgetData.WidgetType);
        ResolveWidgetType();
        widgetObjectType.VariableChange += WidgetObjectType_VariableChange;
    }

    public override void Stop()
    {
        widgetObjectType.VariableChange -= WidgetObjectType_VariableChange;
        columnSpan.VariableChange -= OnVariableChange;
        rowSpan.VariableChange -= OnVariableChange;
        columnStart.VariableChange -= OnVariableChange;
        rowStart.VariableChange -= OnVariableChange;
        sourceDriver.VariableChange -= OnDriverChange;
    }

    [ExportMethod]
    public void CloseDialogAndUpdate()
    {  
        string messageAction = toAdd ? "create" : "update";       
        if (!ValidateGenuineNodeId(editModelWidgetData.SourceNode))
        {
            Log.Error(LogicObject.BrowseName, $"The source node is null! Widget cannot be {messageAction}!");
            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Warning, $"The source for the widget is invalid! Unable to {messageAction} the widget!");
            return;
        }
        if (toAdd)
        {
            var widgetDataToAdd = nodeFactory.CloneNode(editModelWidgetData, editModelWidgetData.NodeId.NamespaceIndex, NamingRuleType.Mandatory);
            DashboardLogic.Instance.AddNewWidget(widgetDataToAdd);
        }
        else
        {
            DashboardLogic.Instance.UpdateWidgetData(editModelWidgetData);
        }
        editModelWidgetData.Delete();
        ownerDialog.Close();
    }

    private void WidgetObjectType_VariableChange(object sender, VariableChangeEventArgs e)
    {
        if (toAdd && InformationModel.Get(e.NewValue) is IUAObjectType objectType)
        {
            editModelWidgetData.WidgetType = objectType.NodeId;
            CheckWidgetType(editModelWidgetData.WidgetType);
        }
    }

    private void CheckWidgetType(NodeId widgetType)
    {
        if (ownerDialog != null && widgetType != null)
        {
            ownerDialog.GetVariable("ShowSpanParameters").Value = !IsPlcVariableSourceWidget(widgetType);
        }
    }

    private bool IsPlcVariableSourceWidget(NodeId widgetType)
    {
        return widgetType == OptixEdge_WizardApp.ObjectTypes.DisplayUIObj || widgetType == OptixEdge_WizardApp.ObjectTypes.SparklineUIObj;      
    }

    private static bool ValidateGenuineNodeId(NodeId nodeToCheck)
    {
        return nodeToCheck != null && nodeToCheck != NodeId.Empty;
    }

    private void ResolveWidgetType()
    {
        var enumWidgetNodeId = widgetObjectType.GetByType<EnumWidgetNodeId>();
        var sourceEnumeratioNode = enumWidgetNodeId.GetVariable("Source");
        var resolvePathResult = LogicObject.Context.ResolvePath(sourceEnumeratioNode, sourceEnumeratioNode.GetByType<DynamicLink>().Value);
        if (resolvePathResult != null && resolvePathResult.ResolvedNode is IUAVariable targetVariable)
        {
            var enumerationPairs = enumWidgetNodeId.ObjectType.GetObject("Pairs");
            try
            {
                var setValue = 0;
                foreach (var pair in enumerationPairs.GetNodesByType<IUAObject>())
                {
                    if ((NodeId)pair.GetVariable("Value").Value == editModelWidgetData.WidgetType)
                    {
                        setValue = pair.GetVariable("Key").Value;
                        break;
                    }
                }
                targetVariable.Value = setValue;
            }
            catch (Exception ex)
            {
                Log.Error(LogicObject.BrowseName, ex.Message);
            }
        }
    }

    private void InitSourceDriver(NodeId widgetType, WidgetData editModelWidgetData)
    {
        if (!IsPlcVariableSourceWidget(widgetType))
        {
            return;
        }
        if (InformationModel.Get(editModelWidgetData.SourceNode) is IUAVariable sourceVariable)
        {
            LogicObject.GetVariable("SourceDriver").Value = sourceVariable switch
            {
                FTOptix.S7TCP.Tag => (UAValue)(uint)SourceDriver.S7TCP,
                FTOptix.S7TiaProfinet.Tag => (UAValue)(uint)SourceDriver.S7Profinet,
                FTOptix.Modbus.Tag => (UAValue)(uint)SourceDriver.Modbus,
                FTOptix.RAEtherNetIP.Tag => (UAValue)(uint)SourceDriver.RAEtherNetIP,
                _ => (UAValue)(uint)SourceDriver.RAEtherNetIP,
            };
            if (CommonLogic.GetOwner(sourceVariable, FTOptix.CommunicationDriver.ObjectTypes.CommunicationStation) is CommunicationStation sourceStation && sourceStation.Owner is CommunicationDriver sourceDriver)
            {
                string folderName = sourceDriver switch
                {
                    FTOptix.S7TCP.Driver => "S7TCP",
                    FTOptix.S7TiaProfinet.Driver => "S7Profinet",
                    FTOptix.Modbus.Driver modbusDriver => modbusDriver.Protocol == FTOptix.Modbus.ModbusProtocol.ModbusTCPProtocol ? "ModbusTCP" : "ModbusRTU",
                    FTOptix.RAEtherNetIP.Driver => "RAEIP",
                    _ => "RAEIP",
                };
                LogicObject.GetVariable("SourceStation").Value = GetComboBoxStationData(folderName, sourceStation.NodeId);
            }
        }
    }

    private static NodeId GetComboBoxStationData(string folderName, NodeId sourceStation)
    {
        if (Project.Current.Get(CommonLogic.CommDriverComboBoxElementsPath).Get<Folder>(folderName) is Folder driverFolder)
        {
            foreach (ComboBoxStationData ComboBoxStationData in driverFolder.GetNodesByType<ComboBoxStationData>())
            {
                if (ComboBoxStationData.StationNodeId == sourceStation)
                {
                    return ComboBoxStationData.NodeId;
                }
            }
        }
        return NodeId.Empty;
    }

    private bool InitializeVariables()
    {
        columnStart = LogicObject.GetVariable("ColumnStart");
        if (columnStart == null)
        {
            Log.Error(LogicObject.BrowseName, "ColumnStart variable is null! Fatal error!");
            return false;
        }
        rowStart = LogicObject.GetVariable("RowStart");
        if (rowStart == null)
        {
            Log.Error(LogicObject.BrowseName, "RowStart variable is null! Fatal error!");
            return false;
        }
        columnSpan = LogicObject.GetVariable("ColumnSpan");
        if (columnSpan == null)
        {
            Log.Error(LogicObject.BrowseName, "ColumnSpan variable is null! Fatal error!");
            return false;
        }
        rowSpan = LogicObject.GetVariable("RowSpan");
        if (rowSpan == null)
        {
            Log.Error(LogicObject.BrowseName, "RowSpan variable is null! Fatal error!");
            return false;
        }
        sourceDriver = LogicObject.GetVariable("SourceDriver");
        if (sourceDriver == null)
        {
            Log.Error(LogicObject.BrowseName, "SourceDriver variable is null! Fatal error!");
            return false;
        }
        columnSpan.Value = editModelWidgetData.ColumnSpan;
        rowSpan.Value = editModelWidgetData.RowSpan;
        columnStart.Value = editModelWidgetData.ColumnStart + 1;
        rowStart.Value = editModelWidgetData.RowStart + 1;
        columnSpan.VariableChange += OnVariableChange;
        rowSpan.VariableChange += OnVariableChange;
        columnStart.VariableChange += OnVariableChange;
        rowStart.VariableChange += OnVariableChange;
        sourceDriver.VariableChange += OnDriverChange;
        return true;
    }

    private void OnVariableChange(object sender, VariableChangeEventArgs e)
    {
        if (editModelWidgetData == null)
        {
            return;
        }
        editModelWidgetData.ColumnSpan = columnSpan.Value;
        editModelWidgetData.RowSpan = rowSpan.Value;
        editModelWidgetData.ColumnStart = columnStart.Value - 1;
        editModelWidgetData.RowStart = rowStart.Value - 1;
    }

    private void OnDriverChange(object sender, VariableChangeEventArgs e)
    {
        LogicObject.GetVariable("SourceStation").Value = NodeId.Empty;
        editModelWidgetData.SourceNode = NodeId.Empty;
    }

    private enum SourceDriver
    {
        RAEtherNetIP,
        S7TCP,
        Modbus,
        S7Profinet
    }

    private WidgetData editModelWidgetData;
    private IUAVariable widgetObjectType;
    private IUAVariable columnStart;
    private IUAVariable rowStart;
    private IUAVariable columnSpan;
    private IUAVariable rowSpan;
    private IUAVariable sourceDriver;
    private NodeFactory nodeFactory;
    private bool toAdd;
    private Dialog ownerDialog;
}
