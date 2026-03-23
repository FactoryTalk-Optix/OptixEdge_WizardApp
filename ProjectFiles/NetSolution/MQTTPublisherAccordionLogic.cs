#region Using directives
using System;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.UI;
using FTOptix.HMIProject;
using FTOptix.NetLogic;
using FTOptix.CoreBase;
using FTOptix.WebUI;
using FTOptix.SQLiteStore;
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
using FTOptix.MQTTClient;
using FTOptix.CommunicationDriver;
using FTOptix.OPCUAServer;
using FTOptix.DataLogger;
using FTOptix.OPCUAClient;
using FTOptix.Core;
using System.Collections.Generic;
#endregion

public class MQTTPublisherAccordionLogic : BaseNetLogic
{
    public override void Start()
    {
        configuration = Owner.Owner.GetAlias(CommonLogic.editAliasNameMapping.GetValueOrDefault(OptixEdge_WizardApp.ObjectTypes.MQTTPublisherUIObj)) as MQTTPublisherDataConfiguration;
        Owner.Get<NetLogicObject>("UIFieldParameterObserverLogic")?.ExecuteMethod("SubscribeObserver");
    }

    public override void Stop()
    {
        // Insert code to be executed when the user-defined logic is stopped
    }
    [ExportMethod]
    public void SavePayloadConfiguration()
    {
        configuration ??= Owner.Owner.GetAlias(CommonLogic.editAliasNameMapping.GetValueOrDefault(OptixEdge_WizardApp.ObjectTypes.MQTTPublisherUIObj)) as MQTTPublisherDataConfiguration;
        if (configuration.PayloadStructurePreview.Get<MQTTPayloadFieldInfo>("root") is MQTTPayloadFieldInfo rootNodeToApply)
        {
            if (configuration.PayloadStructure.Get<MQTTPayloadFieldInfo>("root") is MQTTPayloadFieldInfo rootNode)
            {
                rootNode.Delete();
            }
            rootNode = LogicObject.Context.NodeFactory.CloneNode(rootNodeToApply, configuration.NodeId.NamespaceIndex, NamingRuleType.None);
            configuration.PayloadStructure.Add(rootNode);
            rootNodeToApply.Children.Clear();;
            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Info, "Payload configuration applied successfully. Please save to finalize the changes.", durationOnScreen: 2000);
        }
    }

    [ExportMethod]
    public void RegerateUIWidget(NodeId verticalLayoutContainer)
    {
        if (InformationModel.Get<ColumnLayout>(verticalLayoutContainer) is ColumnLayout container && configuration != null &&            
            configuration.PayloadStructure.Get<MQTTPayloadFieldInfo>("root") is MQTTPayloadFieldInfo payloadRoot && (MQTTPublisherPayloadKind)configuration.PayloadKind == MQTTPublisherPayloadKind.Custom)
        {
            new LongRunningTask(GenerateUIFromPayload, new object[] { payloadRoot, container }, container).Start();
        }
    }

    private void GenerateUIFromPayload(LongRunningTask task, object arguments)
    {
        object[] args = (object[])arguments;
        var payloadRoot = (MQTTPayloadFieldInfo)args[0];
        var container = (ColumnLayout)args[1];
        // Call unsubscribe UI observer method for disable the save management on user edit
        Owner.Get<NetLogicObject>("UIFieldParameterObserverLogic")?.ExecuteMethod("UnsubscribeObserver");
        // Clear all previous UI items
        container.Children.Clear();
        uint fieldIndex = 1;
        // Regenerate UI from payload structure
        foreach (var child in payloadRoot.GetNodesByType<MQTTPayloadFieldInfo>())
        {
            MqttClientLogic.GeneratePayloadWidgetFromConfiguration(child, container, ref fieldIndex, Owner.Owner.GetVariable("CurrentSelectedField"));
        }
        Owner.Owner.GetVariable("LastIndexReleased").Value = fieldIndex;
        // Call subscribe UI observer method for enable the save management on user edit
        Owner.Get<NetLogicObject>("UIFieldParameterObserverLogic")?.ExecuteMethod("SubscribeObserver");
        task.Dispose();
    }

    MQTTPublisherDataConfiguration configuration;
}
