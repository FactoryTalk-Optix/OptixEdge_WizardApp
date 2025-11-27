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
using System.Linq;
using System.Text.Json;
#endregion

public class PayloadRecapLogic : BaseNetLogic
{
    const string tableName = "PayloadReview";
    public override void Start()
    {
        if (Owner.GetAlias("MainDialog") is MQTTCustomPayloadWizard mainDialog && mainDialog.GetAlias("MQTTPublisherDataConfiguration") is MQTTPublisherDataConfiguration publisherConfig && InformationModel.Get<Store>(LogicObject.GetVariable("DatabaseTable").Value) is Store databaseTable)
        {
            object[] argumentArray = [publisherConfig, databaseTable];
            var task = new LongRunningTask(AnalyzePayloadModel, argumentArray, LogicObject);
            task.Start();
        }
        else
        {
            Log.Error("PayloadRecapLogic", "MQTTPublisherDataConfiguration not found");
        }
    }

    public override void Stop()
    {
        // Insert code to be executed when the user-defined logic is stopped
    }

    private void AnalyzePayloadModel(LongRunningTask task, object argument)
    {
        object[] argumentArray = (object[])argument;
        MQTTPublisherDataConfiguration publisherConfig = (MQTTPublisherDataConfiguration)argumentArray[0];
        Store databaseTable = (Store)argumentArray[1];
        int counterNested = 0;
        int counterArrayFields = 0;
        int counterTimestampFields = 0;
        int counterFields = 0;
        int counterArrayNested = 0;
        foreach (var children in publisherConfig.PayloadStructurePreview.Get("root").GetNodesByType<MQTTPayloadFieldInfo>())
        {
            CountingElements(children, ref counterNested, ref counterArrayFields, ref counterTimestampFields, ref counterFields, ref counterArrayNested);
        }
        string[] columnsName = ["ItemType", "Quantity"];
        string[] valuesType = ["Total Fields", "Total Array Fields", "Total Timestamp Fields", "Total Nested Objects", "Total Array Nested Objects"];
        object[,] rowToInsert = new object[5, 2]
        {
                { valuesType[0], counterFields },
                { valuesType[1], counterArrayFields },
                { valuesType[2], counterTimestampFields },
                { valuesType[3], counterNested },
                { valuesType[4], counterArrayNested }
        };
        databaseTable.Query($"DELETE FROM {tableName}", out _, out _);
        databaseTable.Insert(tableName, columnsName, rowToInsert);
        // Get the payload recap and show it in the label
        var payload = new Dictionary<string, object>();
        foreach (var children in publisherConfig.PayloadStructurePreview.Get("root").GetNodesByType<MQTTPayloadFieldInfo>())
        {
            MQTTPublisherDataLogic.PayloadEntryFromConfiguration(payload, children, publisherConfig, DateTime.Now.ToString(MQTTPublisherDataLogic.defaultDateTimeFormat), DateTime.UtcNow.ToString(MQTTPublisherDataLogic.defaultDateTimeFormat));
        }
        // Print the payload in the label
        if (payload.Count > 0 && Owner.Find<Label>("Payload") is Label payloadLabel)
        {
            payloadLabel.Text = JsonSerializer.Serialize(payload, defaultSerializerOptions);
        }
        task.Dispose();
    }

    private static void CountingElements(MQTTPayloadFieldInfo nodeToAnalyze, ref int counterNested, ref int counterArrayFields, ref int counterTimestampFields, ref int counterFields, ref int counterArrayNested)
    {
        switch ((MQTTPayloadFieldKind)nodeToAnalyze.FieldKind)
        {
            case MQTTPayloadFieldKind.Field:
                counterFields++;
                break;
            case MQTTPayloadFieldKind.ArrayField:
                counterArrayFields++;
                break;
            case MQTTPayloadFieldKind.LocalTimestampField:
            case MQTTPayloadFieldKind.UTCTimestampField:
                counterTimestampFields++;
                break;
            case MQTTPayloadFieldKind.NestedObject:
                counterNested++;
                foreach (var children in nodeToAnalyze.GetNodesByType<MQTTPayloadFieldInfo>())
                {
                    CountingElements(children, ref counterNested, ref counterArrayFields, ref counterTimestampFields, ref counterFields, ref counterArrayNested);
                }
                break;
            case MQTTPayloadFieldKind.NestedObjectArray:
                counterArrayNested++;
                foreach (var children in nodeToAnalyze.GetNodesByType<MQTTPayloadFieldInfo>())
                {
                    CountingElements(children, ref counterNested, ref counterArrayFields, ref counterTimestampFields, ref counterFields, ref counterArrayNested);
                }
                break;
        }
    }
    
    private readonly JsonSerializerOptions defaultSerializerOptions = new()
    {
        WriteIndented = true
    };
}
