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
using FTOptix.DataLogger;
using FTOptix.Core;
using FTOptix.OPCUAServer;
using FTOptix.MQTTBroker;
using FTOptix.AuditSigning;
using FTOptix.NativeUI;
#endregion

public class DataloggersAccordionLogic : BaseNetLogic
{
    public override void Start()
    {
        Accordion ownerAccordion = (Accordion)Owner.Owner;
        CommonLogic.Instance.GenerateConfigurationWidgetFromSource(Project.Current.GetObject("Loggers"), ownerAccordion, InformationModel.Get(LogicObject.GetVariable("StationWidgetFolder").Value));
    }

    public override void Stop()
    {
        // Insert code to be executed when the user-defined logic is stopped
    }
}
