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
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Linq;
using FTOptix.NativeUI;
#endregion

public class RetentivityHandlerLogic : BaseNetLogic
{
    public override void Start()
    {
        bodyPanelLoader = Owner.Get<PanelLoader>("MainBody/Body/Body");
        if (bodyPanelLoader != null)
        {
            bodyPanelLoader.PanelVariable.VariableChange += bodyPanelLoader_PanelVariable_VariableChange;
        }
        else
        {
            Log.Warning(LogicObject.BrowseName, "Missing body panel loader!");
        }
    }

    public override void Stop()
    {
        if (bodyPanelLoader != null)
        {
            bodyPanelLoader.PanelVariable.VariableChange -= bodyPanelLoader_PanelVariable_VariableChange;
        }
    }

    private void bodyPanelLoader_PanelVariable_VariableChange(object sender, VariableChangeEventArgs e)
    {
        NodeId newScreenNodeId = (NodeId)e.NewValue;
        SetDeltaObserverToRetentivity(newScreenNodeId);
    }

    private void SetDeltaObserverToRetentivity(NodeId activeScreenTypeNodeId)
    {
        if (InformationModel.Get(activeScreenTypeNodeId) is ScreenType activeScreenType)
        {
            var retentivityBrowseName = ScreenRetentivityMapping.GetValueOrDefault(activeScreenTypeNodeId, "");
            foreach (var retenitvityNode in Project.Current.Get("Retentivity").GetNodesByType<RetentivityStorage>().Where(x=> !retentivityAlwaysEnabled.Contains(x.BrowseName)))
            {
                retenitvityNode.DeltaObserverEnabled = retenitvityNode.BrowseName == retentivityBrowseName;
            }
        }
    }

    private PanelLoader bodyPanelLoader;
    private static readonly ImmutableDictionary<NodeId, string> ScreenRetentivityMapping = ImmutableDictionary.CreateRange(
    [
        KeyValuePair.Create(OptixEdge_WizardApp.ObjectTypes.DataSourcesConfig, "DataSourcesStorage"),
        KeyValuePair.Create(OptixEdge_WizardApp.ObjectTypes.DataDestinationsConfig, "DataDestinationsStorage"),
    ]);
    private static readonly ImmutableList<string> retentivityAlwaysEnabled = ["UIStorage", "SecurityRetentivityStorage", "AlarmsRetentivityStorage",];
}

