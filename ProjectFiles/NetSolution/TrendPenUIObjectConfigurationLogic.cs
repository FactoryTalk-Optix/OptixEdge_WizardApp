#region Using directives
using UAManagedCore;
using FTOptix.NetLogic;
using FTOptix.Core;
#endregion

public class TrendPenUIObjectConfigurationLogic : BaseNetLogic
{
    public override void Start()
    {
        titleVariable = LogicObject.GetVariable("Title");
        colorVariable = LogicObject.GetVariable("Color");
        penIndex = Owner.GetVariable("PenIndex").Value;
        thicknessVariable = LogicObject.GetVariable("Thickness");
        penBrowseNameVariable = LogicObject.GetVariable("PenBrowseName");
        enabledVariable = LogicObject.GetVariable("Enabled");
        if (Owner.GetAlias("TrendWidgetData") is WidgetData widgetData)
        {
            this.trendWidgetData = widgetData;
            GetPenData();
            titleVariable.VariableChange += UpdateData;
            colorVariable.VariableChange += UpdateData;
            thicknessVariable.VariableChange += UpdateData;
            penBrowseNameVariable.VariableChange += UpdateData;
            enabledVariable.VariableChange += UpdateData;
        }
    }

    public override void Stop()
    {
        titleVariable.VariableChange -= UpdateData;
        colorVariable.VariableChange -= UpdateData;
        thicknessVariable.VariableChange -= UpdateData;
        penBrowseNameVariable.VariableChange -= UpdateData;
        enabledVariable.VariableChange -= UpdateData;
    }
    

    private void GetPenData()
    {
        int penOffset = penIndex * 2;
        int parametersArrayBaseOffset = trendWidgetData.IndexOfPensArray;
        int[] configurationParameters = trendWidgetData.GetVariable("ConfigurationParameters").Value;
        uint[] configurationColors = trendWidgetData.GetVariable("ConfigurationColors").Value;
        thicknessVariable.Value = configurationParameters[penOffset + parametersArrayBaseOffset];//Thickness
        enabledVariable.Value = configurationParameters[penOffset + parametersArrayBaseOffset + 1]; //Enabled
        penBrowseNameVariable.Value = new LocalizedText(trendWidgetData.ConfigurationTextParameters[penOffset], Session.ActualLocaleId); // Variable BrowseName
        titleVariable.Value = new LocalizedText(trendWidgetData.ConfigurationTextParameters[penOffset + 1], Session.ActualLocaleId); //Title
        colorVariable.Value = new Color(configurationColors[penIndex]); //Color
    }

    private void UpdateData(object sender, VariableChangeEventArgs e)
    {
        int penOffset = penIndex * 2;
        int parametersArrayBaseOffset = trendWidgetData.IndexOfPensArray;
        // Get existing arrays
        int[] configurationParameters = trendWidgetData.GetVariable("ConfigurationParameters").Value;
        uint[] configurationColors = trendWidgetData.GetVariable("ConfigurationColors").Value;
        string[] configurationTextParameters = trendWidgetData.ConfigurationTextParameters;
        // Modify arrays with new pen data
        configurationParameters[penOffset + parametersArrayBaseOffset] = thicknessVariable.Value; //Thickness
        configurationParameters[penOffset + parametersArrayBaseOffset + 1] = enabledVariable.Value; //Enabled
        configurationTextParameters[penOffset] = ((LocalizedText)penBrowseNameVariable.Value).Text; // Variable BrowseName
        configurationTextParameters[penOffset + 1] = ((LocalizedText)titleVariable.Value).Text; //Title
        configurationColors[penIndex] = colorVariable.Value; //Color
        // Write back the modified arrays
        trendWidgetData.GetVariable("ConfigurationParameters").Value = configurationParameters;
        trendWidgetData.GetVariable("ConfigurationColors").Value = configurationColors;
        trendWidgetData.ConfigurationTextParameters = configurationTextParameters;
    }

    WidgetData trendWidgetData;
    IUAVariable titleVariable;
    IUAVariable colorVariable;
    int penIndex;
    IUAVariable thicknessVariable;
    IUAVariable penBrowseNameVariable;
    IUAVariable enabledVariable;
}
