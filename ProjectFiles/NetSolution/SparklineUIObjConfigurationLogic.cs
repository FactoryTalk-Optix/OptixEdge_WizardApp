#region Using directives
using System;
using UAManagedCore;
using FTOptix.NetLogic;
using FTOptix.Core;
#endregion

public class SparklineUIObjConfigurationLogic : BaseNetLogic
{
    public override void Start()
    {
        // Get references to configuration variables from the logic object
        lineColorVariable = LogicObject.GetVariable("LineColor");
        rangeColorVariable = LogicObject.GetVariable("RangeColor");
        thicknessVariable = LogicObject.GetVariable("Thickness");        
        // Try to get the SparklineWidgetData alias and cast it to WidgetData type
        if (Owner.GetAlias("SparklineUIWidgetData") is WidgetData widgetData)
        {
            // Store the widget data reference and load initial configuration
            sparklineWidgetData = widgetData;
            GetSparklineConfiguration();            
            CheckSparklineData();
            // Subscribe to variable change events to update widget data when modified
            lineColorVariable.VariableChange += UpdateData;
            rangeColorVariable.VariableChange += UpdateData;
            thicknessVariable.VariableChange += UpdateData;
        }
    }

    public override void Stop()
    {
        // Check if the widget data was successfully initialized
        if (sparklineWidgetData != null)
        {
            // Unsubscribe from variable change events to prevent memory leaks
            lineColorVariable.VariableChange -= UpdateData;
            rangeColorVariable.VariableChange -= UpdateData;
            thicknessVariable.VariableChange -= UpdateData;
        }
    }

    /// <summary>
    /// Loads the sparkline configuration from the widget data and populates the UI variables.
    /// Retrieves line color, range color, and thickness settings from the stored configuration arrays.
    /// </summary>
    private void GetSparklineConfiguration()
    {
        // Get the base offset and retrieve configuration arrays
        int parametersArrayBaseOffset = sparklineWidgetData.IndexOfPensArray;
        int[] configurationParameters = sparklineWidgetData.GetVariable("ConfigurationParameters").Value;
        uint[] configurationColors = sparklineWidgetData.GetVariable("ConfigurationColors").Value;        
        // Populate UI variables with values from configuration arrays
        thicknessVariable.Value = configurationParameters[parametersArrayBaseOffset];
        lineColorVariable.Value = new Color(configurationColors[0]);
        rangeColorVariable.Value = new Color(configurationColors[1]);
    }

    /// <summary>
    /// Event handler triggered when any of the sparkline configuration variables change.
    /// Updates the widget data arrays with the new values from the UI variables.
    /// </summary>
    /// <param name="sender">The variable that triggered the change event</param>
    /// <param name="e">Event arguments containing change details</param>
    private void UpdateData(object sender, VariableChangeEventArgs e)
    {
        // Get the base offset and retrieve current configuration arrays
        int parametersArrayBaseOffset = sparklineWidgetData.IndexOfPensArray;
        int[] configurationParameters = sparklineWidgetData.GetVariable("ConfigurationParameters").Value;
        uint[] configurationColors = sparklineWidgetData.GetVariable("ConfigurationColors").Value;        
        // Update arrays with new values from UI variables
        configurationParameters[parametersArrayBaseOffset] = thicknessVariable.Value;
        configurationColors[0] = lineColorVariable.Value;
        configurationColors[1] = rangeColorVariable.Value;        
        // Persist modified arrays back to widget data
        sparklineWidgetData.GetVariable("ConfigurationParameters").Value = configurationParameters;
        sparklineWidgetData.GetVariable("ConfigurationColors").Value = configurationColors;
    }

    private void CheckSparklineData()
    {
        // Set default thickness if zero
        if ((float)thicknessVariable.Value == 0)
        {
            thicknessVariable.Value = -1;
        }
        // Set default line color to black if zero
        if ((uint)lineColorVariable.Value == 0)
        {
            lineColorVariable.Value = System.Drawing.Color.Black.ToArgb();
        }
        // Set default range color to yellow if zero
        if ((uint)rangeColorVariable.Value == 0)
        {
            rangeColorVariable.Value = System.Drawing.Color.Yellow.ToArgb();
        }
        // Save data in the configuration
        UpdateData(null,null);
    }

    #region Private Fields
    /// <summary>
    /// Reference to the widget data object containing the sparkline configuration arrays
    /// </summary>
    WidgetData sparklineWidgetData;
    
    /// <summary>
    /// Variable representing the line color of the sparkline chart
    /// </summary>
    IUAVariable lineColorVariable;
    
    /// <summary>
    /// Variable representing the range color (background/fill) of the sparkline chart
    /// </summary>
    IUAVariable rangeColorVariable;
    
    /// <summary>
    /// Variable representing the thickness (line width) of the sparkline chart
    /// </summary>
    IUAVariable thicknessVariable;
    #endregion
}
