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
using FTOptix.AuditSigning;
using FTOptix.NativeUI;
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
        var mqttPublishersVariableFolders = Project.Current.Get(CommonLogic.MQTTDataFolderPath);
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
            if (mqttPublishersVariableFolders.Get(browseName) == null)
            {
                mqttPublishersVariableFolders.Add(InformationModel.Make<Folder>(browseName));
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
        IUANode[] nodesToDelete = {InformationModel.Get(station), InformationModel.Get(widget)};
        removeStationTask = new DelayedTask(DeleteStationTask, nodesToDelete, 100, LogicObject);
        removeStationTask.Start();
    }

    [ExportMethod]
    public void CreatePublisher(NodeId mqttClient, NodeId widgetOwner)
    {
        if (InformationModel.GetObject(mqttClient) is MQTTClient mqttClientNode)
        {
            var mqttPublishersVariableFolders = Project.Current.Get($"{CommonLogic.MQTTDataFolderPath}/{mqttClientNode.BrowseName}");
            int countCurrentPublisher = mqttClientNode.GetNodesByType<MQTTPublisher>().Count();
            string browseName = $"Publisher{countCurrentPublisher + 1}";
            if (mqttClientNode.Get(browseName) == null)
            {
                var mqttPublisher = InformationModel.MakeObject<MQTTPublisher>(browseName);
                mqttPublisher.Topic = $"Optix_DefaultApplication_OptixEdge/{browseName}";
                var topicFolder = mqttPublishersVariableFolders.Get(browseName);
                if (topicFolder == null)
                {
                    topicFolder = InformationModel.Make<Folder>(browseName);
                    mqttPublishersVariableFolders.Add(topicFolder);
                }
                InitMqttPublisherNode(mqttPublisher, topicFolder.NodeId);
                if (InformationModel.Get(widgetOwner) is ColumnLayout verticalLayout)
                {
                    var newWidget = InformationModel.MakeObject<MQTTClientPublisherUIObj>(browseName);
                    newWidget.SetAlias(CommonLogic.editAliasNameMapping.GetValueOrDefault(FTOptix.MQTTClient.ObjectTypes.MQTTPublisher), mqttPublisher);
                    verticalLayout.Add(newWidget);
                    newWidget.FindByType<StationProps>().GetVariable("EnableSave").Value = false;
                }
                mqttClientNode.Add(mqttPublisher);
                NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Success, $"New publisher successfully created on {mqttClientNode.BrowseName}.");
            }
            else
            {
                NotificationsMessageHandlerLogic.Instance.RequestBannerNotification(ToastBannerNotificationLevel.Warning, $"Cannot add the new publisher to {mqttClientNode.BrowseName}, already exist");
            }
        }
    }

    [ExportMethod]
    public void SaveProperties(NodeId mqttClient, NodeId widget)
    {
        try
        {
            if (InformationModel.GetObject(mqttClient) is MQTTClient editStation && InformationModel.GetObject(widget) is IUAObject widgetNode)
            {
                string stationNodeAlias = CommonLogic.sourceAliasNameMapping.GetValueOrDefault(editStation.ObjectType.NodeId);
                var sourceStation = (MQTTClient)widgetNode.GetAlias(stationNodeAlias);
                var mqttClientFolder = Project.Current.Get<Folder>(CommonLogic.MQTTClientFolderPath);
                if (sourceStation == null)
                {
                    sourceStation = InformationModel.Make<MQTTClient>(editStation.BrowseName);
                    ApplyProperties(sourceStation, editStation);
                    mqttClientFolder.Add(sourceStation);
                    widgetNode.SetAlias(stationNodeAlias, sourceStation);
                    NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Success, $"New MQTT client successfully created.");
                }
                else
                {
                    sourceStation.Stop();
                    ApplyProperties(sourceStation, editStation);
                    sourceStation.Start();
                    NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Success, $"Parameters for MQTT client {editStation.BrowseName} successfully saved.");
                }
                widgetNode.GetVariable("EnableAddPublisher").Value = true;
                widgetNode.Find("StationActions").GetVariable("EnableSave").Value = false;
            }
            else
            {
                throw new NullReferenceException("Missing MQTT client node!");
            }
        }
        catch (Exception ex)
        {
            NotificationsMessageHandlerLogic.Instance.RequestBannerNotification(ToastBannerNotificationLevel.Error, "Critical error - Check application logs");
            Log.Error(LogicObject.BrowseName, $"{ex.Message} - Stack: {ex.StackTrace}");
        }
    }

    public void CreateOrUpdateTagsToPublish(NodeId mqttPublisher)
    {
        try
        {
            if (InformationModel.GetObject(mqttPublisher) is MQTTPublisher mqttPublisherNode)
            {
                var mqttClient = (IUAObject)mqttPublisherNode.Owner;
                mqttClient.Stop();
                CreateOrUpdateTags(mqttPublisherNode);
                mqttClient.Start();
            }
            else
            {
                throw new NullReferenceException("Missing MQTT publisher publisher node!");
            }
        }
        catch (Exception ex)
        {
            NotificationsMessageHandlerLogic.Instance.RequestBannerNotification(ToastBannerNotificationLevel.Error, "Critical error - Check application logs");
            Log.Error(LogicObject.BrowseName, $"{ex.Message} - Stack: {ex.StackTrace}");
        }
    }

    [ExportMethod]
    public void SavePublisherParameters(NodeId mqttPublisher, NodeId widget)
    {
        try
        {
            if (InformationModel.GetObject(mqttPublisher) is MQTTPublisher mqttPublisherNode && InformationModel.GetObject(widget) is IUAObject widgetNode)
            {
                var mqttClient = (IUAObject)mqttPublisherNode.Owner;
                mqttClient.Stop();
                mqttClient.Start();
                NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Success, $"Parameters for publisher {mqttPublisherNode.BrowseName} of MQTT client {mqttClient.BrowseName} successfully saved.");
            }
            else
            {
                throw new NullReferenceException("Missing MQTT publisher publisher node!");
            }
            widgetNode.Find("StationActions").GetVariable("EnableSave").Value = false;
        }
        catch (Exception ex)
        {
            NotificationsMessageHandlerLogic.Instance.RequestBannerNotification(ToastBannerNotificationLevel.Error, "Critical error - Check application logs");
            Log.Error(LogicObject.BrowseName, $"{ex.Message} - Stack: {ex.StackTrace}");
        }
    }

    #endregion

    private static void InitMqttClientNode(MQTTClient mqttClient)
    {
        mqttClient.BrokerAddress = "localhost";
        mqttClient.BrokerPort = 1883;
        mqttClient.ClientId = "FTOptix_DefaultApplication_OptixEdge-1";        
        mqttClient.SSLTLSEnabled = false;
        mqttClient.ValidateBrokerCertificate = false;        
        mqttClient.UserIdentityType = UserIdentityType.Anonymous;
        _ = mqttClient.UsernameVariable;
        _ = mqttClient.PasswordVariable;
        _ = mqttClient.CACertificateFileVariable;
        _ = mqttClient.ClientCertificateFileVariable;
        _ = mqttClient.ClientPrivateKeyFileVariable;
    }

    private static void InitMqttPublisherNode(MQTTPublisher mqttPublisher, NodeId topicFolder)
    {
        mqttPublisher.SamplingMode = SamplingMode.Periodic;
        mqttPublisher.SamplingPeriod = 1500;
        mqttPublisher.PollingPeriod = 2500;
        mqttPublisher.Folder = topicFolder;
        mqttPublisher.QoS = QoSLevel.AtMostOnce;
        mqttPublisher.Retain = false;
    }

    private static void ApplyProperties(MQTTClient stationNode, MQTTClient editNode)
    {
        stationNode.BrokerAddress = editNode.BrokerAddress;
        stationNode.BrokerPort = editNode.BrokerPort;
        stationNode.ClientId = editNode.ClientId;
        stationNode.SSLTLSEnabled = editNode.SSLTLSEnabled;
        stationNode.ValidateBrokerCertificate = editNode.ValidateBrokerCertificate;
        stationNode.CACertificateFile = editNode.CACertificateFile;
        stationNode.ClientCertificateFile = editNode.ClientCertificateFile;
        stationNode.ClientPrivateKeyFile = editNode.ClientPrivateKeyFile;
        stationNode.UserIdentityType = editNode.UserIdentityType;
        stationNode.Username = editNode.Username;
        stationNode.Password = editNode.Password;
    }

    private void CreateOrUpdateTags(IUAObject mqttPublisher)
    {
        if (Project.Current.Get($"Model/{mqttPublisher.Owner.BrowseName}/{mqttPublisher.BrowseName}") is not IUAObject temporaryFolder)
        {
            NotificationsMessageHandlerLogic.Instance.RequestBannerNotification(ToastBannerNotificationLevel.Warning, $"Cannot add variables to publisher {mqttPublisher.BrowseName} of MQTT Client {mqttPublisher.Owner.BrowseName} - Check application logs");
            Log.Warning(LogicObject.BrowseName, $"Missing temporary folder for publisher {mqttPublisher.BrowseName} of client {mqttPublisher.Owner.BrowseName}");
            return;
        }
        int createdTags = 0;
        var publisherTagFolder = Project.Current.Get<Folder>($"{CommonLogic.MQTTDataFolderPath}/{mqttPublisher.Owner.BrowseName}/{mqttPublisher.BrowseName}");
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
            var deletedTags = DeleteMissingTag(mqttPublisher, dataFromTagImporter.Where(x => !x.Checked).ToList());
            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Info, $"Added {createdTags}, removed {deletedTags} variables on the publisher {mqttPublisher.BrowseName} of client {mqttPublisher.Owner.BrowseName}");
        }
    }

    private int DeleteMissingTag(IUAObject mqttPublisher, List<TagCustomGridRowData> tagDatasUnchecked)
    {
        int deletedTagsCounter = 0;
        List<TagDataImported> tagsImported = CommonLogic.ReadTagsFromSourceDataCollector(Project.Current.Get($"{CommonLogic.MQTTDataFolderPath}/{mqttPublisher.Owner.BrowseName}/{mqttPublisher.BrowseName}"), mqttPublisher);
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
        if ((nodesToDelete[0] is MQTTClient || nodesToDelete[0] is MQTTPublisher) && nodesToDelete[1] is Item mqttWidget)
        {
            MQTTClient sourceStation = null;
            if (nodesToDelete[0] is MQTTClient editStation)
            {
                string stationNodeAlias = CommonLogic.sourceAliasNameMapping.GetValueOrDefault(editStation.ObjectType.NodeId);
                sourceStation = (MQTTClient)mqttWidget.GetAlias(stationNodeAlias);
            } 
            try
            {
                nodesToDelete[1].Delete();
            }
            catch
            {
                // nothing important
            }          
            switch (nodesToDelete[0])
            {
                case MQTTClient:              
                    Project.Current.Get($"{CommonLogic.MQTTDataFolderPath}/{nodesToDelete[0].BrowseName}")?.Delete();
                    try
                    {
                        nodesToDelete[0].Delete();
                    }
                    catch
                    {
                        // nothing important
                    }
                    try
                    {
                        if (sourceStation != null)
                        {
                            string sourceStationName = sourceStation.BrowseName;     
                            sourceStation.Delete();               
                            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Success, $"MQTT Client {sourceStationName} successfully deleted.");
                        } 
                    }
                    catch
                    {
                        // nothing important
                    }
                    break;
                case MQTTPublisher mqttPublisher:
                    try
                    {
                        var mqttClientOwner = (MQTTClient)mqttPublisher.Owner;
                        Project.Current.Get($"{CommonLogic.MQTTDataFolderPath}/{mqttPublisher.Owner.BrowseName}/{mqttPublisher.BrowseName}")?.Delete();        
                        mqttClientOwner.Stop();
                        string publisherName = mqttPublisher.BrowseName;
                        mqttPublisher.Delete();
                        NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Success, $"Publisher configuration {publisherName} of MQTT Client {mqttClientOwner.BrowseName} successfully deleted.");
                        mqttClientOwner.Start();
                    }
                    catch
                    {
                        // nothing important
                    }
                    break;
            }
        }
        task.Dispose();
    }

    private DelayedTask removeStationTask;
}
