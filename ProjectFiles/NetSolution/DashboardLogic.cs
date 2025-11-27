#region Using directives
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Data;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.UI;
using FTOptix.HMIProject;
using FTOptix.NetLogic;
using FTOptix.CoreBase;
using FTOptix.Core;
using FTOptix.DataLogger;
#endregion

public class DashboardLogic : BaseNetLogic
{
    public static DashboardLogic Instance { get; private set; }

    public override void Start()
    {
        if (InformationModel.Get(LogicObject.GetVariable("WidgetGrid").Value) is GridLayout gridLayout)
        {
            widgetGrid = gridLayout;
            Instance = this;
        }
        else
        {
            Log.Error(LogicObject.BrowseName, "Missing widgets grid layout! Check design configuration!");
            NotificationsMessageHandlerLogic.Instance.RequestBannerNotification(ToastBannerNotificationLevel.Error, "Critical error - Check application logs");
        }
        configurationModeVariable = LogicObject.GetVariable("ConfigurationMode");
        if (configurationModeVariable == null)
        {
            Log.Error(LogicObject.BrowseName, "Missing configuration mode variable! Check design configuration!");
            NotificationsMessageHandlerLogic.Instance.RequestBannerNotification(ToastBannerNotificationLevel.Error, "Critical error - Check application logs");
        }
        if (InformationModel.Get(LogicObject.GetVariable("WidgetDataFolder").Value) is Folder dataFolder)
        {
            dashboardDataFolder = dataFolder;
            InitializeGridLayout();
            RegenerateDashboard();
            lastRun = DateTime.Now.AddDays(-1);
            checkConfigurationModeTask = new DelayedTask(checkConfigurationMode, 100, LogicObject);
            checkConfigurationModeTask.Start();
        }
        else
        {
            Log.Error(LogicObject.BrowseName, "Missing dashboard data folder! Check design configuration!");
            NotificationsMessageHandlerLogic.Instance.RequestBannerNotification(ToastBannerNotificationLevel.Error, "Critical error - Check application logs");
        }
    }

    public override void Stop()
    {
        widgetGrid = null;
        Instance = null;
        try
        {
            Session.FindByType<Window>().GetVariable("ConfigMode").Value = configurationModeVariable.Value;
        }
        catch
        {
            // No session find
        }
    }

    #region Public method
    public void AddNewWidget(WidgetData widgetDataToAdd)
    {
        dashboardDataFolder.Add(widgetDataToAdd);
        var newUIWidget = GenerateUIWidgetFromData(widgetDataToAdd);
        RegenerateGridLayout();
        AddWidgetToGrid(newUIWidget); 
    }

    public void UpdateWidgetData(WidgetData editModelWidgetData)
    {
        if (dashboardDataFolder.Get<WidgetData>(editModelWidgetData.BrowseName) is WidgetData widgetData)
        {
            // Check if dimensions changed
            bool newDimensions = widgetData.RowSpan != editModelWidgetData.RowSpan || widgetData.ColumnSpan != editModelWidgetData.ColumnSpan ||
                widgetData.RowStart != editModelWidgetData.RowStart || widgetData.ColumnStart != editModelWidgetData.ColumnStart;
            if (newDimensions)
            {
                RegenerateGridLayout(editModelWidgetData);
            }
            // Update all properties except WidgetType
            SaveNewParameters(widgetData, editModelWidgetData);
            if (widgetGrid.Get(widgetData.BrowseName) is TrendUIObj trendToUpdate)
            {
                // Update only Trend specific properties
                if (InformationModel.Get(widgetData.SourceNode) is DataLogger sourceDatalogger)
                {
                    var trendNode = trendToUpdate.Find<Trend>("TrendObj");
                    // Get the list of BrowseNames from VariablesToLog
                    var variablesToLogNames = sourceDatalogger.VariablesToLog.Select(v => v.BrowseName).ToHashSet();
                    // Get pens that don't exist in VariablesToLog and remove them
                    foreach (var penToRemove in trendNode.Pens.Where(p => !variablesToLogNames.Contains(p.BrowseName)))
                    {
                        trendNode.Pens.Remove(penToRemove);
                    }
                    trendNode.Model = sourceDatalogger.NodeId;
                    int[] parametersArray = widgetData.GetVariable("ConfigurationParameters").Value;
                    uint[] configurationColors = widgetData.GetVariable("ConfigurationColors").Value;
                    int parametersArrayBaseOffset = widgetData.IndexOfPensArray;
                    int index = 0;
                    foreach (var variableToLog in sourceDatalogger.VariablesToLog)
                    {
                        var trendPen = CreateOrUpdateTrendPen(trendNode, variableToLog);
                        UpdateTrendPenParameters(widgetData, parametersArray, configurationColors, parametersArrayBaseOffset, index, trendPen);
                        index++;
                    }
                }
            }
        }
    }

    public List<int> GetListTotalWidgetsBrowseName()
    {
        return WidgetDatas.Select(x => int.Parse(x.BrowseName.Replace("widget", string.Empty, StringComparison.InvariantCultureIgnoreCase))).ToList();
    }

    [ExportMethod]
    public void ClearDashboard()
    {
        foreach (var widget in widgetGrid.GetNodesByType<IUAObject>().Where(x => x.IsInstanceOf(OptixEdge_WizardApp.ObjectTypes.BaseWidgetUIObject)))
        {
            try
            {
                if (widget.GetAlias("WidgetData") is WidgetData widgetData)
                {
                    widgetData.Delete();
                }
                widgetGrid.Remove(widget);
            }
            catch
            {
                // Nothing important
            }
        }
        RegenerateGridLayout();
    }

    [ExportMethod]
    public void SwitchToConfigureMode()
    {
        configurationModeVariable.Value = !ConfigurationMode;
    }

    [ExportMethod]
    public void ResizeGridLayout()
    {
        RegenerateGridLayout();
    }
    #endregion

     private void RegenerateGridLayout(WidgetData editWidgetData = null)
    {
        if (widgetGrid != null && (DateTime.Now - lastRun).TotalMilliseconds > 250)
        {
            GenerateGridColumnsAndRows(editWidgetData, out var targetColumnsLayout, out var targetRowsLayout);
            if (targetColumnsLayout.Count != widgetGrid.Columns.Length || targetRowsLayout.Count != widgetGrid.Rows.Length)
            {
                ApplyNewGridLayout(targetColumnsLayout, targetRowsLayout);
                lastRun = DateTime.Now;
            }
        }
    }

    private void GenerateGridColumnsAndRows(WidgetData editWidgetData, out List<string> targetColumnsLayout, out List<string> targetRowsLayout)
    {
        var totalWidgetDatas = GetUpdatedWidgedDataCollection(editWidgetData);
        targetColumnsLayout = [];
        targetRowsLayout = [];
        var maxColumns = CalculateMaxColumnsFromWindow();
        var rowSpan = 0f;
        var columnSpan = 0f;
        var maxRowSelected = -1.0f;
        var countOfWidgets = (float)totalWidgetDatas.Count;
        if (countOfWidgets > 0)
        {
            rowSpan = (float)totalWidgetDatas.Sum(x => x.RowSpan - 1.0f);
            columnSpan = (float)totalWidgetDatas.Sum(x => x.ColumnSpan - 1.0f);
            maxRowSelected = totalWidgetDatas.Max(x => x.RowStart + 1.0f);
        }
        var rowsNumbers = ((countOfWidgets + columnSpan) / maxColumns) + rowSpan + maxRowSelected;
        for (int i = 0; i < maxColumns; i++)
        {
            string columnLayout = "1fr";
            targetColumnsLayout.Add(columnLayout);
        }
        rowsNumbers = rowsNumbers < 1.0f ? 1.0f : rowsNumbers;
        for (int i = 0; i < rowsNumbers; i++)
        {
            targetRowsLayout.Add("192");
        }
    }

    private List<WidgetData> GetUpdatedWidgedDataCollection(WidgetData editWidgetData)
    {
        List<WidgetData> updatedWidgetDatas = WidgetDatas;
        if (editWidgetData != null)
        {
            updatedWidgetDatas.Where(x => x.BrowseName == editWidgetData.BrowseName).ToList().ForEach(x =>
            {
                updatedWidgetDatas.Remove(x);
                updatedWidgetDatas.Add(editWidgetData);
            });
        }
        return updatedWidgetDatas;
    }

    private void ApplyNewGridLayout(List<string> targetColumnsLayout, List<string> targetRowsLayout)
    {
        widgetGrid.Columns = [.. targetColumnsLayout];
        widgetGrid.Rows = [.. targetRowsLayout];
    }

    private void checkConfigurationMode()
    {
        {
            Instance = this;
            configurationModeVariable.Value = false;
            SwitchToConfigureMode();
        }
    }

    private void AddWidgetToGrid(IUAObject widgetToAdd)
    {
        if (widgetToAdd is DataGridUIObj)
        {
            RegenerateDataGridColums(widgetToAdd);
        }
        widgetGrid.Add(widgetToAdd);
    }

    private void InitializeGridLayout()
    {
        try
        {
            GenerateGridColumnsAndRows(null, out var columnsLayout, out var rowsLayout);
            ApplyNewGridLayout(columnsLayout, rowsLayout);
        }
        catch (Exception ex)
        {
            Log.Error(LogicObject.BrowseName, ex.Message);
        }

    }

    private void RegenerateDashboard()
    {
        foreach (var widgetData in WidgetDatas)
        {
            var widget = GenerateUIWidgetFromData(widgetData);
            AddWidgetToGrid(widget);
        }
        memoryCountWidgets = WidgetDatas.Count;
    }

    private Item GenerateUIWidgetFromData(WidgetData widgetData)
    {
        var widget = InformationModel.MakeObject(widgetData.BrowseName, widgetData.WidgetType) as Item;
        widget.SetAlias("WidgetData", widgetData);
        switch (widget)
        {
            case TrendUIObj:
                if (InformationModel.Get(widgetData.SourceNode) is DataLogger sourceDatalogger)
                {
                    var trendNode = widget.Find<Trend>("TrendObj");
                    trendNode.Model = sourceDatalogger.NodeId;
                    int[] parametersArray = widgetData.GetVariable("ConfigurationParameters").Value;
                    uint[] configurationColors = widgetData.GetVariable("ConfigurationColors").Value;
                    int parametersArrayBaseOffset = widgetData.IndexOfPensArray;
                    int index = 0;
                    foreach (var variableToLog in sourceDatalogger.VariablesToLog)
                    {
                        var trendPen = CreateOrUpdateTrendPen(trendNode, variableToLog);
                        UpdateTrendPenParameters(widgetData, parametersArray, configurationColors, parametersArrayBaseOffset, index, trendPen);
                        index++;
                    }
                }
                break;
        }
        widget.HorizontalAlignment = HorizontalAlignment.Stretch;
        widget.VerticalAlignment = VerticalAlignment.Stretch;
        return widget;
    }

    private void UpdateTrendPenParameters(WidgetData widgetData, int[] parametersArray, uint[] configurationColors, int parametersArrayBaseOffset, int index, TrendPen trendPen)
    {
        int penOffset = index * 2;
        trendPen.Thickness = (float)parametersArray[penOffset + parametersArrayBaseOffset];
        trendPen.Enabled = parametersArray[penOffset + parametersArrayBaseOffset + 1] != 0;
        trendPen.Title = new LocalizedText(widgetData.ConfigurationTextParameters[penOffset + 1], Session.ActualLocaleId);
        trendPen.Color = new Color(configurationColors[index]);
    }

    private static void RegenerateDataGridColums(IUANode dataGridWidget)
    {
        if (dataGridWidget.Find("DataGridObj") is DataGrid dataGridObj && dataGridWidget.GetAlias("WidgetData") is WidgetData widgetData)
        {
            dataGridObj.Columns.Clear();
            string tableName = ExtractTableName(widgetData.Query);
            if (Project.Current.Get(CommonLogic.LoggersFolderPath).Get(tableName) is DataLogger targetDataLogger)
            {
                var localTimestampColumn = GenerateDataGridLabelColumn("LocalTimestamp");
                dataGridObj.Columns.Add(GenerateDataGridLabelColumn("Timestamp"));
                dataGridObj.Columns.Add(localTimestampColumn);
                foreach (var variableToLog in targetDataLogger.VariablesToLog)
                {
                    dataGridObj.Columns.Add(GenerateDataGridLabelColumn(variableToLog.BrowseName));
                }
                dataGridObj.SortOrder = SortOrder.Descending;
                dataGridObj.SortColumn = localTimestampColumn.NodeId;
            }
            if (dataGridObj.Status == NodeStatus.Started)
            {
                dataGridObj.Refresh();
            }
        }
    }

    private static DataGridColumn GenerateDataGridLabelColumn(string columnName)
    {
        var newDataGridColumn = InformationModel.MakeObject<DataGridColumn>(columnName);
        newDataGridColumn.Title = columnName;
        newDataGridColumn.DataItemTemplate = InformationModel.MakeObject<DataGridLabelItemTemplate>("DataItemTemplate");
        var dynamicLink = InformationModel.MakeVariable<DynamicLink>("DynamicLink", FTOptix.Core.DataTypes.NodePath);
        dynamicLink.Value = "{Item}/" + NodePath.EscapeNodePathBrowseName(columnName);
        newDataGridColumn.DataItemTemplate.GetVariable("Text").Refs.AddReference(FTOptix.CoreBase.ReferenceTypes.HasDynamicLink, dynamicLink);
        newDataGridColumn.OrderBy = dynamicLink.Value;
        return newDataGridColumn;
    }

    private static string ExtractTableName(string query)
    {
        // Regular expression to match the table name
        string pattern = @"FROM\s+(\w+)";
        Match match = Regex.Match(query, pattern, RegexOptions.IgnoreCase);
        if (match.Success && match.Groups.Count == 2)
        {
            return match.Groups[1].Value;
        }
        return string.Empty;
    }

    private float CalculateMaxColumnsFromWindow()
    {
        float maxColumns;
        var mainWindow = Session.FindByType<Window>();
        if (mainWindow.Width >= 1280)
        {
            maxColumns = 4f;
        }
        else if (mainWindow.Width >= 640)
        {
            maxColumns = 2f;
        }
        else
        {
            maxColumns = 1f;
        }

        return maxColumns;
    }

    private static void SaveNewParameters(WidgetData widgetData, WidgetData editModelWidgetData)
    {
        widgetData.WidgetDisplayName = editModelWidgetData.WidgetDisplayName;
        widgetData.EngineeringUnit = editModelWidgetData.EngineeringUnit;
        widgetData.SourceNode = editModelWidgetData.SourceNode;
        widgetData.ColumnStart = editModelWidgetData.ColumnStart;
        widgetData.ColumnSpan = editModelWidgetData.ColumnSpan;
        widgetData.RowStart = editModelWidgetData.RowStart;
        widgetData.RowSpan = editModelWidgetData.RowSpan;
        widgetData.Query = editModelWidgetData.Query;
        widgetData.ConfigurationDurations = editModelWidgetData.ConfigurationDurations;
        widgetData.GetVariable("ConfigurationParameters").Value = editModelWidgetData.GetVariable("ConfigurationParameters").Value;
        widgetData.GetVariable("ConfigurationColors").Value = editModelWidgetData.GetVariable("ConfigurationColors").Value;
        widgetData.ConfigurationTextParameters = editModelWidgetData.ConfigurationTextParameters;
        widgetData.IndexOfPensArray = editModelWidgetData.IndexOfPensArray;
    }

    public TrendPen CreateOrUpdateTrendPen(Trend trendObj, VariableToLog sourceVariableToLog)
    {
        var trendPen = trendObj.Pens.FirstOrDefault(p => p.BrowseName == sourceVariableToLog.BrowseName, null);
        if (trendPen == null)
        {
            trendPen = InformationModel.MakeVariable<TrendPen>(sourceVariableToLog.BrowseName, OpcUa.DataTypes.BaseDataType);
            trendPen.Enabled = true;
            trendPen.Title = new LocalizedText(sourceVariableToLog.BrowseName, Session.ActualLocaleId);
            trendPen.Thickness = -1;
            trendPen.Color = new Color(255, (byte)new Random().Next(255), (byte)new Random().Next(255), (byte)new Random().Next(255));
            trendObj.Pens.Add(trendPen);
        }
        trendPen.SetDynamicLink(sourceVariableToLog.LastValueVariable);
        return trendPen;
    }

    private bool ConfigurationMode => configurationModeVariable.Value;

    private float memoryCountWidgets;
    private IUAVariable configurationModeVariable;
    private GridLayout widgetGrid;
    private Folder dashboardDataFolder;
    private DateTime lastRun;
    private DelayedTask checkConfigurationModeTask;
    private List<WidgetData> WidgetDatas => dashboardDataFolder.GetNodesByType<WidgetData>().ToList();
}
