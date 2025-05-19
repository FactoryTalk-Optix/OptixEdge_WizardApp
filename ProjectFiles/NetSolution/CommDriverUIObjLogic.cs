#region Using directives
using System;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.UI;
using FTOptix.HMIProject;
using FTOptix.NetLogic;
using FTOptix.WebUI;
using FTOptix.DataLogger;
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
using FTOptix.Store;
using FTOptix.OPCUAClient;
using FTOptix.Core;
using FTOptix.SQLiteStore;
using FTOptix.MQTTBroker;
using FTOptix.NativeUI;
using FTOptix.AuditSigning;
using FTOptix.System;
using System.Linq;
using FTOptix.InfluxDBStoreRemote;
using FTOptix.Report;
#endregion

public class CommDriverUIObjLogic : BaseNetLogic
{
    public override void Start()
    {
        Accordion ownerAccordion = (Accordion) Owner.Owner;
        if (ownerAccordion.GetAlias("CommDriverNode") is IUAObject communicationDriver)
        {
            CommonLogic.Instance.GenerateConfigurationWidgetFromSource(communicationDriver, ownerAccordion, InformationModel.Get(LogicObject.GetVariable("StationWidgetFolder").Value));
        }
    }

    public override void Stop()
    {
        // nothing to do
    }
    
}


