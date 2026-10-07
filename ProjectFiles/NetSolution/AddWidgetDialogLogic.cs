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
using FTOptix.EventLogger;
#endregion

public class AddWidgetDialogLogic : BaseNetLogic
{
    public static AddWidgetDialogLogic Instance { get; private set; }
    public override void Start()
    {
        Instance = this;
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
        }
        if (!InitializeVariables())
        {
            ownerDialog.Close();
            return;
        }
        Owner.GetVariable("IsNewWidget").Value = toAdd;
        CheckWidgetType(editModelWidgetData.WidgetType);
        ResolveWidgetType();
        widgetObjectType.VariableChange += WidgetObjectType_VariableChange;
        if (editModelWidgetData.SourceNode != NodeId.Empty && InformationModel.Get(editModelWidgetData.SourceNode) is IUAVariable sourceVariable)
        {
            if (Owner.FindByType<LinkedVariableToField>() is LinkedVariableToField linkedVariableToField)
            {
                linkedVariableToField.GetByType<Label>().Text = sourceVariable.BrowseName;
            }
        }
    }

    public override void Stop()
    {
        widgetObjectType.VariableChange -= WidgetObjectType_VariableChange;
        columnSpan.VariableChange -= OnVariableChange;
        rowSpan.VariableChange -= OnVariableChange;
        columnStart.VariableChange -= OnVariableChange;
        rowStart.VariableChange -= OnVariableChange;
        Instance = null;
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

    [ExportMethod]
    public void UnlinkVariableFromValue()
    {
        // Clear the node pointer to remove variable binding
        editModelWidgetData.SourceNode = NodeId.Empty;
        if (Owner.FindByType<LinkedVariableToField>() is LinkedVariableToField linkedVariableToField)
        {
            linkedVariableToField.GetByType<Label>().Text = string.Empty;
        }
    }

    public void LinkVariableToWidgetSource(NodeId sourceVariableToLink)
    {
        if (InformationModel.GetVariable(sourceVariableToLink) is IUAVariable sourceVariable)
        {
            editModelWidgetData.SourceNode = sourceVariable.NodeId;
            if (Owner.FindByType<LinkedVariableToField>() is LinkedVariableToField linkedVariableToField)
            {
                linkedVariableToField.GetByType<Label>().Text = sourceVariable.BrowseName;
            }
        }  
    }

    private void WidgetObjectType_VariableChange(object sender, VariableChangeEventArgs e)
    {
        if (toAdd && InformationModel.Get(e.NewValue) is IUAObjectType objectType)
        {
            editModelWidgetData.WidgetType = objectType.NodeId;
            UnlinkVariableFromValue();
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
        columnSpan.Value = editModelWidgetData.ColumnSpan;
        rowSpan.Value = editModelWidgetData.RowSpan;
        columnStart.Value = editModelWidgetData.ColumnStart + 1;
        rowStart.Value = editModelWidgetData.RowStart + 1;
        columnSpan.VariableChange += OnVariableChange;
        rowSpan.VariableChange += OnVariableChange;
        columnStart.VariableChange += OnVariableChange;
        rowStart.VariableChange += OnVariableChange;
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

    private WidgetData editModelWidgetData;
    private IUAVariable widgetObjectType;
    private IUAVariable columnStart;
    private IUAVariable rowStart;
    private IUAVariable columnSpan;
    private IUAVariable rowSpan;
    private NodeFactory nodeFactory;
    private bool toAdd;
    private Dialog ownerDialog;
}
