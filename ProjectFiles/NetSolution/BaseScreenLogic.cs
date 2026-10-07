#region Using directives
using System;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.SQLiteStore;
using FTOptix.UI;
using FTOptix.HMIProject;
using FTOptix.WebUI;
using FTOptix.NetLogic;
using FTOptix.CoreBase;
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
using FTOptix.MQTTClient;
using FTOptix.OPCUAServer;
using FTOptix.DataLogger;
using FTOptix.OPCUAClient;
using FTOptix.Core;
using System.Collections.Immutable;
using System.Collections.Generic;
#endregion

public class BaseScreenLogic : BaseNetLogic
{
    public override void Start()
    {
        initUITask = new LongRunningTask(InitUI, null, LogicObject);
        initUITask.Start();
    }

    public override void Stop()
    {
        CommonLogic.DisposeTask(initUITask);
        eventRegistration?.Dispose();
    }

    private void InitUI(BaseTaskWrapper task, object arguments)
    {
        if (Owner.GetVariable("RunInitUILogic").Value && InformationModel.Get(LogicObject.GetVariable("ContentStartingPoint").Value) is ColumnLayout contentStartingPoint)
        {
            IUANode sourceWidgetFolder = null;
            foreach (var children in contentStartingPoint.GetNodesByType<Item>())
            {
                switch (children)
                {
                    case CommDriverUIObj commDriverUIObj:
                        sourceWidgetFolder = InformationModel.Get(SourceWidgetFolderData.GetValueOrDefault("CommDriverUIObj"));
                        if (commDriverUIObj.GetAlias("CommDriverNode") is IUAObject communicationDriver)
                        {
                            CommonLogic.Instance.GenerateConfigurationWidgetFromSource(communicationDriver, commDriverUIObj, sourceWidgetFolder);
                        }
                        break;
                    case Accordion accordion:
                        sourceWidgetFolder = InformationModel.Get(SourceWidgetFolderData.GetValueOrDefault(accordion.BrowseName));
                        switch (accordion.BrowseName)
                        {
                            case "OPCUAServer":
                                affinityId = LogicObject.Context.AssignAffinityId();
                                if (accordion.Find("AddButton") is AddButton addButton && accordion.Get("Content/Content") is ColumnLayout contentWidgetContainer)
                                {
                                    eventRegistration = contentWidgetContainer.RegisterEventObserver(new AccordionWidgetObserver(addButton), EventType.ForwardReferenceChanged, affinityId);
                                }
                                CommonLogic.Instance.GenerateConfigurationWidgetFromSource(Project.Current.GetObject(CommonLogic.OPCUAServerFolderPath), accordion, sourceWidgetFolder);
                                break;
                            case "MQTTClient":
                                CommonLogic.Instance.GenerateConfigurationWidgetFromSource(Project.Current.GetObject("MQTT/MQTT Clients"), accordion, sourceWidgetFolder);
                                break;
                            case "Dataloggers":
                                CommonLogic.Instance.GenerateConfigurationWidgetFromSource(Project.Current.GetObject("Loggers"), accordion, sourceWidgetFolder);
                                break;
                            default:
                                break;
                        }
                        break;
                    default:
                        break;
                }
            }
        }
    }

    LongRunningTask initUITask;
    uint affinityId = 0;
    IEventRegistration eventRegistration;

    public readonly ImmutableDictionary<string, NodeId> SourceWidgetFolderData = ImmutableDictionary.CreateRange(
    [
        KeyValuePair.Create("CommDriverUIObj", Project.Current.Get("UI/UITypes/Widgets/CommDriverStationsUIObj").NodeId),
        KeyValuePair.Create("OPCUAServer", Project.Current.Get("UI/UITypes/Widgets/OPC UA Server").NodeId),
        KeyValuePair.Create("MQTTClient", Project.Current.Get("UI/UITypes/Widgets/MQTT/Client").NodeId),
        KeyValuePair.Create("Dataloggers", Project.Current.Get("UI/UITypes/Widgets/Datalogger").NodeId),
    ]);
}

public class AccordionWidgetObserver(AddButton addButton) : IReferenceObserver
{
    public void OnReferenceAdded(IUANode sourceNode, IUANode targetNode, NodeId referenceTypeId, ulong senderId)
    {
        if (targetNode is OPCUAServerStationUIObj)
        {
            _addButton.Enabled = false;
        }
    }

    public void OnReferenceRemoved(IUANode sourceNode, IUANode targetNode, NodeId referenceTypeId, ulong senderId)
    {
        if (targetNode is OPCUAServerStationUIObj)
        {
            _addButton.Enabled = true;
        }
    }

    private AddButton _addButton = addButton;
}