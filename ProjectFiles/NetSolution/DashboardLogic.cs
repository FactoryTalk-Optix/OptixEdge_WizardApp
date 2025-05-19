#region Using directives
using System;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.UI;
using FTOptix.HMIProject;
using FTOptix.NetLogic;
using FTOptix.WebUI;
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
using FTOptix.OPCUAClient;
using FTOptix.Core;
using System.Linq;
using System.Threading;
using System.Collections.Generic;
using FTOptix.MQTTBroker;
using FTOptix.Store;
using FTOptix.SQLiteStore;
using FTOptix.InfluxDBStore;
using FTOptix.InfluxDBStoreLocal;
using FTOptix.ODBCStore;
using FTOptix.DataLogger;
using FTOptix.InfluxDBStoreRemote;
using FTOptix.AuditSigning;
using System.Text.RegularExpressions;
using System.Data;
using FTOptix.NativeUI;
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
    public void AddNewWidget(IUAObject widgetToAdd)
    {
        var addWidget = widgetGrid.Get(AddWidgetBrowseName);
        if (ConfigurationMode && addWidget != null)
        {
            widgetGrid.Remove(addWidget);
        }
        AddWidgetToGrid(widgetToAdd);
        if (ConfigurationMode)
        {
            AddWidgetToGrid(InformationModel.Make<AddWidget>(AddWidgetBrowseName));
        }
        UpdateModelCollection();
        ResizeGridLayout(true);
    }

    public void UpdateWidgetData()
    {
        UpdateModelCollection();
        ResizeGridLayout(true);
    }

    public void ChangeWidgetType(IUANode actualWidget, Item newWidgetNode)
    {
        widgetGrid.Remove(actualWidget);
        AddNewWidget(newWidgetNode);
    }

    public int GetTotalCountOfWidgets()
    {
        return GetTotalWidgets().Count;
    }

    public List<int> GetListTotalWidgetsBrowseName()
    {
        return GetTotalWidgets().Select(x => int.Parse(x.BrowseName.Replace("widget", string.Empty, StringComparison.InvariantCultureIgnoreCase))).ToList();
    }

    [ExportMethod]
    public void ClearDashboard()
    {
        var dummyWidget = InformationModel.Make<BaseWidgetUIObject>("Dummy");
        foreach (var widget in widgetGrid.Children.Where(x => x.NodeClass == NodeClass.Object && (x as IUAObject).IsInstanceOf(dummyWidget.ObjectType.NodeId)))
        {
            try
            {
                widgetGrid.Remove(widget);
            }
            catch
            {
                // Nothing important
            }
        }
        UpdateModelCollection();
    }

    [ExportMethod]
    public void SwitchToConfigureMode()
    {
        if (ConfigurationMode)
        {
            var addWidget = widgetGrid.GetNodesByType<AddWidget>().FirstOrDefault(InformationModel.Make<AddWidget>("Default"));
            if (addWidget != null && addWidget.BrowseName != "Default" && addWidget.Owner != null)
            {
                widgetGrid.Remove(addWidget);
            }
        }
        else
        {
            var addWidget = InformationModel.Make<AddWidget>(AddWidgetBrowseName);
            AddWidgetToGrid(addWidget);
        }
        configurationModeVariable.Value = !ConfigurationMode;        
        ResizeGridLayout(true);
    }

    [ExportMethod]
    public void ResizeGridLayout(bool requestForceResize = false)
    {
        if (widgetGrid != null && (DateTime.Now - lastRun).TotalMilliseconds > 250)
        {
            List<string> targetColumnsLayout = [];
            List<string> targetRowsLayout = [];
            bool forceResize = false;
            var maxColumnsForResolution = CalculateMaxColumnsFromWindow();
            var maxColumns = maxColumnsForResolution;
            var rowSpan = 0f;
            var columnSpan = 0f;
            var widgetsCollection = GetTotalWidgets();
            var countOfWidgets = (float)widgetsCollection.Count;
            if (countOfWidgets > 0)
            {   
                rowSpan = (float)widgetsCollection.Sum(x => x.GetByType<GridLayoutProperties>().RowSpan - 1.0f);
                columnSpan = (float)widgetsCollection.Sum(x => x.GetByType<GridLayoutProperties>().ColumnSpan - 1.0f);
            }
            if (ConfigurationMode)
            {
                forceResize = countOfWidgets != memoryCountWidgets || requestForceResize;
                memoryCountWidgets = countOfWidgets;
            }
            if ((countOfWidgets + columnSpan) < maxColumnsForResolution)
            {
                maxColumns = countOfWidgets + columnSpan;
            }
            var rowsNumbers = ((countOfWidgets + columnSpan) / maxColumns) + rowSpan;
            for (int i = 0; i < maxColumns; i++)
            {
                string columnLayout = "1fr";
                targetColumnsLayout.Add(columnLayout);
            }
            rowsNumbers = rowsNumbers < 1.0f ? 1.0f : rowsNumbers;
            if (ConfigurationMode)
            {
                rowsNumbers += 1.0f;
            }
            for (int i = 0; i < rowsNumbers; i++)
            {
                targetRowsLayout.Add("192");
            }
            if (targetColumnsLayout.Count != widgetGrid.Columns.Length || targetRowsLayout.Count != widgetGrid.Rows.Length || forceResize)
            {
                lastRun = DateTime.Now;
                GenerateNewGridLayout(targetColumnsLayout, targetRowsLayout);
                foreach (var widget in widgetsCollection)
                {
                    if (widget.GetByType<GridLayoutProperties>() is GridLayoutProperties gridProperties)
                    {
                        if (gridProperties.ColumnSpan > maxColumnsForResolution)
                        {
                            gridProperties.ColumnSpan = (int)maxColumnsForResolution;
                        }
                        else if (dashboardDataFolder.GetNodesByType<WidgetData>().FirstOrDefault(x => x.WidgetBrowseName == widget.BrowseName, null) is WidgetData widgetData && widgetData.ColumnSpan != gridProperties.ColumnSpan)
                        {
                            gridProperties.ColumnSpan = widgetData.ColumnSpan > (int)maxColumnsForResolution ? (int)maxColumnsForResolution : widgetData.ColumnSpan;
                        }
                    }
                }
                if (ConfigurationMode && widgetGrid.GetByType<AddWidget>() is AddWidget addWidget)
                {
                    addWidget.GridLayoutProperties.RowStart = targetRowsLayout.Count - 1;
                }
            }
        }
    }


    private void GenerateNewGridLayout(List<string> targetColumnsLayout, List<string> targetRowsLayout)
    {
        widgetGrid.Columns = [.. targetColumnsLayout];
        widgetGrid.Rows = [.. targetRowsLayout];
    }
    #endregion

    private List<IUAObject> GetTotalWidgets()
    {
        var dummyWidget = InformationModel.Make<BaseWidgetUIObject>("Dummy");
        return widgetGrid.Children.Where(x => x.NodeClass == NodeClass.Object).Cast<IUAObject>().Where(x => x.IsInstanceOf(dummyWidget.ObjectType.NodeId)).ToList();
    }

    private void checkConfigurationMode()
    {
        if (Session.FindByType<Window>().GetVariable("ConfigMode").Value)
        {
            Instance = this;
            configurationModeVariable.Value = false;
            SwitchToConfigureMode();
        }
    }

    private void UpdateModelCollection()
    {
        dashboardDataFolder.Children.Clear();
        int i = 0;
        foreach (var widget in GetTotalWidgets())
        {
            WidgetData widgetData = InformationModel.MakeObject<WidgetData>(i.ToString());
            widgetData.WidgetType = widget.ObjectType.NodeId;
            widgetData.WidgetBrowseName = widget.BrowseName;
            widgetData.ObjName = widget.GetVariable("ObjName").Value;
            widgetData.ObjEngUnit = widget.GetVariable("ObjEngUnit").Value;
            widgetData.ObjPointer = widget.GetVariable("ObjPointer").Value;
            switch (widget)
            {
                case DataGridUIObj:
                    widgetData.ObjDurations = widget.GetVariable("ObjDurations").Value;
                    widgetData.ObjQuery = widget.GetVariable("ObjQuery").Value;
                    break;
                case TrendUIObj:
                    widgetData.ObjDurations = widget.GetVariable("ObjDurations").Value;
                    widgetData.GetVariable("ObjParameters").Value = widget.GetVariable("ObjParameters").Value;
                    widgetData.GetVariable("ObjColors").Value = widget.GetVariable("ObjColors").Value;
                    widgetData.GetVariable("ObjTextParameters").Value = widget.GetVariable("ObjTextParameters").Value;
                    widgetData.ObjQuery = widget.GetVariable("ObjQuery").Value;
                    break;
                case SparklineUIObj:
                    widgetData.ObjDurations = widget.GetVariable("ObjDurations").Value;
                    widgetData.GetVariable("ObjParameters").Value = widget.GetVariable("ObjParameters").Value;
                    widgetData.GetVariable("ObjColors").Value = widget.GetVariable("ObjColors").Value;
                    break;

            }
            if (widget.GetByType<GridLayoutProperties>() is GridLayoutProperties layoutProperties)
            {
                widgetData.ColumnStart = layoutProperties.ColumnStart;
                widgetData.ColumnSpan = layoutProperties.ColumnSpan;
                widgetData.RowStart = layoutProperties.RowStart;
                widgetData.RowSpan = layoutProperties.RowSpan;
            }
            dashboardDataFolder.Add(widgetData);
            i++;
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
            var rowSpan = 0;
            var columnSpan = 0;
            var widgetNumber = 1;
            if (dashboardDataFolder.GetNodesByType<WidgetData>() is var widgetDataCollection && widgetDataCollection.ToList().Count > 0)
            {
                rowSpan = widgetDataCollection.Sum(x => x.RowSpan - 1);
                columnSpan = widgetDataCollection.Sum(x => x.ColumnSpan - 1);
                widgetNumber = widgetDataCollection.Count();
            }
            var maxColumns = CalculateMaxColumnsFromWindow();
            if ((widgetNumber + columnSpan) < maxColumns)
            {
                maxColumns = widgetNumber + columnSpan;
            }
            var maxRows = ((widgetNumber + columnSpan) / maxColumns) + rowSpan;
            List<string> columnsLayout = [];
            for (int i = 0; i < maxColumns; i++)
            {
                columnsLayout.Add("1fr");
            }
            List<string> rowsLayout = [];
            for (int i = 0; i < maxRows; i++)
            {
                rowsLayout.Add("192");
            }
            for (int i = columnsLayout.Count; i < 4; i++)
            {
                columnsLayout.Add("0");
            }
            GenerateNewGridLayout(columnsLayout, rowsLayout);
        }
        catch (Exception ex)
        {
            Log.Error(LogicObject.BrowseName, ex.Message);
        }

    }

    private void RegenerateDashboard()
    {
        foreach (var widgetData in dashboardDataFolder.GetNodesByType<WidgetData>())
        {
            var widget = InformationModel.MakeObject(widgetData.WidgetBrowseName, widgetData.WidgetType);
            widget.GetVariable("ObjName").Value = widgetData.ObjName;
            widget.GetVariable("ObjEngUnit").Value = widgetData.ObjEngUnit;
            widget.GetVariable("ObjPointer").Value = widgetData.ObjPointer;
            switch (widget)
            {
                case DataGridUIObj:
                    widget.GetVariable("ObjDurations").Value = widgetData.ObjDurations;
                    widget.GetVariable("ObjQuery").Value = widgetData.ObjQuery;
                    break;
                case TrendUIObj:
                    widget.GetVariable("ObjDurations").Value = widgetData.ObjDurations;
                    widget.GetVariable("ObjParameters").Value = widgetData.GetVariable("ObjParameters").Value;
                    widget.GetVariable("ObjTextParameters").Value = widgetData.GetVariable("ObjTextParameters").Value;
                    widget.GetVariable("ObjColors").Value = widgetData.GetVariable("ObjColors").Value;
                    widget.GetVariable("ObjQuery").Value = widgetData.ObjQuery;
                    if (InformationModel.Get(widget.GetVariable("ObjPointer").Value) is DataLogger sourceDatalogger)
                    {
                        var trendNode = widget.Find<Trend>("TrendObj");
                        trendNode.Model = sourceDatalogger.NodeId;
                        int[] parametersArray = widget.GetVariable("ObjParameters").Value;
                        uint[] colorsArray = widget.GetVariable("ObjColors").Value;
                        string[] textParametersArray = widget.GetVariable("ObjTextParameters").Value;
                        int parametersArrayIndex = widget.GetVariable("IndexOfPensArray").Value;
                        int colorsArrayIndex = 0;
                        int textParametersArrayIndex = 0;
                        foreach (var variableToLog in sourceDatalogger.VariablesToLog)
                        {
                            var trendPen = CreateOrUpdateTrendPen(trendNode, variableToLog);
                            trendPen.Thickness = (float)parametersArray[parametersArrayIndex];
                            trendPen.Enabled = parametersArray[parametersArrayIndex + 1] != 0;
                            trendPen.Title = new LocalizedText(textParametersArray[textParametersArrayIndex], Session.ActualLocaleId);
                            trendPen.Color = new Color(colorsArray[colorsArrayIndex]);
                            parametersArrayIndex++;
                            colorsArrayIndex++;
                            textParametersArrayIndex++;
                        }
                    }
                    break;
                case SparklineUIObj:
                    widget.GetVariable("ObjDurations").Value = widgetData.ObjDurations;
                    widget.GetVariable("ObjParameters").Value = widgetData.GetVariable("ObjParameters").Value;
                    widget.GetVariable("ObjColors").Value = widgetData.GetVariable("ObjColors").Value;
                    break;
            }
            (widget as Item).HorizontalAlignment = HorizontalAlignment.Stretch;
            (widget as Item).VerticalAlignment = VerticalAlignment.Stretch;
            if (widget.GetByType<GridLayoutProperties>() is GridLayoutProperties layoutProperties)
            {
                layoutProperties.ColumnStart = widgetData.ColumnStart;
                layoutProperties.ColumnSpan = widgetData.ColumnSpan;
                layoutProperties.RowStart = widgetData.RowStart;
                layoutProperties.RowSpan = widgetData.RowSpan;
            }
            AddWidgetToGrid(widget);
        }
        memoryCountWidgets = GetTotalCountOfWidgets();
    }

    private static void RegenerateDataGridColums(IUANode dataGridWidget)
    {
        if (dataGridWidget.Find("DataGridObj") is DataGrid dataGridObj && dataGridWidget.GetVariable("ObjQuery") is IUAVariable queryVariable)
        {
            dataGridObj.Columns.Clear();
            string tableName = ExtractTableName(queryVariable.Value);
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
        var mainWindow = Session.FindByType<MainWindow>();
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
    private const string AddWidgetBrowseName = "AddWidget1";

    private float memoryCountWidgets;
    private IUAVariable configurationModeVariable;
    private GridLayout widgetGrid;
    private Folder dashboardDataFolder;
    private DateTime lastRun;
    private DelayedTask checkConfigurationModeTask;
}
