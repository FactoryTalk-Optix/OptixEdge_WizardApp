#region Using directives
using System;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.UI;
using FTOptix.HMIProject;
using FTOptix.CoreBase;
using FTOptix.NetLogic;
using FTOptix.WebUI;
using FTOptix.Core;
using System.Net;
using System.Collections.Immutable;
using Microsoft.VisualBasic;
using System.Threading;
using System.Linq;
using FTOptix.NativeUI;
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
        widgetUIObject = Owner.GetAlias("WidgetUIObjAlias") as Item;
        if (widgetUIObject == null)
        {
            var widgetNumber = CommonLogic.FindMissingNumber(DashboardLogic.Instance.GetListTotalWidgetsBrowseName());
            NodeId widgetDefaultTypeNodeId = OptixEdge_WizardApp.ObjectTypes.DataGridUIObj;
            if (InformationModel.Get(widgetObjectType.Value) is IUAObjectType objectType)
            {
                widgetDefaultTypeNodeId = objectType.NodeId;
            }
            widgetUIObject = (Item)InformationModel.MakeObject($"widget{widgetNumber}", widgetDefaultTypeNodeId);
            widgetUIObject.HorizontalAlignment = HorizontalAlignment.Stretch;
            Owner.SetAlias("WidgetUIObjAlias", widgetUIObject);
            toAdd = true;
        }
        else
        {
            editModelObjectUI = nodeFactory.CloneNode(widgetUIObject, widgetUIObject.NodeId.NamespaceIndex, NamingRuleType.Mandatory);
            Owner.Find<ComboBox>("WidgetSelectionValue").Enabled = false;
            Owner.SetAlias("WidgetUIObjAlias", editModelObjectUI);
        }
        CheckWidgetType(widgetUIObject.ObjectType);
        var enumWidgetNodeId = widgetObjectType.GetByType<EnumWidgetNodeId>();
        var sourceEnumeratioNode = enumWidgetNodeId.GetVariable("Source");
        string dynamicLinkValue = sourceEnumeratioNode.GetByType<TrendPen>()?.Value ?? "";
        var resolvePathResult = LogicObject.Context.ResolvePath(sourceEnumeratioNode, sourceEnumeratioNode.GetByType<DynamicLink>().Value);
        if (resolvePathResult != null && resolvePathResult.ResolvedNode is IUAVariable targetVariable)
        {
            var enumerationPairs = enumWidgetNodeId.ObjectType.GetObject("Pairs");
            try
            {
                var setValue = 0;
                foreach (var pair in enumerationPairs.GetNodesByType<IUAObject>())
                {
                    if ((NodeId)pair.GetVariable("Value").Value == widgetUIObject.ObjectType.NodeId)
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
        widgetObjectType.VariableChange += WidgetObjectType_VariableChange;
    }

    public override void Stop()
    {
        if (toAdd && widgetUIObject != null && widgetUIObject.Owner == null)
        {
            widgetUIObject.Delete();
            widgetUIObject = null;
        }
        widgetObjectType.VariableChange -= WidgetObjectType_VariableChange;
    }

    [ExportMethod]
    public void CloseDialogAndUpdate()
    {       
        if (toAdd)
        {
            if (!ValidateGenuineNodeId(widgetUIObject.GetVariable("ObjPointer").Value))
            {
                Log.Error(LogicObject.BrowseName, "The source node is null! Widget cannot be added!");
                NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Warning, "The source for the widget is invalid! Unable to create the widget!");
                return;
            }
            // By passing the widget as an alias and with owner null, the Alias becomes the new widget owner.
            // Have to remove all references with the alias before add in Dashboard
            if (widgetUIObject.Owner is IUANode widgetOwner)
            {
                foreach (var referenceType in widgetOwner.Refs.GetReferences().Where(x => x.TargetNode.NodeId == widgetUIObject.NodeId))
                {
                    widgetOwner.Refs.RemoveReference(referenceType.ReferenceTypeId, widgetUIObject.NodeId);
                }
            }
            if (widgetUIObject is TrendUIObj)
            {
                var trendNode = widgetUIObject.Find<Trend>("TrendObj");
                trendNode.Model = widgetUIObject.GetVariable("ObjPointer").Value;
                UpdatePensParameters(widgetUIObject.Find<Trend>("TrendObj"), null, widgetUIObject.GetVariable("ObjParameters"),widgetUIObject.GetVariable("ObjColors"), widgetUIObject.GetVariable("ObjTextParameters"), widgetUIObject.GetVariable("IndexOfPensArray").Value);
                
            }
            DashboardLogic.Instance.AddNewWidget(widgetUIObject);
        }
        else if (toRefactor)
        {
            // By passing the widget as an alias and with owner null, the Alias becomes the new widget owner.
            // Have to remove all references with the alias before add in Dashboard
            if (editModelObjectUI.Owner is IUANode widgetOwner)
            {
                foreach (var referenceType in widgetOwner.Refs.GetReferences().Where(x => x.TargetNode.NodeId == editModelObjectUI.NodeId))
                {
                    widgetOwner.Refs.RemoveReference(referenceType.ReferenceTypeId, editModelObjectUI.NodeId);
                }
            }
            DashboardLogic.Instance.ChangeWidgetType(widgetUIObject, editModelObjectUI as Item);
        }
        else
        {
            if (!ValidateGenuineNodeId(editModelObjectUI.GetVariable("ObjPointer").Value))
            {
                Log.Error(LogicObject.BrowseName, "The source node is null! Widget cannot be edit!");
                NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Warning, "The source for the widget is invalid! Unable to edit the widget!");
                return;
            }
            widgetUIObject.GetVariable("ObjName").Value = editModelObjectUI.GetVariable("ObjName").Value;
            widgetUIObject.GetVariable("ObjEngUnit").Value = editModelObjectUI.GetVariable("ObjEngUnit").Value;
            widgetUIObject.GetVariable("ObjPointer").Value = editModelObjectUI.GetVariable("ObjPointer").Value;
            switch (widgetUIObject)
            {
                case DataGridUIObj:
                widgetUIObject.GetVariable("ObjQuery").Value = editModelObjectUI.GetVariable("ObjQuery").Value;
                    widgetUIObject.GetVariable("ObjDurations").Value = editModelObjectUI.GetVariable("ObjDurations").Value;
                    break;
                case TrendUIObj:
                    UpdatePensParameters(editModelObjectUI.Find<Trend>("TrendObj"),widgetUIObject.Find<Trend>("TrendObj"), editModelObjectUI.GetVariable("ObjParameters"),editModelObjectUI.GetVariable("ObjColors"), editModelObjectUI.GetVariable("ObjTextParameters"), editModelObjectUI.GetVariable("IndexOfPensArray").Value);
                    widgetUIObject.GetVariable("ObjQuery").Value = editModelObjectUI.GetVariable("ObjQuery").Value;
                    widgetUIObject.GetVariable("ObjDurations").Value = editModelObjectUI.GetVariable("ObjDurations").Value;
                    widgetUIObject.GetVariable("ObjColors").Value = editModelObjectUI.GetVariable("ObjColors").Value;
                    widgetUIObject.GetVariable("ObjParameters").Value = editModelObjectUI.GetVariable("ObjParameters").Value;
                    widgetUIObject.GetVariable("ObjTextParameters").Value = editModelObjectUI.GetVariable("ObjTextParameters").Value;
                    break;
                case SparklineUIObj:
                    widgetUIObject.GetVariable("ObjDurations").Value = editModelObjectUI.GetVariable("ObjDurations").Value;
                    widgetUIObject.GetVariable("ObjColors").Value = editModelObjectUI.GetVariable("ObjColors").Value;
                    widgetUIObject.GetVariable("ObjParameters").Value = editModelObjectUI.GetVariable("ObjParameters").Value;
                    break;
            }
            widgetUIObject.HorizontalAlignment = HorizontalAlignment.Stretch;
            widgetUIObject.VerticalAlignment = VerticalAlignment.Stretch;
            if (widgetUIObject.GetByType<GridLayoutProperties>() is GridLayoutProperties layoutProperties && editModelObjectUI.GetByType<GridLayoutProperties>() is GridLayoutProperties memoryLayoutProperties)
            {
                layoutProperties.ColumnStart = memoryLayoutProperties.ColumnStart;
                layoutProperties.ColumnSpan = memoryLayoutProperties.ColumnSpan;
                layoutProperties.RowStart = memoryLayoutProperties.RowStart;
                layoutProperties.RowSpan = memoryLayoutProperties.RowSpan;
            }
            DashboardLogic.Instance.UpdateWidgetData();
        }
        ownerDialog.Close();
    }
    
    private void WidgetObjectType_VariableChange(object sender, VariableChangeEventArgs e)
    {
        if (InformationModel.Get(e.NewValue) is IUAObjectType objectType)
        {
            if (toAdd)
            {
                var widgetNumber = CommonLogic.FindMissingNumber(DashboardLogic.Instance.GetListTotalWidgetsBrowseName());
                widgetUIObject.Delete();
                widgetUIObject = (Item)InformationModel.MakeObject($"Widget{widgetNumber}", objectType.NodeId);
                widgetUIObject.HorizontalAlignment = HorizontalAlignment.Stretch;
                widgetUIObject.VerticalAlignment = VerticalAlignment.Stretch;
                Owner.SetAlias("WidgetUIObjAlias", widgetUIObject);
            }
            else
            {
                toRefactor = true;
                var clonedEditModel = nodeFactory.CloneNode(editModelObjectUI, editModelObjectUI.NodeId.NamespaceIndex, NamingRuleType.Mandatory);
                editModelObjectUI = InformationModel.MakeObject(editModelObjectUI.BrowseName, objectType.NodeId);
                editModelObjectUI.GetVariable("ObjName").Value = clonedEditModel.GetVariable("ObjName").Value;
                editModelObjectUI.GetVariable("ObjEngUnit").Value = clonedEditModel.GetVariable("ObjEngUnit").Value;
                editModelObjectUI.GetVariable("ObjPointer").Value = clonedEditModel.GetVariable("ObjPointer").Value;
                (editModelObjectUI as Item).HorizontalAlignment = HorizontalAlignment.Stretch;
                (editModelObjectUI as Item).VerticalAlignment = VerticalAlignment.Stretch;
                if (editModelObjectUI.GetByType<GridLayoutProperties>() is GridLayoutProperties layoutProperties && clonedEditModel.GetByType<GridLayoutProperties>() is GridLayoutProperties memoryLayoutProperties)
                {
                    layoutProperties.ColumnStart = memoryLayoutProperties.ColumnStart;
                    layoutProperties.ColumnSpan = memoryLayoutProperties.ColumnSpan;
                    layoutProperties.RowStart = memoryLayoutProperties.RowStart;
                    layoutProperties.RowSpan = memoryLayoutProperties.RowSpan;
                }
                Owner.SetAlias("WidgetUIObjAlias", editModelObjectUI);
            }
            CheckWidgetType(objectType);
        }
    }

    private void UpdatePensParameters(Trend widgetSourceTrend, Trend widgetActualTrend, IUAVariable parametersArrayVariable, IUAVariable colorsArrayVariable, IUAVariable textParametersArrayVariable, ushort startIndexOfArray)
    {
        int[] parametersArray = parametersArrayVariable.Value;
        uint[] colorsArray = colorsArrayVariable.Value;
        string[] textParametersArray = textParametersArrayVariable.Value;
        int parametersArrayIndex = startIndexOfArray;
        int colorsArrayIndex = 0;
        int textParametersArrayIndex = 0;
        foreach (var widgetSourceTrendPen in widgetSourceTrend.Pens)
        {
            
            parametersArray[parametersArrayIndex] = (int)widgetSourceTrendPen.Thickness;
            parametersArray[parametersArrayIndex+1] = widgetSourceTrendPen.Enabled ? 1 : 0;
            textParametersArray[textParametersArrayIndex] = widgetSourceTrendPen.Title.Text ;
            colorsArray[colorsArrayIndex] = widgetSourceTrendPen.Color.ARGB;
            try
            {
                if (widgetActualTrend != null)
                {
                    var widgetActualTrendPen = widgetActualTrend.Pens.Get(widgetSourceTrendPen.BrowseName);
                    widgetActualTrendPen.Thickness = widgetSourceTrendPen.Thickness;
                    widgetActualTrendPen.Enabled = widgetSourceTrendPen.Enabled;
                    widgetActualTrendPen.Title = widgetSourceTrendPen.Title;
                    widgetActualTrendPen.Color = widgetSourceTrendPen.Color;
                }
            }
            catch
            {
                // the widget trend pen does not exist
            }
            colorsArrayIndex++;
            parametersArrayIndex++;
            textParametersArrayIndex++;
        }
        parametersArrayVariable.Value = parametersArray;
        colorsArrayVariable.Value = colorsArray;
        textParametersArrayVariable.Value = textParametersArray;
    }

    private void CheckWidgetType(IUAObjectType widgetType)
    {
        if (ownerDialog != null && widgetType != null)
        {
            ownerDialog.GetVariable("ShowSpanParameters").Value = widgetType.NodeId switch
            {
                var _ when widgetType.NodeId == OptixEdge_WizardApp.ObjectTypes.DisplayUIObj => false,
                var _ when widgetType.NodeId == OptixEdge_WizardApp.ObjectTypes.SparklineUIObj => false,
                _ => true
            };
        }
    }

    private static bool ValidateGenuineNodeId(NodeId nodeToCheck)
    {
        return nodeToCheck != null && nodeToCheck != NodeId.Empty;
    }

    private Item widgetUIObject;
    private IUAObject editModelObjectUI;
    private IUAVariable widgetObjectType;
    private NodeFactory nodeFactory;
    private bool toAdd;
    private bool toRefactor;
    private Dialog ownerDialog;
}
