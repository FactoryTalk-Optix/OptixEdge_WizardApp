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
using FTOptix.System;
using FTOptix.Retentivity;
using FTOptix.CommunicationDriver;
using FTOptix.OPCUAClient;
using FTOptix.DataLogger;
using FTOptix.MQTTClient;
using FTOptix.Core;
using System.Collections.Generic;
using System.Threading;
using System.Linq;
using FTOptix.AuditSigning;
using FTOptix.NativeUI;
#endregion

public class OpcUaServerLogic : BaseNetLogic
{
    public static OpcUaServerLogic Instance { get; private set; }

    public override void Start()
    {
        Instance = this;
    }

    public override void Stop()
    {
        Instance = null;
        CommonLogic.DisposeTask(removeStationTask);
        CommonLogic.DisposeTask(generateConfigurationTags);
    }

    #region Methods exposed to Optix
    [ExportMethod]
    public void CreateNewOPCUaServer(NodeId widgetOwner)
    {
        var opcUaServerFolder = Project.Current.Get<Folder>(CommonLogic.OPCUAServerFolderPath);
        var opcUaConfigurationsVariableFolder = Project.Current.Get(CommonLogic.OPCUAServerDataFolderPath);
        if (opcUaServerFolder.GetNodesByType<OPCUAServer>() is IEnumerable<OPCUAServer> listOpcUaServers && listOpcUaServers.Count() > 0)
        {
            Log.Warning(LogicObject.BrowseName, "Only one instance of OPC-UA Server is allowed in the project!");
            NotificationsMessageHandlerLogic.Instance.RequestBannerNotification(ToastBannerNotificationLevel.Warning, "Only one instance of OPC-UA Server is allowed in the project!");
            return;
        }
        string browseName = "OPCUAServer1";
        var opcUaServer = InformationModel.MakeObject<OPCUAServer>(browseName);
        InitOpcUaServerNode(opcUaServer);
        if (InformationModel.Get(widgetOwner) is ColumnLayout verticalLayout)
        {
            var newWidget = InformationModel.MakeObject<OPCUAServerStationUIObj>(browseName);
            newWidget.SetAlias(CommonLogic.editAliasNameMapping.GetValueOrDefault(FTOptix.OPCUAServer.ObjectTypes.OPCUAServer), opcUaServer);
            newWidget.Find("StationActions").GetVariable("EnableSave").Value = true;
            verticalLayout.Add(newWidget);
        }
        if (opcUaConfigurationsVariableFolder.Get(browseName) == null)
        {
            opcUaConfigurationsVariableFolder.Add(InformationModel.Make<Folder>(browseName));
        }
    }

    [ExportMethod]
    public void DeleteStation(NodeId station, NodeId widget)
    {
        IUANode[] taskArguments = [InformationModel.Get(station), InformationModel.Get(widget)];
        removeStationTask = new DelayedTask(DeleteStationTask, taskArguments, 50, LogicObject);
        removeStationTask.Start();
    }

    [ExportMethod]
    public void CreateConfiguration(NodeId opcUaServer, NodeId widgetOwner)
    {
        if (InformationModel.Get(opcUaServer) is OPCUAServer opcUaServerNode)
        {
            var opcUaConfigurationsVariableFolder = Project.Current.Get($"{CommonLogic.OPCUAServerDataFolderPath}/{opcUaServerNode.BrowseName}");
            var nodeToPublishCollection = opcUaServerNode.GetObject("NodesToPublish");
            int countCurrentPublisher = nodeToPublishCollection.GetNodesByType<NodesToPublishConfigurationEntry>().Count();
            string browseName = $"Configuration{countCurrentPublisher + 1}";
            if (nodeToPublishCollection.Get(browseName) == null)
            {
                var configuration = InformationModel.MakeObject<NodesToPublishConfigurationEntry>(browseName);
                var userPointer = InformationModel.MakeVariable<NodePointer>("User1", OpcUa.DataTypes.NodeId);
                userPointer.Value = FTOptix.Core.Objects.AnonymousUser;
                configuration.Users.Add(userPointer);
                var configurationFolder = opcUaConfigurationsVariableFolder.Get(browseName);
                if (configurationFolder == null)
                {
                    configurationFolder = InformationModel.Make<Folder>(browseName);
                    opcUaConfigurationsVariableFolder.Add(configurationFolder);
                }
                var nodesPointer = InformationModel.MakeVariable<NodePointer>("Node1", OpcUa.DataTypes.NodeId);
                nodesPointer.Value = configurationFolder.NodeId;
                configuration.Nodes.Add(nodesPointer);
                nodeToPublishCollection.Add(configuration);
                if (InformationModel.Get(widgetOwner) is ColumnLayout verticalLayout)
                {
                    var newWidget = InformationModel.MakeObject<OPCUAServerNodesToPublishUIObj>(browseName);
                    newWidget.SetAlias(CommonLogic.editAliasNameMapping.GetValueOrDefault(FTOptix.OPCUAServer.ObjectTypes.NodesToPublishConfigurationEntry), configuration);
                    verticalLayout.Add(newWidget);
                    NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Success, $"Configuration for OPC-UA Server successfully created.");
                }
            }
        }
    }

    [ExportMethod]
    public void SaveProperties(NodeId opcUaServer, NodeId widget)
    {
        try
        {
            if (InformationModel.GetObject(opcUaServer) is OPCUAServer editStation && InformationModel.GetObject(widget) is IUAObject widgetNode)
            {
                string stationNodeAlias = CommonLogic.sourceAliasNameMapping.GetValueOrDefault(editStation.ObjectType.NodeId);
                var sourceStation = (OPCUAServer)widgetNode.GetAlias(stationNodeAlias);
                var opcUaServerFolder = Project.Current.Get<Folder>(CommonLogic.OPCUAServerFolderPath);
                if (sourceStation == null)
                {
                    sourceStation = InformationModel.Make<OPCUAServer>(editStation.BrowseName);
                    ApplyProperties(sourceStation, editStation);
                    opcUaServerFolder.Add(sourceStation);
                    widgetNode.SetAlias(stationNodeAlias, sourceStation);
                    NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Success, $"OPC-UA Server successfully created.");
                }
                else
                {
                    sourceStation.Stop();
                    ApplyProperties(sourceStation, editStation);
                    sourceStation.Start();
                    NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Success, $"Parameters for OPC-UA Server successfully saved.");
                }
                widgetNode.GetVariable("EnableAddConfiguration").Value = true;
                widgetNode.Find("StationActions").GetVariable("EnableSave").Value = false;
            }
            else
            {
                throw new NullReferenceException("Missing OPC-UA server node!");
            }
        }
        catch (Exception ex)
        {
            NotificationsMessageHandlerLogic.Instance.RequestBannerNotification(ToastBannerNotificationLevel.Error, "Critical error - Check application logs");
            Log.Error(LogicObject.BrowseName, $"{ex.Message} - Stack: {ex.StackTrace}");
        }
    }

    public void SaveConfiguration(NodeId nodesToPublish)
    {
        generateConfigurationTags = new LongRunningTask(CreateOrUpdateTask, nodesToPublish, LogicObject);
        generateConfigurationTags.Start();
    }

    #endregion

    private static void InitOpcUaServerNode(OPCUAServer opcUAServer)
    {
        opcUAServer.GetOrCreateVariable("MaxNumberOfConnections").Value = 1;
        opcUAServer.SamplingInterval = 1000;
        opcUAServer.ProductURI = "urn:Optix_DefaultApplication_OptixEdge:Application";
        opcUAServer.ProductName = "Allen-Bradley 2800E Optix_DefaultApplication_OptixEdge";
        opcUAServer.MinimumMessageSecurityMode = FTOptix.OPCUAServer.MessageSecurityMode.None;
        opcUAServer.MinimumSecurityPolicy = FTOptix.OPCUAServer.SecurityPolicy.None;
        _ = opcUAServer.UseNodePathInNodeIdsVariable;
        _ = opcUAServer.MaxArrayLengthVariable;
        _ = opcUAServer.ServerCertificateFileVariable;
        _ = opcUAServer.ServerPrivateKeyFileVariable;
        _ = opcUAServer.ManufacturerNameVariable;
    }

    private static void ApplyProperties(OPCUAServer stationNode, OPCUAServer editNode)
    {
        stationNode.EndpointURL = editNode.EndpointURL;
        stationNode.GetOrCreateVariable("MaxNumberOfConnections").Value = editNode.GetOrCreateVariable("MaxNumberOfConnections").Value;
        stationNode.UseNodePathInNodeIds = editNode.UseNodePathInNodeIds;
        stationNode.SamplingInterval = editNode.SamplingInterval;
        stationNode.MaxArrayLength = editNode.MaxArrayLength;
        stationNode.MinimumMessageSecurityMode = editNode.MinimumMessageSecurityMode;
        stationNode.MinimumSecurityPolicy = editNode.MinimumSecurityPolicy;
        stationNode.ServerCertificateFile = editNode.ServerCertificateFile;
        stationNode.ServerPrivateKeyFile = editNode.ServerPrivateKeyFile;
        stationNode.ProductURI = editNode.ProductURI;
        stationNode.ProductName = editNode.ProductName;
        stationNode.ManufacturerName = editNode.ManufacturerName;
    }

    private void CreateOrUpdateTask(LongRunningTask task, object argument)
    {
        try
        {
            var nodesToPublish = (NodeId)argument;
            if (InformationModel.GetObject(nodesToPublish) is NodesToPublishConfigurationEntry nodesToPublishNode)
            {
                var opcUAServer = (IUAObject)nodesToPublishNode.Owner.Owner;
                opcUAServer.Stop();
                CreateOrUpdateTags(nodesToPublishNode);
                opcUAServer.Start();
            }
            else
            {
                throw new NullReferenceException("Missing OPC-UA server node to publish node!");
            }
            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Success, $"Parameters for node to publish of OPC-UA Server successfully saved.");
        }
        catch (Exception ex)
        {
            NotificationsMessageHandlerLogic.Instance.RequestBannerNotification(ToastBannerNotificationLevel.Error, "Critical error - Check application logs");
            Log.Error(LogicObject.BrowseName, $"{ex.Message} - Stack: {ex.StackTrace}");
        }
        task.Dispose();
    }

    private void CreateOrUpdateTags(IUAObject configuration)
    {
        if (Project.Current.Get($"Model/{configuration.Owner.Owner.BrowseName}/{configuration.BrowseName}") is not IUAObject temporaryFolder)
        {
            NotificationsMessageHandlerLogic.Instance.RequestBannerNotification(ToastBannerNotificationLevel.Warning, $"Cannot add variables to expose for configuration {configuration.BrowseName} of OPC-UA Server - Check application logs");
            Log.Warning(LogicObject.BrowseName, $"Missing temporary folder for configuration {configuration.BrowseName}");
            return;
        }
        int createdTags = 0;
        var configurationTagFolder = Project.Current.Get<Folder>($"{CommonLogic.OPCUAServerDataFolderPath}/{configuration.Owner.Owner.BrowseName}/{configuration.BrowseName}");
        foreach (var sourceFieldFolder in temporaryFolder.GetNodesByType<Folder>())
        {
            var dataFromTagImporter = sourceFieldFolder.GetNodesByType<TagCustomGridRowData>();
            foreach (var tagData in dataFromTagImporter.Where(x => x.Checked))
            {
                string variableName = $"{sourceFieldFolder.BrowseName}.{tagData.VariableName}";
                IUAVariable targetTag = configurationTagFolder.GetVariable(variableName);
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
                    configurationTagFolder.Add(targetTag);
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
            var deletedTags = DeleteMissingTag(configuration, dataFromTagImporter.Where(x => !x.Checked).ToList());
            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Info, $"Added {createdTags}, removed {deletedTags} variables on the configuration {configuration.BrowseName} of OPC-UA Server");
        }
    }

    private int DeleteMissingTag(IUAObject configuration, List<TagCustomGridRowData> tagDatasUnchecked)
    {
        int deletedTagsCounter = 0;
        List<TagDataImported> tagsImported = CommonLogic.ReadTagsFromSourceDataCollector(Project.Current.Get($"{CommonLogic.OPCUAServerDataFolderPath}/{configuration.Owner.Owner.BrowseName}/{configuration.BrowseName}"), configuration);
        foreach (var tagData in tagsImported.Where(x => tagDatasUnchecked.Exists(y => y.VariableName == x.BrowseName)))
        {
            InformationModel.Get(tagData.NodeId)?.Delete();
            deletedTagsCounter++;
        }
        return deletedTagsCounter;
    }

    private void DeleteStationTask(DelayedTask task, object arguments)
    {
        IUANode[] nodesToDelete = (IUANode[])arguments; 
        if ((nodesToDelete[0] is OPCUAServer || nodesToDelete[0] is NodesToPublishConfigurationEntry) && nodesToDelete[1] is Item opcUAServerWidget)
        {
            OPCUAServer sourceStation = null;
            if (nodesToDelete[0] is OPCUAServer editStation)
            {
                string stationNodeAlias = CommonLogic.sourceAliasNameMapping.GetValueOrDefault(editStation.ObjectType.NodeId);
                sourceStation = (OPCUAServer)opcUAServerWidget.GetAlias(stationNodeAlias);
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
                case OPCUAServer:              
                    Project.Current.Get($"{CommonLogic.OPCUAServerDataFolderPath}/{nodesToDelete[0].BrowseName}")?.Delete();
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
                            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Success, $"OPC-UA Server {sourceStationName} successfully deleted.");
                        } 
                    }
                    catch
                    {
                        // nothing important
                    }
                    break;
                case NodesToPublishConfigurationEntry configuration:
                    try
                    {
                        var opcUaServerOwner = (OPCUAServer)configuration.Owner.Owner;
                        Project.Current.Get($"{CommonLogic.OPCUAServerDataFolderPath}/{configuration.Owner.Owner.BrowseName}/{configuration.BrowseName}")?.Delete();
                        opcUaServerOwner.Stop();
                        string configurationName = configuration.BrowseName;
                        configuration.Delete();
                        NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Success, $"Nodes to publish configuration {configurationName} of OPC-UA Server {opcUaServerOwner.BrowseName} successfully deleted.");
                        opcUaServerOwner.Start();
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
    private LongRunningTask generateConfigurationTags;
}
