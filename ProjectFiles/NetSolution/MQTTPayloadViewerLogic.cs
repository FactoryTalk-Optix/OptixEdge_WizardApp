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
using FTOptix.CommunicationDriver;
using FTOptix.MQTTClient;
using FTOptix.OPCUAServer;
using FTOptix.DataLogger;
using FTOptix.OPCUAClient;
using FTOptix.Core;
using System.Collections.Generic;
using System.Text.Json;
#endregion

public class MQTTPayloadViewerLogic : BaseNetLogic
{
    public override void Start()
    {
        if (Owner.GetAlias("MQTTPublisherDataConfiguration") is MQTTPublisherDataConfiguration publisherConfig)
        {
            generatePayloadTask = new LongRunningTask(GeneratePayload, publisherConfig, LogicObject);
            generatePayloadTask.Start();
        }
        else
        {
            Log.Error(LogicObject.BrowseName, "MQTTPublisherDataConfiguration not found");
        }
    }

    public override void Stop()
    {
        CommonLogic.DisposeTask(generatePayloadTask);
    }

    [ExportMethod]
    public void RegeneratePayload()
    {
        generatePayloadTask?.Start();
    }

    private void GeneratePayload(LongRunningTask task, object argument)
    {
        var publisherConfig = (MQTTPublisherDataConfiguration)argument;
        // Get the payload recap and show it in the label
        var payload = new Dictionary<string, object>();
        foreach (var children in publisherConfig.PayloadStructure.Get("root").GetNodesByType<MQTTPayloadFieldInfo>())
        {
            MQTTPublisherDataLogic.PayloadEntryFromConfiguration(payload, children, publisherConfig, DateTime.Now.ToString(MQTTPublisherDataLogic.defaultDateTimeFormat), DateTime.UtcNow.ToString(MQTTPublisherDataLogic.defaultDateTimeFormat));
        }
        // Print the payload in the label
        if (payload.Count > 0 && Owner.Find<EditableLabel>("Payload") is EditableLabel payloadLabel)
        {
            payloadLabel.Text = JsonSerializer.Serialize(payload, defaultSerializerOptions);
        }
        task.Dispose();
    }

    LongRunningTask generatePayloadTask;

    private readonly JsonSerializerOptions defaultSerializerOptions = new()
    {
        WriteIndented = true
    };
}
