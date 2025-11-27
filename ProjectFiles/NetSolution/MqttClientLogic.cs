#region Using directives
using System;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.UI;
using FTOptix.HMIProject;
using FTOptix.CoreBase;
using FTOptix.NetLogic;
using FTOptix.Core;
using FTOptix.MQTTBroker;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using FTOptix.MQTTClient;
using FTOptix.InfluxDBStoreRemote;
using FTOptix.OPCUAServer;
using FTOptix.NativeUI;
using System.Reflection.Metadata;
using FTOptix.System;
using System.Diagnostics.CodeAnalysis;
using FTOptix.CommunicationDriver;
using System.IO;
#endregion

public class MqttClientLogic : BaseNetLogic
{
    public static MqttClientLogic Instance { get; private set; }

    public override void Start()
    {
        Instance = this;
    }

    public override void Stop()
    {
        Instance = null;
        CommonLogic.DisposeTask(removeStationTask);
    }

    #region Methods exposed to Optix
    [ExportMethod]
    public void CreateNewMqttClient(NodeId widgetOwner)
    {
        var mqttClientFolder = Project.Current.Get<Folder>(CommonLogic.MQTTClientFolderPath);
        int countCurrentClient = mqttClientFolder.GetNodesByType<MQTTClient>().Count();
        string browseName = $"MQTTClient{countCurrentClient + 1}";
        if (mqttClientFolder.Get(browseName) == null)
        {
            var mqttClient = InformationModel.MakeObject<MQTTClient>(browseName);
            InitMqttClientNode(mqttClient);
            if (InformationModel.Get(widgetOwner) is ColumnLayout verticalLayout)
            {
                var newWidget = InformationModel.MakeObject<MQTTClientUIObj>(browseName);
                newWidget.SetAlias(CommonLogic.editAliasNameMapping.GetValueOrDefault(FTOptix.MQTTClient.ObjectTypes.MQTTClient), mqttClient);
                verticalLayout.Add(newWidget);
                newWidget.Find("StationActions").GetVariable("EnableSave").Value = true;
            }
        }
        else
        {
            NotificationsMessageHandlerLogic.Instance.RequestBannerNotification(ToastBannerNotificationLevel.Warning, "Cannot add the new MQTT client, already exist in the system");
        }
    }

    [ExportMethod]
    public void DeleteStation(NodeId station, NodeId widget)
    {
        IUANode[] nodesToDelete = { InformationModel.Get(station), InformationModel.Get(widget) };
        removeStationTask = new DelayedTask(DeleteStationTask, nodesToDelete, 100, LogicObject);
        removeStationTask.Start();
    }

    [ExportMethod]
    public void CreatePublisher(NodeId mqttClient, NodeId widgetOwner)
    {
        if (InformationModel.GetObject(mqttClient) is MQTTClient mqttClientNode)
        {
            int countCurrentPublisher = mqttClientNode.GetNodesByType<MQTTPublisher>().Count();
            string browseName = $"Publisher{countCurrentPublisher + 1}";
            if (mqttClientNode.Get(browseName) == null)
            {
                var mqttPublisher = InformationModel.MakeObject<MQTTPublisher>(browseName);
                InitMqttPublisherNode(mqttPublisher);
                var newWidget = InformationModel.MakeObject<MQTTPublisherUIObj>(browseName);
                if (InformationModel.Get(widgetOwner) is ColumnLayout verticalLayout)
                {
                    newWidget.SetAlias(CommonLogic.editAliasNameMapping.GetValueOrDefault(FTOptix.MQTTClient.ObjectTypes.MQTTPublisher), mqttPublisher);
                    newWidget.SetAlias("MQTTClientNode", mqttClientNode);
                    verticalLayout.Add(newWidget);                    
                    newWidget.FindByType<StationProps>().GetVariable("EnableSave").Value = true;
                }
                var payloadData = InformationModel.Make<MQTTPublisherDataConfiguration>($"{mqttClientNode.BrowseName}_{mqttPublisher.BrowseName}");
                InitMqttPublisherPayloadConfiguration(payloadData);
                newWidget.SetAlias(CommonLogic.editAliasNameMapping.GetValueOrDefault(OptixEdge_WizardApp.ObjectTypes.MQTTPublisherUIObj), payloadData);
            }
            else
            {
                NotificationsMessageHandlerLogic.Instance.RequestBannerNotification(ToastBannerNotificationLevel.Warning, $"Cannot add the new publisher to {mqttClientNode.BrowseName}, already exist");
            }
        }
    }

    [ExportMethod]
    public void SaveProperties(NodeId station, NodeId widget)
    {
        switch (InformationModel.Get(station))
        {
            case MQTTClient editClient when InformationModel.Get(widget) is IUAObject widgetNode:
                SaveClientProperties(editClient, widgetNode);
                break;
            case MQTTPublisher editPublisher when InformationModel.Get(widget) is IUAObject widgetNode:
                SavePublisherProperties(editPublisher, widgetNode);
                break;
        }
    }

    private void SaveClientProperties(MQTTClient editStation, IUAObject widgetNode)
    {
        try
        {
            string stationNodeAlias = CommonLogic.sourceAliasNameMapping.GetValueOrDefault(editStation.ObjectType.NodeId);
            var sourceStation = (MQTTClient)widgetNode.GetAlias(stationNodeAlias);
            var mqttClientFolder = Project.Current.Get<Folder>(CommonLogic.MQTTClientFolderPath);
            if (sourceStation == null)
            {
                // Temporarily impersonate root to perform the creation in the right context
                var sessionHandler = LogicObject.Context.Sessions.ImpersonateRootTemporary();
                sourceStation = InformationModel.Make<MQTTClient>(editStation.BrowseName);
                ApplyProperties(sourceStation, editStation);
                mqttClientFolder.Add(sourceStation);
                // Return to UI session context
                sessionHandler.Dispose();
                widgetNode.SetAlias(stationNodeAlias, sourceStation);
                NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Success, $"Successfully added new MQTT client.");
            }
            else
            {
                // Temporarily impersonate root to perform the update in the right context
                var sessionHandler = LogicObject.Context.Sessions.ImpersonateRootTemporary();
                sourceStation.Stop();
                ApplyProperties(sourceStation, editStation);
                sourceStation.Start();
                // Return to UI session context
                sessionHandler.Dispose();
                NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Success, $"Settings successfully updated for MQTT client {editStation.BrowseName}.");
            }
            widgetNode.GetVariable("EnableAddPublisher").Value = true;
            widgetNode.Find("StationActions").GetVariable("EnableSave").Value = false;
        }
        catch (Exception ex)
        {
            NotificationsMessageHandlerLogic.Instance.RequestBannerNotification(ToastBannerNotificationLevel.Error, "Critical error - Check application logs");
            Log.Error(LogicObject.BrowseName, $"{ex.Message} - Stack: {ex.StackTrace}");
        }
    }

    private void SavePublisherProperties(MQTTPublisher editStation, IUAObject widgetNode)
    {
        try
        {
            string stationNodeAlias = CommonLogic.sourceAliasNameMapping.GetValueOrDefault(editStation.ObjectType.NodeId);
            var sourceStation = (MQTTPublisher)widgetNode.GetAlias(stationNodeAlias);
            MQTTClient mqttClientOwner = null;
            if (sourceStation == null)
            {
                mqttClientOwner = (MQTTClient)widgetNode.GetAlias("MQTTClientNode");
                if (mqttClientOwner == null)
                {
                    throw new NullReferenceException("Missing MQTT client node!");
                }
                // Temporarily impersonate root to perform the creation in the right context
                var sessionHandler = LogicObject.Context.Sessions.ImpersonateRootTemporary();
                sourceStation = InformationModel.Make<MQTTPublisher>(editStation.BrowseName);
                ApplyProperties(sourceStation, editStation);
                mqttClientOwner.Stop();
                mqttClientOwner.Add(sourceStation);
                RegenerateMQTTPublisherDataConfiguration(widgetNode.GetAlias(CommonLogic.editAliasNameMapping.GetValueOrDefault(OptixEdge_WizardApp.ObjectTypes.MQTTPublisherUIObj)) as MQTTPublisherDataConfiguration, sourceStation);
                CommonLogic.GenerateAndAttachTagViewer(widgetNode, CommonLogic.TagViewerMQTTPublisherAliasSourceLink);
                mqttClientOwner.Start();
                // Return to UI session context
                sessionHandler.Dispose();
                widgetNode.SetAlias(stationNodeAlias, sourceStation);
                NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Success, $"Publisher created successfully on {mqttClientOwner.BrowseName}.");
            }
            else
            {
                // Temporarily impersonate root to perform the update in the right context
                var sessionHandler = LogicObject.Context.Sessions.ImpersonateRootTemporary();
                mqttClientOwner = (MQTTClient)sourceStation.Owner;
                if (mqttClientOwner == null)
                {
                    throw new NullReferenceException("Missing MQTT client node!");
                }

                mqttClientOwner.Stop();
                ApplyProperties(sourceStation, editStation);
                RegenerateMQTTPublisherDataConfiguration(widgetNode.GetAlias(CommonLogic.editAliasNameMapping.GetValueOrDefault(OptixEdge_WizardApp.ObjectTypes.MQTTPublisherUIObj)) as MQTTPublisherDataConfiguration, sourceStation);
                mqttClientOwner.Start();
                // Return to UI session context
                sessionHandler.Dispose();
                NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Success, $"Settings successfully updated for MQTT Publisher {editStation.BrowseName} on {mqttClientOwner.BrowseName}.");
            }
            widgetNode.Find("StationActions").GetVariable("EnableSave").Value = false;
            widgetNode.Find("StationActions").GetVariable("EnableImport").Value = true;

        }
        catch (Exception ex)
        {
            NotificationsMessageHandlerLogic.Instance.RequestBannerNotification(ToastBannerNotificationLevel.Error, "Critical error - Check application logs");
            Log.Error(LogicObject.BrowseName, $"{ex.Message} - Stack: {ex.StackTrace}");
        }
    }

    public void CreateOrUpdateTagsToPublish(NodeId mqttDataConfiguration)
    {
        // Temporarily impersonate root to perform the creation in the right context
        var sessionHandler = LogicObject.Context.Sessions.ImpersonateRootTemporary();
        try
        {
            if (InformationModel.GetObject(mqttDataConfiguration) is MQTTPublisherDataConfiguration mqttDataConfigurationNode)
            {

                CreateOrUpdateTags(mqttDataConfigurationNode);
                if (InformationModel.Get(mqttDataConfigurationNode.MQTTPublisherNode) is MQTTPublisher mqttPublisher)
                {
                    MQTTClient mqttClient = (MQTTClient)mqttPublisher.Owner;
                    mqttClient.Stop();
                    RegenerateMQTTPublisherDataConfiguration(mqttDataConfigurationNode, mqttPublisher);
                    mqttClient.Start();
                }
            }
            else
            {
                throw new NullReferenceException("Missing MQTT publisher publisher node!");
            }
            // Return to UI session context
            sessionHandler.Dispose();
        }
        catch (Exception ex)
        {
            // Return to UI session context
            sessionHandler.Dispose();
            NotificationsMessageHandlerLogic.Instance.RequestBannerNotification(ToastBannerNotificationLevel.Error, "Critical error - Check application logs");
            Log.Error(LogicObject.BrowseName, $"{ex.Message} - Stack: {ex.StackTrace}");
        }
    }
    #endregion

    private static void InitMqttClientNode(MQTTClient mqttClient)
    {
        mqttClient.BrokerAddress = "localhost";
        mqttClient.BrokerPort = 1883;
        mqttClient.ClientId = "FTOptixEdge_WizardApp-1";
        mqttClient.SSLTLSEnabled = false;
        mqttClient.ValidateBrokerCertificate = false;
        mqttClient.UserIdentityType = UserIdentityType.Anonymous;
        _ = mqttClient.UsernameVariable;
        _ = mqttClient.PasswordVariable;
        _ = mqttClient.CACertificateFileVariable;
        _ = mqttClient.ClientCertificateFileVariable;
        _ = mqttClient.ClientPrivateKeyFileVariable;
    }

    private static void InitMqttPublisherNode(MQTTPublisher mqttPublisher)
    {
        mqttPublisher.Topic = $"OptixEdge_WizardApp/{mqttPublisher.BrowseName}";
        mqttPublisher.SamplingMode = FTOptix.MQTTClient.SamplingMode.Periodic;
        mqttPublisher.SamplingPeriod = 1500;
        mqttPublisher.PollingPeriod = 500;
        mqttPublisher.QoS = QoSLevel.AtMostOnce;
        mqttPublisher.Retain = false;
    }

    public static void InitMqttPublisherPayloadConfiguration(MQTTPublisherDataConfiguration payloadData)
    {
        payloadData.PayloadKind = (int)MQTTPublisherPayloadKind.Optix;
        payloadData.SamplingMode = (int)FTOptix.MQTTClient.SamplingMode.Periodic;
    }

    private static void ApplyProperties(IUAObject stationNode, IUAObject editNode)
    {
        switch (stationNode)
        {
            case MQTTClient client:
                ApplyMQTTClientProperties(editNode as MQTTClient, client);
                break;
            case MQTTPublisher publisher:
                ApplyMQTTPublisherProperties(editNode as MQTTPublisher, publisher);
                break;
        }
    }

    private static void ApplyMQTTClientProperties(MQTTClient editNode, MQTTClient client)
    {
        client.BrokerAddress = editNode.BrokerAddress;
        client.BrokerPort = editNode.BrokerPort;
        client.ClientId = editNode.ClientId;
        client.SSLTLSEnabled = editNode.SSLTLSEnabled;
        client.ValidateBrokerCertificate = editNode.ValidateBrokerCertificate;
        client.CACertificateFile = editNode.CACertificateFile;
        client.ClientCertificateFile = editNode.ClientCertificateFile;
        client.ClientPrivateKeyFile = editNode.ClientPrivateKeyFile;
        client.UserIdentityType = editNode.UserIdentityType;
        client.Username = editNode.Username;
        client.Password = editNode.Password;
    }

    private static void ApplyMQTTPublisherProperties(MQTTPublisher editNode, MQTTPublisher publisher)
    {
        publisher.SamplingMode = editNode.SamplingMode;
        publisher.SamplingPeriod = editNode.SamplingPeriod;
        publisher.PollingPeriod = editNode.PollingPeriod;
        publisher.QoS = editNode.QoS;
        publisher.Retain = editNode.Retain;
        publisher.Topic = editNode.Topic;
    }

    private void RegenerateMQTTPublisherDataConfiguration(MQTTPublisherDataConfiguration dataConfiguration, MQTTPublisher publisherNode)
    {
        var oldConfiguration = Project.Current.Get<MQTTPublisherDataConfiguration>($"{CommonLogic.MQTTPublishersDataConfigurationPath}/{dataConfiguration.BrowseName}");
        if ((MQTTPublisherPayloadKind)dataConfiguration.PayloadKind == MQTTPublisherPayloadKind.Optix)
        {
            try
            {
                dataConfiguration.PayloadStructure.Get<MQTTPayloadFieldInfo>("root").GetNodesByType<MQTTPayloadFieldInfo>().ToList().ForEach(x => x.Delete());
                dataConfiguration.PayloadStructurePreview.Get<MQTTPayloadFieldInfo>("root").GetNodesByType<MQTTPayloadFieldInfo>().ToList().ForEach(x => x.Delete());
            }
            catch
            {
                // Root node not exist and nothing to delete
            }
        }
        if (oldConfiguration != null)
        {
            if ((MQTTPublisherPayloadKind)oldConfiguration.PayloadKind != (MQTTPublisherPayloadKind)dataConfiguration.PayloadKind)
            {
                try
                {
                    dataConfiguration.Data.Children.Clear();
                }
                catch
                {
                    // Error during clear, nothing important
                }
            }
        }
        MQTTPublisherDataConfiguration newConfiguration = LogicObject.Context.NodeFactory.CloneNode(dataConfiguration, dataConfiguration.NodeId.NamespaceIndex, NamingRuleType.Mandatory);
        newConfiguration.MQTTPublisherNode = publisherNode.NodeId;
        oldConfiguration?.Delete();
        Project.Current.Get(CommonLogic.MQTTPublishersDataConfigurationPath).Add(newConfiguration);
        foreach (var dataChild in newConfiguration.Data.Children)
        {
            RecreateDynamicLinks(dataChild);
        }
    }

    private void RecreateDynamicLinks(IUANode nodeToAnalyze)
    {
        switch (nodeToAnalyze)
        {
            case IUAVariable variable:
                if (variable.Refs.GetNode(FTOptix.CoreBase.ReferenceTypes.HasDynamicLink) is IUAVariable linkedVariable)
                {
                    if (LogicObject.Context.ResolvePath(variable, linkedVariable.Value) is PathResolverResult result && result.ResolvedNode is IUAVariable targetVariable)
                    {
                        variable.SetDynamicLink(targetVariable, DynamicLinkMode.Read);
                    }
                }
                break;
            case IUAObject objectNode:
                foreach (var child in objectNode.Children)
                {
                    RecreateDynamicLinks(child);
                }
                break;
        }
    }

    private void CreateOrUpdateTags(MQTTPublisherDataConfiguration mqttDataConfiguration)
    {
        if (Project.Current.Get($"Model/{mqttDataConfiguration.BrowseName}") is not IUAObject temporaryFolder)
        {
            NotificationsMessageHandlerLogic.Instance.RequestBannerNotification(ToastBannerNotificationLevel.Warning, $"Cannot add variables to publisher {mqttDataConfiguration.BrowseName} - Check application logs");
            Log.Warning(LogicObject.BrowseName, $"Missing temporary folder for publisher {mqttDataConfiguration.BrowseName}");
            return;
        }
        int createdTags = 0;
        var publisherTagFolder = mqttDataConfiguration.Data;
        foreach (var sourceFieldFolder in temporaryFolder.GetNodesByType<Folder>())
        {
            var dataFromTagImporter = sourceFieldFolder.GetNodesByType<TagCustomGridRowData>();
            foreach (var tagData in dataFromTagImporter.Where(x => x.Checked))
            {
                string variableName = $"{sourceFieldFolder.BrowseName}.{tagData.VariableName}";
                IUAVariable targetTag = publisherTagFolder.GetVariable(variableName);
                if (targetTag == null)
                {
                    if (tagData.VariableIsArray)
                    {
                        targetTag = InformationModel.MakeVariable(variableName, tagData.VariableDataTypeNodeId, tagData.VariableArrayDimension);
                    }
                    else
                    {
                        targetTag = InformationModel.MakeVariable(variableName, tagData.VariableDataTypeNodeId);
                    }
                    targetTag.Description = new(tagData.VariableComment, Session.ActualLocaleId);
                    publisherTagFolder.Add(targetTag);
                    if (InformationModel.GetVariable(tagData.VariableNodeId) is IUAVariable sourceTag)
                    {
                        targetTag.SetDynamicLink(sourceTag);
                    }
                    else
                    {
                        Log.Warning(LogicObject.BrowseName, $"sourceTag {tagData.VariableName} not found!");
                    }
                    createdTags++;
                }
            }
            var deletedTags = DeleteMissingTag(mqttDataConfiguration, dataFromTagImporter.Where(x => !x.Checked).ToList());
            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Info, $"Added {createdTags}, removed {deletedTags} variables on the publisher {mqttDataConfiguration.BrowseName}.");
        }
    }

    private int DeleteMissingTag(MQTTPublisherDataConfiguration mqttDataConfiguration, List<TagCustomGridRowData> tagDatasUnchecked)
    {
        int deletedTagsCounter = 0;
        List<TagDataImported> tagsImported = CommonLogic.ReadTagsFromSourceDataCollector(mqttDataConfiguration.Data, mqttDataConfiguration);
        foreach (var tagData in tagsImported.Where(x => tagDatasUnchecked.Exists(y => y.VariableName == x.BrowseName)))
        {
            InformationModel.Get(tagData.NodeId)?.Delete();
            deletedTagsCounter++;
        }
        return deletedTagsCounter;
    }

    private void DeleteStationTask(DelayedTask task, object arguments)
    {
        var nodesToDelete = (IUANode[])arguments;
        if ((nodesToDelete[0] is MQTTClient || nodesToDelete[0] is MQTTPublisher || nodesToDelete[0] is MQTTPayloadFieldInfo) && nodesToDelete[1] is Item mqttWidget)
        {
            // Delete editStation
            try
            {
                nodesToDelete[0].Delete();
            }
            catch
            {
                // nothing important
            }
            switch (nodesToDelete[0])
            {
                case MQTTClient editClient:
                    DeleteMQTTClient(editClient, mqttWidget);
                    break;
                case MQTTPublisher editPublisher:
                    DeleteMQTTPublisher(editPublisher, mqttWidget);
                    break;
            }
            // Delete widget
            try
            {
                mqttWidget.Delete();
            }
            catch
            {
                // nothing important
            }
        }
        task.Dispose();
    }

    private static void DeleteMQTTClient(MQTTClient editStation, IUAObject widgetToDelete)
    {
        string stationNodeAlias = CommonLogic.sourceAliasNameMapping.GetValueOrDefault(editStation.ObjectType.NodeId);
        var sourceStation = (MQTTClient)widgetToDelete.GetAlias(stationNodeAlias);
        // Delete source station
        try
        {
            if (sourceStation != null)
            {
                string sourceStationName = sourceStation.BrowseName;
                foreach (var configuration in Project.Current.Get(CommonLogic.MQTTPublishersDataConfigurationPath).Children.Where(x => x.BrowseName.StartsWith(sourceStationName)))
                {
                    configuration.Delete();
                }
                sourceStation.Delete();
                NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Success, $"MQTT Client {sourceStationName} successfully deleted.");
            }
        }
        catch
        {
            // nothing important
        }
    }

    private static void DeleteMQTTPublisher(MQTTPublisher editStation, IUAObject widgetToDelete)
    {
        string stationNodeAlias = CommonLogic.sourceAliasNameMapping.GetValueOrDefault(editStation.ObjectType.NodeId);
        var sourceStation = (MQTTPublisher)widgetToDelete.GetAlias(stationNodeAlias);
        // Delete source station
        try
        {
            if (sourceStation != null)
            {
                var mqttClientOwner = (MQTTClient)sourceStation.Owner;
                Project.Current.Get($"{CommonLogic.MQTTPublishersDataConfigurationPath}/{mqttClientOwner.BrowseName}_{sourceStation.BrowseName}")?.Delete();
                mqttClientOwner.Stop();
                string publisherName = sourceStation.BrowseName;
                sourceStation.Delete();
                NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Success, $"Publisher configuration {publisherName} of MQTT Client {mqttClientOwner.BrowseName} successfully deleted.");
                mqttClientOwner.Start();
            }
        }
        catch
        {
            // nothing important
        }
    }

    private static void DeleteMQTTPayloadInfo(MQTTPayloadFieldInfo editField)
    {
        string fieldName = editField.Key;
        string notifiactionMessageKind = (MQTTPayloadFieldKind)editField.FieldKind switch
        {
            MQTTPayloadFieldKind.Field => "field",
            MQTTPayloadFieldKind.ArrayField => "array field",
            MQTTPayloadFieldKind.LocalTimestampField => "date/time field",
            MQTTPayloadFieldKind.UTCTimestampField => "date/time (UTC) field",
            MQTTPayloadFieldKind.VariablesCollection => "variables collection",
            MQTTPayloadFieldKind.NestedObject => "nested object",
            MQTTPayloadFieldKind.NestedObjectArray => "nested object array",
            MQTTPayloadFieldKind.NestedObjectArrayElement => "nested object array element",
            _ => "unknown"
        };
        try
        {
            editField.Delete();
            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Success, $"Payload {notifiactionMessageKind} {fieldName} successfully deleted.");
        }
        catch
        {
            // nothing important
        }
    }

    public static void GeneratePayloadWidget(IUANode aliasNode, ColumnLayout widgetOwner, IUAVariable currentSelectedIndex, IUAVariable lastIndexReleased)
    {
        if (aliasNode is MQTTPublisherDataConfiguration dataConfiguration && (MQTTPublisherPayloadKind)dataConfiguration.PayloadKind == MQTTPublisherPayloadKind.Custom)
        {
            if (dataConfiguration.PayloadStructure.Get("root") is MQTTPayloadFieldInfo rootNode)
            {
                uint fieldIndex = 1;
                foreach (var fieldInfo in rootNode.GetNodesByType<MQTTPayloadFieldInfo>())
                {
                    GeneratePayloadWidgetFromConfiguration(fieldInfo, widgetOwner, ref fieldIndex, currentSelectedIndex);
                }
                lastIndexReleased.Value = fieldIndex;
            }
        }
    }

    public static void GeneratePayloadWidgetFromConfiguration(MQTTPayloadFieldInfo dataConfiguration, IUANode widgetOwner, ref uint fieldIndex, IUAVariable currentSelectedIndex)
    {
        switch ((MQTTPayloadFieldKind)dataConfiguration.FieldKind)
        {
            case MQTTPayloadFieldKind.Field:
            case MQTTPayloadFieldKind.ArrayField:
            case MQTTPayloadFieldKind.LocalTimestampField:
            case MQTTPayloadFieldKind.UTCTimestampField:
                var newFieldWidget = InformationModel.MakeObject<MQTTPayloadFieldBase>(dataConfiguration.BrowseName);
                newFieldWidget.DisplayName = new LocalizedText(dataConfiguration.Key, "en-US");
                newFieldWidget.SetAlias(CommonLogic.editAliasNameMapping.GetValueOrDefault(newFieldWidget.ObjectType.NodeId), dataConfiguration);
                newFieldWidget.Find<Panel>("ValueContainer").Add(GenerateUIItemFromValue(dataConfiguration));
                newFieldWidget.GetVariable("CurrentSelectedField").SetDynamicLink(currentSelectedIndex, DynamicLinkMode.ReadWrite);
                newFieldWidget.GetVariable("FieldIndex").SetValue(fieldIndex);
                widgetOwner.Add(newFieldWidget);
                fieldIndex++;
                break;
            case MQTTPayloadFieldKind.NestedObject:
            case MQTTPayloadFieldKind.NestedObjectArray:
            case MQTTPayloadFieldKind.NestedObjectArrayElement:
            case MQTTPayloadFieldKind.VariablesCollection:
                var newObjectWidget = InformationModel.MakeObject<MQTTPayloadObject>(dataConfiguration.BrowseName);
                newObjectWidget.DisplayName = new LocalizedText(dataConfiguration.Key, "en-US");
                newObjectWidget.SetAlias(CommonLogic.editAliasNameMapping.GetValueOrDefault(newObjectWidget.ObjectType.NodeId), dataConfiguration);
                widgetOwner.Add(newObjectWidget);
                if ((MQTTPayloadFieldKind)dataConfiguration.FieldKind == MQTTPayloadFieldKind.VariablesCollection)
                {
                    var tagViewer = InformationModel.MakeObject<TagViewer>("TagViewer");
                    tagViewer.SetAlias(CommonLogic.editAliasNameMapping.GetValueOrDefault(OptixEdge_WizardApp.ObjectTypes.TagViewer), newObjectWidget);
                    newObjectWidget.Content.Add(tagViewer);
                }
                else
                {
                    foreach (var child in dataConfiguration.GetNodesByType<MQTTPayloadFieldInfo>())
                    {
                        GeneratePayloadWidgetFromConfiguration(child, newObjectWidget.Get<ColumnLayout>("Content/Content"), ref fieldIndex, currentSelectedIndex);
                    }
                }
                break;
        }
    }

    public static Item GenerateUIItemFromValue(MQTTPayloadFieldInfo payloadFieldInfo)
    {
        Item newValueSelector;
        if (string.IsNullOrEmpty(payloadFieldInfo.ValueDataVariablePath))
        {
            if (payloadFieldInfo.ValueVariable.ArrayDimensions.Length <= 0)
            {
                switch (payloadFieldInfo.ValueVariable.DataType)
                {
                    case NodeId dataType when dataType == OpcUa.DataTypes.Boolean:
                        var newSwitch = InformationModel.MakeObject<BooleanComboBox>("ValueSwitch");
                        newSwitch.SelectedValueVariable.SetDynamicLink(payloadFieldInfo.ValueVariable, DynamicLinkMode.ReadWrite);
                        newValueSelector = newSwitch;
                        break;
                    case NodeId dataType when dataType == OpcUa.DataTypes.SByte || dataType == OpcUa.DataTypes.Byte ||
                         dataType == OpcUa.DataTypes.Int16 || dataType == OpcUa.DataTypes.UInt16 ||
                         dataType == OpcUa.DataTypes.Int32 || dataType == OpcUa.DataTypes.UInt32 ||
                         dataType == OpcUa.DataTypes.Int64 || dataType == OpcUa.DataTypes.UInt64 ||
                         dataType == OpcUa.DataTypes.Float || dataType == OpcUa.DataTypes.Double:
                        var spinBox = InformationModel.MakeObject<SpinBox>("Value");
                        spinBox.ValueVariable.SetDynamicLink(payloadFieldInfo.ValueVariable, DynamicLinkMode.ReadWrite);
                        newValueSelector = spinBox;
                        break;
                    case NodeId dataType when dataType == OpcUa.DataTypes.DateTime || dataType == OpcUa.DataTypes.UtcTime:
                        switch ((MQTTPayloadFieldKind)payloadFieldInfo.FieldKind)
                        {
                            case MQTTPayloadFieldKind.LocalTimestampField:
                            case MQTTPayloadFieldKind.UTCTimestampField:
                                var timestampLabel = InformationModel.MakeObject<Label>("Value");
                                timestampLabel.Text = (MQTTPayloadFieldKind)payloadFieldInfo.FieldKind == MQTTPayloadFieldKind.LocalTimestampField ? "System time (local)" : "System time (UTC)";
                                timestampLabel.Style = "AdditionalInfo";
                                timestampLabel.TextVerticalAlignment = TextVerticalAlignment.Center;
                                newValueSelector = timestampLabel;
                                break;
                            default:
                                var dateTimePicker = InformationModel.MakeObject<DateTimePicker>("Value");
                                dateTimePicker.ValueVariable.SetDynamicLink(payloadFieldInfo.ValueVariable, DynamicLinkMode.ReadWrite);
                                newValueSelector = dateTimePicker;
                                break;
                        }
                        break;
                    case NodeId dataType when dataType == OpcUa.DataTypes.Duration:
                        var durationPicker = InformationModel.MakeObject<DurationPicker>("Value");
                        durationPicker.ValueVariable.SetDynamicLink(payloadFieldInfo.ValueVariable, DynamicLinkMode.ReadWrite);
                        newValueSelector = durationPicker;
                        break;
                    default:
                        var textBoxDefault = InformationModel.MakeObject<TextBox>("Value");
                        textBoxDefault.TextVariable.SetDynamicLink(payloadFieldInfo.ValueVariable, DynamicLinkMode.ReadWrite);
                        textBoxDefault.ValueChangeBehaviour = ValueChangeBehaviour.ValueChangeWhileEditing;
                        newValueSelector = textBoxDefault;
                        break;
                }
            }
            else
            {
                var newArrayEditor = InformationModel.MakeObject<ArrayFieldWithEditor>("ValueArrayEditor");
                newArrayEditor.SetAlias(CommonLogic.editAliasNameMapping.GetValueOrDefault(OptixEdge_WizardApp.ObjectTypes.ArrayFieldWithEditor), payloadFieldInfo.ValueVariable);
                newValueSelector = newArrayEditor;
            }
        }
        else
        {
            var newLinkedField = InformationModel.MakeObject<LinkedVariableToField>("ValueLinked");
            newLinkedField.GetByType<Label>().Text = payloadFieldInfo.ValueDataVariablePath;
            newValueSelector = newLinkedField;
        }
        newValueSelector.HorizontalAlignment = HorizontalAlignment.Stretch;
        newValueSelector.VerticalAlignment = VerticalAlignment.Stretch;
        return newValueSelector;

    }

    public static IUAObject GetSourceDataFromPayloadObject(MQTTPayloadObject payloadObject)
    {
        if (payloadObject.GetAlias(CommonLogic.sourceAliasNameMapping.GetValueOrDefault(OptixEdge_WizardApp.ObjectTypes.MQTTPayloadObject)) is MQTTPayloadFieldInfo fieldInfoData)
        {
            var dataConfiguration = (MQTTPublisherDataConfiguration)CommonLogic.GetOwner(fieldInfoData, OptixEdge_WizardApp.ObjectTypes.MQTTPublisherDataConfiguration);
            return dataConfiguration.Data.GetObject(fieldInfoData.ValueDataVariablePath);
        }
        return null;
    }

    private DelayedTask removeStationTask;
}
