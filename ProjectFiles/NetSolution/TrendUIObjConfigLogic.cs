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
using FTOptix.OPCUAServer;
using FTOptix.DataLogger;
using FTOptix.MQTTClient;
using FTOptix.Core;
using System.Linq;
using System.IO.Compression;
using System.Collections.Generic;
using System.Threading;
using FTOptix.NativeUI;
#endregion

public class TrendUIObjConfigLogic : BaseNetLogic
{
    public override void Start()
    {
        if (Owner.GetAlias("TrendUIObjAlias") is TrendUIObj trendUIObject)
        {
            trendUIObjectAliasNode = trendUIObject;
            if (InformationModel.Get(trendUIObjectAliasNode.GetVariable("ObjPointer")?.Value) is DataLogger loggerSource)
            {
                GeneratePensFromSource(loggerSource);
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
            GeneratePensFromSource(loggerSource);
        }
    }

    private void GeneratePensFromSource(DataLogger sourceNode)
    {
        if (Owner.GetAlias("TrendUIObjAlias") is TrendUIObj trendUIObject)
        {
            trendUIObjectAliasNode = trendUIObject;
        }
        var trendObject = trendUIObjectAliasNode.Find<Trend>("TrendObj");
        var pensWidgetOwner = Owner.GetObject("Content/Pens/Content/Content");
        if (trendObject == null)
        {
            Log.Error(LogicObject.BrowseName, "Cannote find trendObj! Fatal error");
            return;
        }
        List<string> pensList = trendObject.Pens.Select(x => x.BrowseName).ToList();       
        foreach (var variableToLog in sourceNode.VariablesToLog)
        {
            var trendPen = DashboardLogic.Instance.CreateOrUpdateTrendPen(trendObject, variableToLog);
            var trendPenWidget = pensWidgetOwner.GetObject(variableToLog.BrowseName);
            if (trendPenWidget == null)
            {
                trendPenWidget = InformationModel.MakeObject(variableToLog.BrowseName, Optix_DefaultApplication_OptixEdge.ObjectTypes.TrendPenUIObjConfig);
                trendPenWidget.SetAlias("TrendPenUIObjConfigAlias", trendPen);
                pensWidgetOwner.Add(trendPenWidget);
            }
            else
            {
                trendPenWidget.SetAlias("TrendPenUIObjConfigAlias", trendPen);
            }
            pensList.Remove(variableToLog.BrowseName);
        }
        foreach (var penToDelete in pensList)
        {
            DeletePenAndWidget(trendObject, pensWidgetOwner, penToDelete);
        }
    }

    private static void DeletePenAndWidget(Trend trendObj, IUAObject pensWidgetOwner, string penToDelete)
    {
        trendObj.Pens.Remove(penToDelete);
        if (pensWidgetOwner.Get(penToDelete) is TrendPen penNode)
        {
            pensWidgetOwner.Remove(penNode);
        }
    }

    private IUAObject trendUIObjectAliasNode;
}
