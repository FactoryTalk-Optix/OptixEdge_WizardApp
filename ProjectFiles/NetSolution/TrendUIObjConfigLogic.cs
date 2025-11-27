#region Using directives
using System;
using System.Collections.Generic;
using UAManagedCore;
using FTOptix.HMIProject;
using FTOptix.NetLogic;
using FTOptix.DataLogger;
using FTOptix.Core;
#endregion

public class TrendUIObjConfigLogic : BaseNetLogic
{
    public override void Start()
    {
        if (Owner.GetAlias("TrendUIWidgetData") is WidgetData trendWidgetData)
        {
            this.trendWidgetData = trendWidgetData;
            if (InformationModel.Get(trendWidgetData.SourceNode) is DataLogger loggerSource)
            {
                GeneratePensWidgetFromData(loggerSource);
            }
        }
    }

    public override void Stop()
    {
        // Insert code to be executed when the user-defined logic is stopped
    }

    [ExportMethod]
    public void GeneratePens(NodeId source)
    {
        if (InformationModel.Get(source) is DataLogger loggerSource)
        {
            GeneratePensWidgetFromData(loggerSource);
        }
    }

    private void GeneratePensWidgetFromData(DataLogger sourceNode)
    {
        int index = 0;
        var pensWidgetOwner = Owner.GetObject("Content/Pens/Content/Content");
        foreach (var variableToLog in sourceNode.VariablesToLog)
        {
            CheckPenData(variableToLog, index);

            var trendPenWidget = pensWidgetOwner.GetObject(variableToLog.BrowseName);
            if (trendPenWidget == null)
            {
                trendPenWidget = InformationModel.MakeObject(variableToLog.BrowseName, OptixEdge_WizardApp.ObjectTypes.TrendPenUIObjConfig);
                trendPenWidget.SetAlias("TrendWidgetData", trendWidgetData);
                trendPenWidget.GetVariable("PenIndex").Value = index;
                pensWidgetOwner.Add(trendPenWidget);
            }
            else
            {
                trendPenWidget.SetAlias("TrendWidgetData", trendWidgetData);
                trendPenWidget.GetVariable("PenIndex").Value = index;
            }
            index++;
        }
    }

    private Dictionary<string, int> GenerateActualPenList()
    {
        var penNames = new Dictionary<string, int>();
        int parametersArrayBaseOffset = trendWidgetData.IndexOfPensArray;
        uint counterEmpty = 0;
        for (int i = 0; i < trendWidgetData.ConfigurationTextParameters.Length; i++)
        {
            var penName = trendWidgetData.ConfigurationTextParameters[i].ToString(); // Variable BrowseName;
            if (!string.IsNullOrEmpty(penName))
            {
                penNames.Add(penName, i);
                counterEmpty = 0;
            }
            else
            {
                counterEmpty++;
            }
            if (counterEmpty >= 10)
            {
                break;
            }
        }
        return penNames;
    }

    private void CheckPenData(IUAVariable sourceVariableToLog, int index)
    {
        int penOffset = index * 2;
        int parametersArrayBaseOffset = trendWidgetData.IndexOfPensArray;
        // Get existing arrays
        int[] configurationParameters = trendWidgetData.GetVariable("ConfigurationParameters").Value;
        uint[] configurationColors = trendWidgetData.GetVariable("ConfigurationColors").Value;
        string[] configurationTextParameters = trendWidgetData.ConfigurationTextParameters;
        // check if some parameter is missing and set default values
        if (string.IsNullOrEmpty(configurationTextParameters[penOffset]))
        {
            configurationTextParameters[penOffset] = sourceVariableToLog.BrowseName; // Variable BrowseName
            configurationParameters[penOffset + parametersArrayBaseOffset] = -1; // Thickness
            configurationParameters[penOffset + parametersArrayBaseOffset + 1] = 1; // Enabled
        }
        if (string.IsNullOrEmpty(configurationTextParameters[penOffset + 1]))
        {
            configurationTextParameters[penOffset + 1] = sourceVariableToLog.BrowseName; // Title
        }
        configurationColors[index] = configurationColors[index] != 0 ? configurationColors[index] : new Color(255, (byte)new Random().Next(255), (byte)new Random().Next(255), (byte)new Random().Next(255)).ARGB; // Color
        // Write back the modified arrays
        trendWidgetData.GetVariable("ConfigurationParameters").Value = configurationParameters;
        trendWidgetData.GetVariable("ConfigurationColors").Value = configurationColors;
        trendWidgetData.ConfigurationTextParameters = configurationTextParameters;
    }

    private WidgetData trendWidgetData;
}
