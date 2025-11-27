#region Using directives
using System;
using System.Collections.Generic;
using System.Text.Json;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.UI;
using FTOptix.HMIProject;
using FTOptix.NativeUI;
using FTOptix.Retentivity;
using FTOptix.CoreBase;
using FTOptix.Core;
using FTOptix.NetLogic;
using FTOptix.MQTTClient;
using FTOptix.DataLogger;
using System.Diagnostics;
using System.Linq;
using System.Globalization;
using FTOptix.OPCUAServer;
using FTOptix.MQTTBroker;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
#endregion

public class MQTTPublisherDataLogic : BaseNetLogic
{
    public override void Start()
    {
        configuration = (MQTTPublisherDataConfiguration)Owner;
        InitLogic(configuration.MQTTPublisherNode);       
    }

    public override void Stop()
    {
        ResetSamplings();
        fieldSynchronizer?.Dispose();
    }

    private void ResetSamplings()
    {
        CommonLogic.DisposeTask(watcher);
        try
        {
            UnsuscribeVariableChange(configuration.Data);
        }
        catch
        {
            // No subscription to remove
        }
    }

    [ExportMethod]
    public void InitLogic(NodeId mqttPublisher)
    {
        if (InformationModel.Get(mqttPublisher) is MQTTPublisher publisher)
        {
            configuration.MQTTPublisherNode = mqttPublisher;
            ResetSamplings();
            switch ((MQTTPublisherPayloadKind)configuration.PayloadKind)
            {
                case MQTTPublisherPayloadKind.Optix:
                    publisher.SamplingMode = (FTOptix.MQTTClient.SamplingMode)configuration.SamplingMode;
                    publisher.PFEnabled = false;
                    publisher.Folder = configuration.Data.NodeId;
                    break;
                case MQTTPublisherPayloadKind.Custom:
                    publisher.SamplingMode = FTOptix.MQTTClient.SamplingMode.VariableChange;
                    publisher.PFEnabled = true;
                    publisher.Folder = configuration.CustomPayload.NodeId;
                    fieldSynchronizer = new RemoteVariableSynchronizer(TimeSpan.FromMilliseconds(publisher.PollingPeriod));
                    InitFieldSynchronizer(configuration.Data);
                    InitPFHeaderForCustomPayload(ref publisher);
                    InitPFRecordForCustomPayload(ref publisher);
                    switch ((FTOptix.MQTTClient.SamplingMode)configuration.SamplingMode)
                    {
                        case FTOptix.MQTTClient.SamplingMode.Periodic:
                            watcher = new PeriodicTask(GeneratePayload, (int)publisher.SamplingPeriod, LogicObject);
                            watcher.Start();
                            break;
                        case FTOptix.MQTTClient.SamplingMode.VariableChange:
                            SubscribeVariableChange(configuration.Data);
                            break;
                    }
                    break;
            }
        }
    }

    private static void InitPFHeaderForCustomPayload(ref MQTTPublisher publisher)
    {
        publisher.PFHeader = new LocalizedText(Project.Current.Localization.Locales[0], "");
        var stringFormatter = InformationModel.Make<StringFormatter>("StringFormatter1");
        stringFormatter.Format = "#PFRecord";
        publisher.PFHeaderVariable.SetConverter(stringFormatter);
    }

    private static void InitPFRecordForCustomPayload(ref MQTTPublisher publisher)
    {
        publisher.PFRecord = new LocalizedText(Project.Current.Localization.Locales[0], "");
        var dynamicLink = InformationModel.MakeVariable<DynamicLink>("DynamicLink", OpcUa.DataTypes.String);
        dynamicLink.Value = "{Item}";
        publisher.PFRecordVariable.Refs.AddReference(FTOptix.CoreBase.ReferenceTypes.HasDynamicLink, dynamicLink);
    }

    private void InitFieldSynchronizer(IUANode nodeToAnalyze)
    {
        switch (nodeToAnalyze)
        {
            case IUAVariable variableNode:
                try
                {
                    fieldSynchronizer.Add(variableNode);
                }
                catch
                {
                    // Variable already added
                }
                break;
            case IUAObject objectNode:
                foreach (var child in objectNode.Children)
                {
                    InitFieldSynchronizer(child);
                }
                break;
        }
    }

    private void SubscribeVariableChange(IUANode nodeToAnalyze)
    {
        switch (nodeToAnalyze)
        {
            case IUAVariable uaVariable:
                uaVariable.VariableChange += OnVariableChange;
                break;
            case IUAObject uaObject:
                foreach (var child in uaObject.Children)
                {
                    SubscribeVariableChange(child);
                }
                break;
        }
    }

    private void UnsuscribeVariableChange(IUANode nodeToAnalyze)
    {
        switch (nodeToAnalyze)
        {
            case IUAVariable uaVariable:
                uaVariable.VariableChange -= OnVariableChange;
                break;
            case IUAObject uaObject:
                foreach (var child in uaObject.Children)
                {
                    UnsuscribeVariableChange(child);
                }
                break;
        }
    }

    private void OnVariableChange(object sender, VariableChangeEventArgs e)
    {
        GeneratePayload();
    }

    private void GeneratePayload()
    {
        if (configuration != null && configuration.CustomPayload != null)
        {
            var payloadData = GeneratePayloadData();
            configuration.CustomPayload.GetVariable("Payload").Value = payloadData;
        }
    }

    private string GeneratePayloadData()
    {
        var currentDateTime = DateTime.Now.ToString(defaultDateTimeFormat, CultureInfo.InvariantCulture);
        var currentDateTimeUTC = DateTime.UtcNow.ToString(defaultDateTimeFormat, CultureInfo.InvariantCulture);
        var payload = new Dictionary<string, object>();
        var payloadRoot = Owner.Get("PayloadStructure").Get<MQTTPayloadFieldInfo>("root");
        foreach (var child in payloadRoot.GetNodesByType<MQTTPayloadFieldInfo>())
        {
            PayloadEntryFromConfiguration(payload, child, (MQTTPublisherDataConfiguration)Owner, currentDateTime, currentDateTimeUTC);
        }
        if (payload.Count == 0)
        {
            return string.Empty;
        }
        return JsonSerializer.Serialize(payload, defaultSerializerOptions);
    }

    public static void PayloadEntryFromConfiguration(Dictionary<string, object> parent, MQTTPayloadFieldInfo configurationEntry, MQTTPublisherDataConfiguration dataConfiguration, string currentDateTime, string currentDateTimeUTC)
    {
        if (configurationEntry == null)
            return;

        switch ((MQTTPayloadFieldKind)configurationEntry.FieldKind)
        {
            case MQTTPayloadFieldKind.Field:
            case MQTTPayloadFieldKind.ArrayField:
                parent[configurationEntry.Key] = GetFieldValue(configurationEntry, dataConfiguration);
                break;
            case MQTTPayloadFieldKind.LocalTimestampField:
                parent[configurationEntry.Key] = currentDateTime;
                break;
            case MQTTPayloadFieldKind.UTCTimestampField:
                parent[configurationEntry.Key] = currentDateTimeUTC;
                break;
            case MQTTPayloadFieldKind.NestedObject:
                var nestedObject = new Dictionary<string, object>();
                foreach (var child in configurationEntry.GetNodesByType<MQTTPayloadFieldInfo>())
                {
                    PayloadEntryFromConfiguration(nestedObject, child, dataConfiguration, currentDateTime, currentDateTimeUTC);
                }
                parent[configurationEntry.Key] = nestedObject;
                break;
            case MQTTPayloadFieldKind.NestedObjectArrayElement:
                foreach (var child in configurationEntry.GetNodesByType<MQTTPayloadFieldInfo>())
                {
                    PayloadEntryFromConfiguration(parent, child, dataConfiguration, currentDateTime, currentDateTimeUTC);
                }
                break;
            case MQTTPayloadFieldKind.NestedObjectArray:
                var nestedArray = new List<Dictionary<string, object>>();
                foreach (var child in configurationEntry.GetNodesByType<MQTTPayloadFieldInfo>())
                {
                    var childObject = new Dictionary<string, object>();
                    PayloadEntryFromConfiguration(childObject, child, dataConfiguration, currentDateTime, currentDateTimeUTC);
                    nestedArray.Add(childObject);
                }
                parent[configurationEntry.Key] = nestedArray;
                break;
            case MQTTPayloadFieldKind.VariablesCollection:
                var variablesContainer = dataConfiguration.Data.GetObject(configurationEntry.ValueDataVariablePath);
                if (variablesContainer != null)
                {
                    foreach (var variable in variablesContainer.GetNodesByType<IUAVariable>())
                    {
                        parent[variable.BrowseName] = AnalyzeFieldValue(variable);
                    }
                }
                break;
        }
    }

    private static object GetFieldValue(MQTTPayloadFieldInfo configurationEntry, MQTTPublisherDataConfiguration dataConfiguration)
    {
        IUAVariable rawVariable = configurationEntry.ValueVariable;
        if (!string.IsNullOrEmpty(configurationEntry.ValueDataVariablePath) && dataConfiguration.Data.GetVariable(configurationEntry.ValueDataVariablePath) is IUAVariable valueNode)
        {
            rawVariable = valueNode;
        }
        return AnalyzeFieldValue(rawVariable);
    }

    private static object AnalyzeFieldValue(IUAVariable rawVariable)
    {
        if (rawVariable.DataType.Equals(OpcUa.DataTypes.Duration))
        {
            return TimeSpan.FromMicroseconds(rawVariable.Value);
        }
        else if (rawVariable.DataType.Equals(OpcUa.DataTypes.DateTime))
        {
            return ((DateTime)rawVariable.Value.Value).ToString(defaultDateTimeFormat, CultureInfo.InvariantCulture);
        }
        else
        {
            return rawVariable.Value.Value;
        }
    }

    private MQTTPublisherDataConfiguration configuration;
    private PeriodicTask watcher;
    private RemoteVariableSynchronizer fieldSynchronizer;
    public const string defaultDateTimeFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
    private readonly JsonSerializerOptions defaultSerializerOptions = new()
    {
        WriteIndented = false
    };
}

public enum MQTTPublisherPayloadKind
{
    Optix,
    Custom
}

public enum MQTTPayloadFieldKind
{
    Field = 0,
    ArrayField = 1,
    NestedObject = 2,
    VariablesCollection = 3,
    NestedObjectArray = 4,
    RootNode = 5,
    LocalTimestampField = 6,
    UTCTimestampField = 7,
    NestedObjectArrayElement = 8
}
