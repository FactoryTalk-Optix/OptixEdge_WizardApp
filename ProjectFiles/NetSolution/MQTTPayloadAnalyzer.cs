#region Using directives
using System;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.MQTTClient;
using FTOptix.UI;
using FTOptix.HMIProject;
using FTOptix.NetLogic;
using FTOptix.NativeUI;
using FTOptix.Retentivity;
using FTOptix.CoreBase;
using FTOptix.DataLogger;
using FTOptix.Core;
using System.Text.Json;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Globalization;
using FTOptix.OPCUAServer;
using System.Linq;
#endregion

public class MQTTPayloadAnalyzer : BaseNetLogic
{
    // Patterns to recognize various date/time formats
    private static readonly Regex IsoDateTimePattern = new Regex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,3})?Z?$");
    private static readonly Regex DateOnlyPattern = new Regex(@"^\d{4}-\d{2}-\d{2}$");
    private static readonly Regex TimeOnlyPattern = new Regex(@"^\d{2}:\d{2}:\d{2}(\.\d{1,3})?$");
    private static readonly Regex EpochPattern = new Regex(@"^\d{10}$|^\d{13}$"); // Unix timestamp (seconds or milliseconds)

    public override void Start()
    {
        // Insert code to be executed when the user-defined logic is started
    }

    public override void Stop()
    {
        // Insert code to be executed when the user-defined logic is stopped
    }

    [ExportMethod]
    public void ApplyPayload(NodeId dataConfiguration)
    {
        if (InformationModel.Get(dataConfiguration) is MQTTPublisherDataConfiguration configuration && configuration.PayloadStructurePreview.Get("root") is MQTTPayloadFieldInfo rootNode)
        {
            // Clear previous structure
            configuration.PayloadStructure.Children.Clear();
            // Clone the preview structure to the actual structure
            var clonedRoot = LogicObject.Context.NodeFactory.CloneNode(rootNode, rootNode.NodeId.NamespaceIndex, NamingRuleType.None);
            configuration.PayloadStructure.Add(clonedRoot);
            Log.Info(LogicObject.BrowseName, "Applied payload structure from preview");            
        }
        else
        {
            Log.Warning(LogicObject.BrowseName, "No valid payload structure found in preview to apply");
            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Warning, "No valid payload structure found in preview to apply");
        }
    }

    [ExportMethod]
    public void AnalyzePayload(string payload, NodeId dataConfiguration, bool isCustomPayload = false)
    {
        try
        {
            if (InformationModel.Get(dataConfiguration) is MQTTPublisherDataConfiguration configuration)
            {
                LogicObject.GetOrCreateVariable("Complete").Value = false;
                using JsonDocument document = JsonDocument.Parse(payload);
                // Clean previous analysis
                configuration.PayloadStructurePreview.Children.Clear();
                Log.Debug(LogicObject.BrowseName, "Starting payload analysis...");
                AnalyzeElement(document.RootElement, "root", 0, configuration.PayloadStructurePreview, true);
                LogicObject.GetVariable("Complete").Value = true;
                if (isCustomPayload)
                {
                    NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Success, "Payload analysis complete");
                }
                Log.Debug(LogicObject.BrowseName, "Analysis complete");
            }
        }
        catch (Exception ex)
        {
            Log.Error(LogicObject.BrowseName, $"Failed to parse JSON payload {ex.Message}");
            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Warning, "Failed to parse JSON payload");
            LogicObject.GetVariable("Complete").Value = false;
        }
    }

    [ExportMethod]
    public void GenerateAzurePayload(NodeId dataConfiguration)
    {
        var azurePayload = @"{
        ""deviceId"": ""PLC001"",
        ""time"": ""2025-08-26T09:45:00Z"",
        ""timestamp"": ""2025-08-26T09:45:00Z"",
        ""variables"": {
            ""motorStatus"": true,
            ""alarmActive"": false,
            ""temperature"": 72.5
        }
        }";
        AnalyzePayload(azurePayload, dataConfiguration);
    }

    [ExportMethod]
    public void GenerateAWSPayload(NodeId dataConfiguration)
    {
        var awsPayload = @"{
    ""deviceId"": ""PLC_01"",
    ""firmwareVersion"": ""1.2.0"",
    ""machineType"": ""OptixEdge"",
    ""timestamp"": ""2025-09-01T11:10:00Z"",
    ""temperature"": 72.5,
    ""pressure"": 1.02,
    ""status"": ""OK"",
    ""alarm"": false
            }";
        AnalyzePayload(awsPayload, dataConfiguration);
    }

    [ExportMethod]
    public void GenerateGooglePayload(NodeId dataConfiguration)
    {
        var googlePayload = @"{
        ""deviceId"": ""PLC_01"",
        ""timestamp"": ""2025-09-01T11:10:00Z"",
        ""firmwareVersion"": ""1.2.0"",
        ""machineType"": ""OptixEdge"",
        ""metrics"": {
            ""temperature"": 72.5,
            ""pressure"": 1.02,
            ""status"": ""OK"",
            ""alarm"": false
        },
        ""location"": {
            ""lat"": 45.7001,
            ""lng"": 9.2005
        }
        }";
        AnalyzePayload(googlePayload, dataConfiguration);
    }

    [ExportMethod]
    public void GenerateHiveMQPayload(NodeId dataConfiguration)
    {
        var hivemqPayload = @"{
        ""deviceId"": ""DEVICE_001"",
        ""timestamp"": ""2025-09-01T11:10:00Z"",
        ""metrics"": {
            ""temperature"": 72.5,
            ""pressure"": 1.02,
            ""humidity"": 45.0,
            ""status"": ""OK"",
            ""alarm"": false
        },
        ""metadata"": {
            ""firmwareVersion"": ""1.0.0"",
            ""machineType"": ""GenericDevice"",
            ""location"": {
            ""lat"": 45.7001,
            ""lng"": 9.2005
            }
        }
        }";
        AnalyzePayload(hivemqPayload, dataConfiguration);
    }

    private void AnalyzeElement(JsonElement element, string name, int depth, IUAObject container, bool isRootNode = false, bool isArrayElement = false)
    {
        var indent = new string(' ', depth * 2);

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                Log.Debug(LogicObject.BrowseName, $"{indent}{name}: Object");
                var newContainer = InformationModel.MakeObject<MQTTPayloadFieldInfo>(name);
                newContainer.FieldKind = isRootNode ? MQTTPayloadFieldKind.RootNode : MQTTPayloadFieldKind.NestedObject;
                if (isArrayElement)
                {
                    newContainer.FieldKind = MQTTPayloadFieldKind.NestedObjectArrayElement;
                }
                newContainer.Key = name;
                container.Add(newContainer);
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    AnalyzeElement(property.Value, property.Name, depth + 1, newContainer);
                }
                break;

            case JsonValueKind.Array:
                Log.Debug(LogicObject.BrowseName, $"{indent}{name}: Array [{element.GetArrayLength()} elements]");
                int index = 0;
                newContainer = InformationModel.MakeObject<MQTTPayloadFieldInfo>(name);
                switch (element.EnumerateArray().FirstOrDefault().ValueKind)
                {
                    case JsonValueKind.Object:
                        newContainer.FieldKind = MQTTPayloadFieldKind.NestedObjectArray; // Nested object array
                        break;
                    case JsonValueKind.String:
                    case JsonValueKind.Number:
                    case JsonValueKind.True:
                    case JsonValueKind.False:
                        newContainer.FieldKind = MQTTPayloadFieldKind.ArrayField; // Array field
                        break;
                }
                newContainer.Key = name;
                container.Add(newContainer);
                foreach (JsonElement item in element.EnumerateArray())
                {
                    AnalyzeElement(item, $"[{index}]", depth + 1, newContainer, isArrayElement: true);
                    index++;
                    if (index > 2) // Limit output for large arrays
                    {
                        Log.Debug(LogicObject.BrowseName, $"{new string(' ', (depth + 1) * 2)}... other {element.GetArrayLength() - index} elements");
                        break;
                    }
                }
                break;

            case JsonValueKind.String:
                var fieldInfo = AnalyzeStringValue(element.GetString(), name, indent);
                container.Add(fieldInfo);
                break;

            case JsonValueKind.Number:
                var numericField = AnalyzeNumberValue(element, name, indent);
                container.Add(numericField);
                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
                MQTTPayloadFieldInfo variableInfo = InformationModel.MakeObject<MQTTPayloadFieldInfo>(name);
                variableInfo.FieldKind = MQTTPayloadFieldKind.Field;
                variableInfo.Key = name;
                variableInfo.ValueVariable.DataType = OpcUa.DataTypes.Boolean;
                variableInfo.ValueVariable.ActualDataType = OpcUa.DataTypes.Boolean;
                variableInfo.Value = element.GetBoolean();
                Log.Debug(LogicObject.BrowseName, $"{indent}{name}: Boolean = {element.GetBoolean()}");
                container.Add(variableInfo);
                break;

            case JsonValueKind.Null:
                Log.Debug(LogicObject.BrowseName, $"{indent}{name}: Null");
                break;

            default:
                Log.Debug(LogicObject.BrowseName, $"{indent}{name}: Unknown type");
                break;
        }
    }

    private MQTTPayloadFieldInfo AnalyzeStringValue(string value, string name, string indent)
    {
        MQTTPayloadFieldInfo variableInfo = InformationModel.MakeObject<MQTTPayloadFieldInfo>(name);
        variableInfo.FieldKind = MQTTPayloadFieldKind.Field;
        variableInfo.Key = name;
        variableInfo.ValueVariable.DataType = OpcUa.DataTypes.String;
        variableInfo.ValueVariable.ActualDataType = OpcUa.DataTypes.String;
        if (string.IsNullOrEmpty(value))
        {
            variableInfo.Value = string.Empty;
            Log.Debug(LogicObject.BrowseName, $"{indent}{name}: String (empty) = \"\"");
            return variableInfo;
        }
        // Check if it's an ISO 8601 timestamp
        if (IsoDateTimePattern.IsMatch(value) && DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime dateTime))
        {
            variableInfo.ValueVariable.DataType = OpcUa.DataTypes.DateTime;
            variableInfo.ValueVariable.ActualDataType = OpcUa.DataTypes.DateTime;
            variableInfo.Value = dateTime;
            if (name.Equals("timestamp", StringComparison.OrdinalIgnoreCase))
            {
                Log.Debug(LogicObject.BrowseName, $"{indent}{name}: Detected timestamp field");
                variableInfo.FieldKind = MQTTPayloadFieldKind.LocalTimestampField;
            }
            Log.Debug(LogicObject.BrowseName, $"{indent}{name}: DateTime (ISO 8601) = \"{value}\" → {dateTime:yyyy-MM-dd HH:mm:ss.fff}");
            return variableInfo;
        }
        // Check if it's a date only
        if (DateOnlyPattern.IsMatch(value) && DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
        {
            variableInfo.ValueVariable.DataType = OpcUa.DataTypes.DateTime;
            variableInfo.ValueVariable.ActualDataType = OpcUa.DataTypes.DateTime;
            variableInfo.Value = date;
            Log.Debug(LogicObject.BrowseName, $"{indent}{name}: Date = \"{value}\" → {date:yyyy-MM-dd}");
            return variableInfo;
        }
        // Check if it's a time only
        if (TimeOnlyPattern.IsMatch(value) && TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out TimeSpan time))
        {
            variableInfo.ValueVariable.DataType = OpcUa.DataTypes.Duration;
            variableInfo.ValueVariable.ActualDataType = OpcUa.DataTypes.Duration;
            variableInfo.Value = time.TotalMilliseconds;
            Log.Debug(LogicObject.BrowseName, $"{indent}{name}: Time = \"{value}\" → {time:hh\\:mm\\:ss\\.fff}");
            return variableInfo;
        }
        // Check if it's a GUID
        if (Guid.TryParse(value, out Guid guid))
        {
            variableInfo.ValueVariable.DataType = OpcUa.DataTypes.Guid;
            variableInfo.ValueVariable.ActualDataType = OpcUa.DataTypes.Guid;
            variableInfo.Value = guid;
            Log.Debug(LogicObject.BrowseName, $"{indent}{name}: GUID = \"{guid}\"");
            return variableInfo;
        }
        // Check if it's a URL/URI
        if (Uri.TryCreate(value, UriKind.Absolute, out Uri uri))
        {
            variableInfo.Value = value;
            Log.Debug(LogicObject.BrowseName, $"{indent}{name}: URL = \"{value}\" → {uri.Scheme}://{uri.Host}");
            return variableInfo;
        }
        // Generic string
        Log.Debug(LogicObject.BrowseName, $"{indent}{name}: String = \"{value}\"");
        variableInfo.Value = value;
        return variableInfo;
    }

    private MQTTPayloadFieldInfo AnalyzeNumberValue(JsonElement element, string name, string indent)
    {
        var rawText = element.GetRawText();
        MQTTPayloadFieldInfo variableInfo = InformationModel.MakeObject<MQTTPayloadFieldInfo>(name);
        variableInfo.FieldKind = MQTTPayloadFieldKind.Field;
        variableInfo.Key = name;
        variableInfo.ValueVariable.DataType = OpcUa.DataTypes.Int32;
        variableInfo.ValueVariable.ActualDataType = OpcUa.DataTypes.Int32;
        // Check if it could be a Unix timestamp
        if (EpochPattern.IsMatch(rawText) && long.TryParse(rawText, out long timestamp))
        {
            try
            {
                DateTime dateTime;
                if (rawText.Length == 10) // Seconds
                {
                    dateTime = DateTimeOffset.FromUnixTimeSeconds(timestamp).DateTime;
                    Log.Debug(LogicObject.BrowseName, $"{indent}{name}: Unix Timestamp (seconds) = {timestamp} → {dateTime:yyyy-MM-dd HH:mm:ss}");
                    variableInfo.ValueVariable.DataType = OpcUa.DataTypes.DateTime;
                    variableInfo.ValueVariable.ActualDataType = OpcUa.DataTypes.DateTime;
                    variableInfo.Value = dateTime;
                    return variableInfo;
                }
                else if (rawText.Length == 13) // Milliseconds
                {
                    dateTime = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).DateTime;
                    Log.Debug(LogicObject.BrowseName, $"{indent}{name}: Unix Timestamp (milliseconds) = {timestamp} → {dateTime:yyyy-MM-dd HH:mm:ss.fff}");
                    variableInfo.ValueVariable.DataType = OpcUa.DataTypes.DateTime;
                    variableInfo.ValueVariable.ActualDataType = OpcUa.DataTypes.DateTime;
                    variableInfo.Value = dateTime;
                    return variableInfo;
                }
            }
            catch
            {
                // If conversion fails, treat as normal number
            }
        }
        if (element.TryGetInt32(out int intValue))
        {
            Log.Debug(LogicObject.BrowseName, $"{indent}{name}: Integer = {intValue}");
            variableInfo.Value = intValue;
        }
        else if (element.TryGetInt64(out long longValue))
        {
            Log.Debug(LogicObject.BrowseName, $"{indent}{name}: Long = {longValue}");
            variableInfo.ValueVariable.DataType = OpcUa.DataTypes.Int64;
            variableInfo.ValueVariable.ActualDataType = OpcUa.DataTypes.Int64;
            variableInfo.Value = longValue;
        }
        else if (element.TryGetSingle(out float floatValue))
        {
            Log.Debug(LogicObject.BrowseName, $"{indent}{name}: Float = {floatValue}");
            variableInfo.ValueVariable.DataType = OpcUa.DataTypes.Float;
            variableInfo.ValueVariable.ActualDataType = OpcUa.DataTypes.Float;
            variableInfo.Value = floatValue;
        }
        else if (element.TryGetDouble(out double doubleValue))
        {
            Log.Debug(LogicObject.BrowseName, $"{indent}{name}: Double = {doubleValue}");
            variableInfo.ValueVariable.DataType = OpcUa.DataTypes.Double;
            variableInfo.ValueVariable.ActualDataType = OpcUa.DataTypes.Double;
            variableInfo.Value = doubleValue;
        }
        else if (element.TryGetDecimal(out decimal decimalValue))
        {
            Log.Debug(LogicObject.BrowseName, $"{indent}{name}: Decimal = {decimalValue}");
            variableInfo.ValueVariable.DataType = OpcUa.DataTypes.Float;
            variableInfo.ValueVariable.ActualDataType = OpcUa.DataTypes.Float;
            variableInfo.Value = decimalValue;
        }
        else
        {
            Log.Debug(LogicObject.BrowseName, $"{indent}{name}: Number = {rawText}");
            variableInfo.Value = int.Parse(rawText);
        }
        return variableInfo;
    }

}
