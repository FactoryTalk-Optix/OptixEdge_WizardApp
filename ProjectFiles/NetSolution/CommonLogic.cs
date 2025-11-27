#region Using directives
using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Collections.Generic;
using System.Collections.Immutable;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.UI;
using FTOptix.HMIProject;
using FTOptix.NetLogic;
using FTOptix.Modbus;
using FTOptix.CoreBase;
using FTOptix.CommunicationDriver;
using FTOptix.OPCUAClient;
using FTOptix.Core;
using FTOptix.OPCUAServer;
using FTOptix.Store;
using FTOptix.DataLogger;
using FTOptix.MQTTClient;
using L5Sharp.Core;
#endregion

public class CommonLogic : BaseNetLogic
{
    public static CommonLogic Instance { get; private set; }

    public static string OPCUAServerDataFolderPath { get => "OPC UA Data"; }

    public static string OPCUAClientDataFolderPath { get => "Model/OPCUAClient Data"; }

    public static string MQTTClientFolderPath { get => "MQTT/MQTT Clients"; }

    public static string OPCUAServerFolderPath { get => "OPC-UA/OPC-UA Server"; }

    public static string LoggersFolderPath { get => "Loggers"; }

    public static string OPCUAClientFolderPath { get => "OPC-UA/OPC-UA Clients"; }

    public static string CommDriverComboBoxElementsPath { get => "Model/ComboBoxElements/CommDriverComboBoxElements"; }

    public static string MQTTPublishersDataConfigurationPath { get => "MQTT Data Configurations"; }

    public static string TagViewerAliasName { get => "TagSourceDataCollector"; }
    public static string TagViewerCommunicationDriverAliasSourceLink { get => "{StationNode}@NodeId"; }
    public static string TagViewerOPCUAPublisherAliasSourceLink { get => "{OPCUAServerNodesToPublishAlias}@NodeId"; }
    public static string TagViewerMQTTPublisherAliasSourceLink { get => "{ConfigurationDataEditModel}@NodeId"; }


    public override void Start()
    {
        Instance = this;
        try
        {
            if (InformationModel.Get(LogicObject.GetVariable("OpcUaServerLogic").Value) is NetLogicObject opcUaServerLogic)
            {
                opcUaServerNetlogic = opcUaServerLogic;
            }
            else
            {
                throw new InvalidProgramException("Cannot found NetLogic handler of OPC-UA Server");
            }
            if (InformationModel.Get(LogicObject.GetVariable("CommDriversLogic").Value) is NetLogicObject commDriversLogic)
            {
                commDriversNetlogic = commDriversLogic;
            }
            else
            {
                throw new InvalidProgramException("Cannot found NetLogic handler of Communication Drivers");
            }
            if (InformationModel.Get(LogicObject.GetVariable("MqttClientLogic").Value) is NetLogicObject mqttClientLogic)
            {
                mqttClientNetlogic = mqttClientLogic;
            }
            else
            {
                throw new InvalidProgramException("Cannot found NetLogic handler of Mqtt Client");
            }
            if (InformationModel.Get(LogicObject.GetVariable("LoggersLogic").Value) is NetLogicObject loggersLogic)
            {
                loggersNetLogic = loggersLogic;
            }
            else
            {
                throw new InvalidProgramException("Cannot found NetLogic handler of Dataloggers");
            }
            if (InformationModel.Get(LogicObject.GetVariable("DashboardCollection").Value) is Folder dashboardCollection)
            {
                dashboardCollectionFolder = dashboardCollection;
            }
            else
            {
                throw new InvalidProgramException("Cannot found Dashboard collection folder");
            }
            CopyWPEConfiguration();
            PopulateComboBoxElements();
            eventRegistrationList = [];
            communicationDriverObserverList = [];
            foreach (var communicationDriver in Project.Current.Get("CommDrivers").GetNodesByType<CommunicationDriver>())
            {
                var affinityId = LogicObject.Context.AssignAffinityId();
                var newObserver = new CommunicationDriverObserver();
                var newEventRegistration = communicationDriver.RegisterEventObserver(newObserver, EventType.ForwardReferenceChanged, affinityId);
                communicationDriverObserverList.Add(newObserver);
                eventRegistrationList.Add(newEventRegistration);
            }
        }
        catch (Exception ex)
        {
            NotificationsMessageHandlerLogic.Instance?.RequestBannerNotification(ToastBannerNotificationLevel.Error, "Critical error - Contact technical support, see application logs.");
            Log.Error(LogicObject.BrowseName, ex.Message);
        }
    }

    public override void Stop()
    {
        Instance = null;
        foreach (var eventRegistration in eventRegistrationList)
        {
            eventRegistration.Dispose();
        }
    }

    #region Methods exposed to Optix
    [ExportMethod]
    public void SaveCommunicationProperties(NodeId aliasNode, NodeId widgetNode)
    {
        const string methodName = "SaveProperties";
        object[] inputArguments = [aliasNode, widgetNode];
        if (InformationModel.Get(aliasNode) is IUAObject communicationNode)
        {
            switch (communicationNode)
            {
                case FTOptix.CommunicationDriver.CommunicationStation:
                    commDriversNetlogic.ExecuteMethod(methodName, inputArguments);
                    break;
                case FTOptix.MQTTClient.MQTTClient:
                case FTOptix.MQTTClient.MQTTPublisher:
                    mqttClientNetlogic.ExecuteMethod(methodName, inputArguments);
                    break;
                case FTOptix.OPCUAServer.OPCUAServer:
                    opcUaServerNetlogic.ExecuteMethod(methodName, inputArguments);
                    break;
                case FTOptix.DataLogger.DataLogger:
                    loggersNetLogic.ExecuteMethod(methodName, inputArguments);
                    break;
            }
        }
    }

    [ExportMethod]
    public void DeleteDashboardWidget(NodeId widget)
    {
        if (InformationModel.Get(widget) is IUAObject widgetNode && widgetNode.IsInstanceOf(FTOptix.UI.ObjectTypes.Item))
        {
            if (widgetNode.GetAlias("WidgetData") is WidgetData retentivityWidgetNode)
            {
                try
                {
                    widgetNode.Delete();
                    retentivityWidgetNode.Delete();
                }
                catch
                {
                    // Nothing important
                }
                
            }
        }
    }

    [ExportMethod]
    public void RestoreExpandedStatus(NodeId Expanded, bool value)
    {
        if (InformationModel.Get(Expanded) is IUAVariable expandedVariable)
        {
            new DelayedTask(SetExpandedVariableValue, new object[] { expandedVariable, value }, TimeSpan.FromMilliseconds(60), LogicObject).Start();
        }
    }
    #endregion

    public static List<TagDataImported> ReadTagsFromSourceDataCollector(IUANode nodeToDiscover, IUAObject sourceDataCollector)
    {
        List<TagDataImported> returnValue = [];
        if (nodeToDiscover != null)
        {
            bool removePrefix = sourceDataCollector.IsInstanceOf(OptixEdge_WizardApp.ObjectTypes.MQTTPublisherDataConfiguration) ||
                                sourceDataCollector.IsInstanceOf(FTOptix.DataLogger.ObjectTypes.DataLogger) ||
                                sourceDataCollector.IsInstanceOf(FTOptix.OPCUAServer.ObjectTypes.NodesToPublishConfigurationEntry) ||
                                sourceDataCollector is MQTTPublisherDataConfiguration ||
                                sourceDataCollector is MQTTPayloadObject;
            foreach (var tag in nodeToDiscover.Children.Where(x => x.NodeClass == NodeClass.Variable).Cast<IUAVariable>())
            {
                DynamicLinkMode linkDirection = DynamicLinkMode.Read;
                if (tag.GetByType<DynamicLink>() is DynamicLink linkDirectionVariable)
                {
                    linkDirection = linkDirectionVariable.Mode;
                }
                var newTagData = new TagDataImported
                {
                    BrowseName = removePrefix ? RemovePrefix(tag.BrowseName, '.') : tag.BrowseName,
                    NodeId = tag.NodeId,
                    Description = tag.Description.Text,
                    DataType = tag.DataType,
                    ArrayDimensions = tag.ArrayDimensions,
                    LinkDirection = linkDirection
                };
                returnValue.Add(newTagData);
            }
            foreach (var subnode in nodeToDiscover.GetNodesByType<IUAObject>())
            {
                returnValue.AddRange(ReadTagsFromSourceDataCollector(subnode, sourceDataCollector));
            }
        }
        return returnValue;
    }

    public void GenerateConfigurationWidgetFromSource(IUAObject sourceNode, IUANode uiWidgetOwner, IUANode sourceWidgetFolder)
    {
        ColumnLayout content = uiWidgetOwner.Get<ColumnLayout>("Content/Content");
        string widgetTypeName = sourceWidgetMapping.GetValueOrDefault(sourceNode.ObjectType.NodeId);
        IUAObject newWidget = null;
        if (!string.IsNullOrEmpty(widgetTypeName))
        {
            foreach (var source in sourceNode.GetNodesByType<IUAObject>().Where(x => x is CommunicationStation || x is OPCUAClient || x is DataLogger || x is MQTTClient || x is OPCUAServer))
            {
                newWidget = GenerateConfigurationWidget(source, widgetTypeName, sourceWidgetFolder);
                content.Add(newWidget);
                var subContent = newWidget.Get<ColumnLayout>("Content/Content");
                switch (source)
                {
                    case CommunicationStation:
                        newWidget.Find("StationActions").GetVariable("EnableImport").Value = true;
                        // Generate the TagViewer object
                        GenerateAndAttachTagViewer(newWidget, TagViewerCommunicationDriverAliasSourceLink);
                        break;
                    case MQTTClient:
                        newWidget.GetVariable("EnableAddPublisher").Value = true;
                        subContent = newWidget.Find("NodesToPublish").Get<ColumnLayout>("Content/Content");
                        foreach (var publisher in source.GetNodesByType<MQTTPublisher>())
                        {
                            var newSubWidget = GenerateConfigurationWidget(publisher, sourceWidgetMapping.GetValueOrDefault(source.ObjectType.NodeId), sourceWidgetFolder);
                            subContent.Add(newSubWidget);
                            GenerateAndAttachTagViewer(newSubWidget, TagViewerMQTTPublisherAliasSourceLink);
                            newSubWidget.Find("StationActions").GetVariable("EnableImport").Value = true;
                            MqttClientLogic.GeneratePayloadWidget(newSubWidget.GetAlias(CommonLogic.editAliasNameMapping.GetValueOrDefault(OptixEdge_WizardApp.ObjectTypes.MQTTPublisherUIObj)), newSubWidget.Find("Payload").Get<ColumnLayout>("Content/Content"), newSubWidget.Find<IUAVariable>("CurrentSelectedField"), newSubWidget.Find<IUAVariable>("LastIndexReleased"));
                        }
                        break;
                    case DataLogger logger:
                        newWidget.FindByType<StationProps>().GetVariable("EnableImport").Value = true;
                        if (InformationModel.GetObject(logger.GetVariable("Store").Value) is Store loggerStore && loggerStore.Tables.Get(logger.BrowseName) is Table loggerStoreTable)
                        {
                            newWidget.GetVariable("TableRecordLimits").Value = loggerStoreTable.RecordLimit;
                        }
                        // Generate the TagViewer object
                        GenerateAndAttachTagViewer(newWidget, TagViewerCommunicationDriverAliasSourceLink);
                        break;
                    case OPCUAServer:
                        newWidget.GetVariable("EnableAddConfiguration").Value = true;
                        subContent = newWidget.Find("NodesToPublish").Get<ColumnLayout>("Content/Content");
                        foreach (var configuration in source.GetObject("NodesToPublish").GetNodesByType<NodesToPublishConfigurationEntry>())
                        {
                            var subWidget = GenerateSubConfigurationWidget(configuration, source.ObjectType.NodeId, sourceWidgetFolder, subContent);
                            GenerateAndAttachTagViewer(subWidget, TagViewerOPCUAPublisherAliasSourceLink);
                        }
                        break;
                }
            }
        }
    }

    public static void GenerateAndAttachTagViewer(IUAObject newWidget, string linkToSourceValue)
    {
        if (newWidget.Find<Accordion>("TagImported") is Accordion accordionTagViewer)
        {
            var tagViewer = InformationModel.MakeObject<TagViewer>("TagViewer");
            var linkToSource = InformationModel.MakeVariable<DynamicLink>("DynamicLink", FTOptix.Core.DataTypes.NodePath);
            linkToSource.Mode = DynamicLinkMode.ReadWrite;
            linkToSource.Value = linkToSourceValue;
            tagViewer.GetVariable(TagViewerAliasName).Refs.AddReference(FTOptix.CoreBase.ReferenceTypes.HasDynamicLink, linkToSource.NodeId);
            accordionTagViewer.Content.Add(tagViewer);
        }
    }

    public static string RemovePrefix(string input, char divider)
    {
        int dividerIndex = input.IndexOf(divider);
        if (dividerIndex >= 0)
        {
            return input.Substring(dividerIndex + 1);
        }
        return input; // If there is no divider, it returns the original string
    }

    public static int FindMissingNumber(List<int> sequence)
    {
        if (sequence == null || sequence.Count == 0)
        {
            return 1;
        }
        sequence.Sort();
        for (int i = 0; i < sequence.Count - 1; i++)
        {
            if (sequence[i + 1] != sequence[i] + 1)
            {
                return sequence[i] + 1;
            }
        }
        return sequence[^1] + 1;
    }

    public static void DisposeTask(BaseTaskWrapper taskToClose)
    {
        try
        {
            taskToClose?.Cancel();
        }
        catch
        {
            // Task not running
        }
        taskToClose?.Dispose();
    }

    public static void PopulateComboBoxElements()
    {
        var commDriverComboBoxElementsFolder = Project.Current.Get<Folder>(CommDriverComboBoxElementsPath);
        foreach (var communicationDriver in Project.Current.Get("CommDrivers").GetNodesByType<CommunicationDriver>().Where(x => x.BrowseName != "WorkAroundCommHostStart"))
        {
            if (communicationFolderComboBoxMapping.GetValueOrDefault(communicationDriver.ObjectType.NodeId, null) is string folderName)
            {
                if (communicationDriver is FTOptix.Modbus.Driver modbusDriver)
                {
                    folderName = modbusDriver.Protocol == ModbusProtocol.ModbusTCPProtocol ? "ModbusTCP" : "ModbusRTU";
                }
                if (commDriverComboBoxElementsFolder.Get(folderName) is Folder driverFolder)
                {
                    driverFolder.Children.Clear();
                    foreach (var station in communicationDriver.GetNodesByType<CommunicationStation>())
                    {
                        var comboBoxElement = InformationModel.MakeObject<ComboBoxStationData>(station.BrowseName);
                        comboBoxElement.TagsFolder = station.Get("Tags").NodeId;
                        comboBoxElement.StationNodeId = station.NodeId;
                        comboBoxElement.StationName = station.BrowseName;
                        driverFolder.Add(comboBoxElement);
                    }
                }

            }
        }
    }

    public static bool IsPortInUse(uint port)
    {
        try
        {
            IPGlobalProperties ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();

            // Check TCP listeners
            IPEndPoint[] tcpListeners = ipGlobalProperties.GetActiveTcpListeners();
            bool isInUse = tcpListeners.Any(listener => listener.Port == port);

            return isInUse;
        }
        catch (Exception ex)
        {
            Log.Error("System", $"Error checking port {port}: {ex.Message}");
            return false;
        }
    }

    public static bool IsValidTcpPort(uint port)
    {
        // TCP ports range from 1 to 65535
        return port >= 1 && port <= 65535;
    }

    public static bool IsValidIPv4Address(string ipAddress)
    {
        if (!IPAddress.TryParse(ipAddress, out IPAddress parsedIPAddress))
        {
            return false;
        }
        return parsedIPAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;
    }

    public static bool IsLoopbackAddress(string ipAddress)
    {
        try
        {
            return IPAddress.IsLoopback(IPAddress.Parse(ipAddress));
        }
        catch
        {
            return false;
        }
    }

    public static bool IsAnyIpAddress(string ipAddress)
    {
        try
        {
            return IPAddress.Any.Equals(IPAddress.Parse(ipAddress));
        }
        catch
        {
            return false;
        }
    }

    public static bool IsIPAddressAssignedToSystem(string ipAddress)
    {
        try
        {
            var parsedIPAddress = IPAddress.Parse(ipAddress);
            NetworkInterface[] networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (NetworkInterface networkInterface in networkInterfaces)
            {
                IPInterfaceProperties ipProperties = networkInterface.GetIPProperties();
                foreach (UnicastIPAddressInformation unicastAddress in ipProperties.UnicastAddresses.Where(x => x.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork))
                {
                    if (unicastAddress.Address.Equals(parsedIPAddress))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        catch (Exception ex)
        {
            Log.Error("System", $"Error checking network interfaces: {ex.Message}");
            return false;
        }
    }

    public static string MakeBrowsePath(IUANode node, IUANode nodeToEscape = null)
    {
        string path = node.BrowseName;
        IUANode current = node.Owner;

        while (current != nodeToEscape)
        {
            path = $"{current.BrowseName}/{path}";
            current = current.Owner;
        }
        return path;
    }

    public static IUANode GetOwner(IUANode node, NodeId ownerTypeNodeId)
    {
        IUANode current = node.Owner;
        while (current != null)
        {
            switch (current)
            {
                case IUAObject objectNode:
                    if (objectNode.ObjectType.IsSubTypeOf(ownerTypeNodeId))
                    {
                        return current;
                    }
                    break;
                case IUAVariable variableNode:
                    if (variableNode.VariableType.IsSubTypeOf(ownerTypeNodeId))
                    {
                        return current;
                    }
                    break;
            }
            current = current.Owner;
        }
        return null;
    }

    public static IUANode GetOwner(IUANode node, string browseName)
    {
        IUANode current = node.Owner;
        while (current != null)
        {
            switch (current)
            {
                case IUAObject objectNode:
                    if (objectNode.BrowseName == browseName)
                    {
                        return current;
                    }
                    break;
                case IUAVariable variableNode:
                    if (variableNode.BrowseName == browseName)
                    {
                        return current;
                    }
                    break;
            }
            current = current.Owner;
        }
        return null;
    }

    public static IUANode GetOwner(IUANode node, string browseName, NodeId ownerTypeNodeId)
    {
        IUANode current = node.Owner;
        while (current != null)
        {
            switch (current)
            {
                case IUAObject objectNode:
                    if (objectNode.ObjectType.IsSubTypeOf(ownerTypeNodeId) && objectNode.BrowseName == browseName)
                    {
                        return current;
                    }
                    break;
                case IUAVariable variableNode:
                    if (variableNode.VariableType.IsSubTypeOf(ownerTypeNodeId) && variableNode.BrowseName == browseName)
                    {
                        return current;
                    }
                    break;
            }
            current = current.Owner;
        }
        return null;
    }

    private IUAObject GenerateConfigurationWidget(IUANode widgetSourceNode, string widgetTypeName, IUANode widgetTypeFolder)
    {
        NodeId widgetSourceType = NodeId.Empty;
        switch (widgetSourceNode)
        {
            case IUAObject objectNode:
                widgetSourceType = objectNode.ObjectType.NodeId;
                break;
            case IUAVariable variableNode:
                widgetSourceType = variableNode.VariableType.NodeId;
                break;
        }
        string sourceAliasName = sourceAliasNameMapping.GetValueOrDefault(widgetSourceType);
        NodeId widgetType = widgetTypeFolder.Find(widgetTypeName).NodeId;
        var newWidget = InformationModel.MakeObject(widgetSourceNode.BrowseName, widgetType);
        newWidget.SetAlias(sourceAliasName, widgetSourceNode);
        switch (widgetSourceNode)
        {
            case CommunicationStation:
            case DataLogger:
            case OPCUAServer:
            case MQTTClient:
            case MQTTPublisher:
                var editStation = LogicObject.Context.NodeFactory.CloneNode(widgetSourceNode, widgetSourceNode.NodeId.NamespaceIndex, NamingRuleType.None);
                string editAliasName = editAliasNameMapping.GetValueOrDefault(widgetSourceType);
                newWidget.SetAlias(editAliasName, editStation);
                if (widgetSourceNode is MQTTPublisher publisher)
                {
                    ConfigureMQTTPublisherDataConfiguration(publisher, newWidget);
                }
                break;
        }

        return newWidget;
    }

    private void ConfigureMQTTPublisherDataConfiguration(MQTTPublisher publisher, IUAObject widgetNode)
    {
        var configurationData = Project.Current.Get<MQTTPublisherDataConfiguration>($"{MQTTPublishersDataConfigurationPath}/{publisher.Owner.BrowseName}_{publisher.BrowseName}");
        //Only for mantain compability with configuration of 1.1.7 (need to generate MQTTPublisherDataConfiguration)
        if (configurationData == null)
        {
            configurationData = InformationModel.Make<MQTTPublisherDataConfiguration>($"{publisher.Owner.BrowseName}_{publisher.BrowseName}");
            configurationData.MQTTPublisherNode = publisher.NodeId;
            Project.Current.Get(MQTTPublishersDataConfigurationPath).Add(configurationData);
        }
        var configurationDataEditModel = LogicObject.Context.NodeFactory.CloneNode(configurationData, configurationData.NodeId.NamespaceIndex, NamingRuleType.None);
        widgetNode.SetAlias(CommonLogic.editAliasNameMapping.GetValueOrDefault(OptixEdge_WizardApp.ObjectTypes.MQTTPublisherUIObj), configurationDataEditModel);
    }

    private IUAObject GenerateSubConfigurationWidget(IUANode subWidgetSourceNode, NodeId sourceObjectType, IUANode sourceWidgetFolder, IUANode widgetContent)
    {
        string subWidgetTypeName = sourceWidgetMapping.GetValueOrDefault(sourceObjectType);
        var newSubWidget = GenerateConfigurationWidget(subWidgetSourceNode, subWidgetTypeName, sourceWidgetFolder);
        widgetContent.Add(newSubWidget);
        return newSubWidget;
    }

    private void CopyWPEConfiguration()
    {
        if (InformationModel.Get(LogicObject.GetVariable("WPEConfiguration").Value) is WPEConfiguration wpeConfiguration && InformationModel.Get(LogicObject.GetVariable("WPEConfigurationOnStartup").Value) is WPEConfiguration wpeConfigurationOnStartup)
        {
            wpeConfigurationOnStartup.Port = wpeConfiguration.Port;
            wpeConfigurationOnStartup.IPAddress = wpeConfiguration.IPAddress;
            wpeConfigurationOnStartup.Hostname = wpeConfiguration.Hostname;
            wpeConfigurationOnStartup.CertificateFile = wpeConfiguration.CertificateFile;
            wpeConfigurationOnStartup.PrivateKey = wpeConfiguration.PrivateKey;
        }
    }

    private void SetExpandedVariableValue(DelayedTask task, object arguments)
    {
        object[] inputArguments = (object[])arguments;
        IUAVariable expandedVariable = (IUAVariable)inputArguments[0];
        bool value = (bool)inputArguments[1];
        expandedVariable.Value = value;
    }

    #region Public dictionary
    public static readonly ImmutableDictionary<NodeId, string> editAliasNameMapping = ImmutableDictionary.CreateRange(
        [
            KeyValuePair.Create(FTOptix.S7TiaProfinet.ObjectTypes.Station, "EditModel"),
            KeyValuePair.Create(FTOptix.S7TCP.ObjectTypes.Station, "EditModel"),
            KeyValuePair.Create(FTOptix.RAEtherNetIP.ObjectTypes.Station, "EditModel"),
            KeyValuePair.Create(FTOptix.Modbus.ObjectTypes.Station, "EditModel"),
            KeyValuePair.Create(FTOptix.MQTTClient.ObjectTypes.MQTTClient,"EditModel"),
            KeyValuePair.Create(FTOptix.DataLogger.ObjectTypes.DataLogger, "EditModel"),
            KeyValuePair.Create(FTOptix.OPCUAServer.ObjectTypes.OPCUAServer, "EditModel"),
            KeyValuePair.Create(FTOptix.MQTTClient.ObjectTypes.MQTTPublisher, "EditModel"),
            KeyValuePair.Create(FTOptix.OPCUAServer.ObjectTypes.NodesToPublishConfigurationEntry, "OPCUAServerNodesToPublishAlias"),
            KeyValuePair.Create(OptixEdge_WizardApp.ObjectTypes.DataGridUIObjConfig, "DataGridUIObjAlias"),
            KeyValuePair.Create(OptixEdge_WizardApp.ObjectTypes.TrendUIObjConfig, "TrendUIObjAlias"),
            KeyValuePair.Create(OptixEdge_WizardApp.ObjectTypes.MQTTPublisherUIObj, "ConfigurationDataEditModel"),
            KeyValuePair.Create(OptixEdge_WizardApp.ObjectTypes.MQTTPayloadFieldBase, "FieldData"),
            KeyValuePair.Create(OptixEdge_WizardApp.ObjectTypes.MQTTPayloadObject, "ObjectData"),
            KeyValuePair.Create(OptixEdge_WizardApp.ObjectTypes.MQTTPayloadInfoEdit, "MQTTPayloadFieldUI"),
            KeyValuePair.Create(OptixEdge_WizardApp.ObjectTypes.ArrayEditor, "ArrayVariableToEdit"),
            KeyValuePair.Create(OptixEdge_WizardApp.ObjectTypes.ArrayFieldWithEditor, "ArrayVariableToDisplay"),
            KeyValuePair.Create(OptixEdge_WizardApp.ObjectTypes.MQTTPayloadAdd, "AccordionContainer"),
            KeyValuePair.Create(OptixEdge_WizardApp.ObjectTypes.TagViewer, "TagSourceDataCollector"),
        ]);

    public static readonly ImmutableDictionary<NodeId, string> sourceAliasNameMapping = ImmutableDictionary.CreateRange(
        [
            KeyValuePair.Create(FTOptix.S7TiaProfinet.ObjectTypes.Station, "StationNode"),
            KeyValuePair.Create(FTOptix.S7TCP.ObjectTypes.Station, "StationNode"),
            KeyValuePair.Create(FTOptix.RAEtherNetIP.ObjectTypes.Station, "StationNode"),
            KeyValuePair.Create(FTOptix.Modbus.ObjectTypes.Station, "StationNode"),
            KeyValuePair.Create(FTOptix.MQTTClient.ObjectTypes.MQTTClient,"StationNode"),
            KeyValuePair.Create(FTOptix.DataLogger.ObjectTypes.DataLogger, "StationNode"),
            KeyValuePair.Create(FTOptix.OPCUAServer.ObjectTypes.OPCUAServer, "StationNode"),
            KeyValuePair.Create(FTOptix.MQTTClient.ObjectTypes.MQTTPublisher, "StationNode"),
            KeyValuePair.Create(FTOptix.OPCUAServer.ObjectTypes.NodesToPublishConfigurationEntry, "OPCUAServerNodesToPublishAlias"),
            KeyValuePair.Create(OptixEdge_WizardApp.ObjectTypes.DataGridUIObjConfig, "DataGridUIObjAlias"),
            KeyValuePair.Create(OptixEdge_WizardApp.ObjectTypes.TrendUIObjConfig, "TrendUIObjAlias"),
            KeyValuePair.Create(OptixEdge_WizardApp.ObjectTypes.MQTTPublisherUIObj, "ConfigurationDataEditModel"),
            KeyValuePair.Create(OptixEdge_WizardApp.ObjectTypes.MQTTPayloadFieldBase, "FieldData"),
            KeyValuePair.Create(OptixEdge_WizardApp.ObjectTypes.MQTTPayloadObject, "ObjectData"),
            KeyValuePair.Create(OptixEdge_WizardApp.ObjectTypes.MQTTPayloadInfoEdit, "MQTTPayloadFieldUI"),
            KeyValuePair.Create(OptixEdge_WizardApp.ObjectTypes.ArrayEditor, "ArrayVariableToEdit"),
            KeyValuePair.Create(OptixEdge_WizardApp.ObjectTypes.ArrayFieldWithEditor, "ArrayVariableToDisplay"),
            KeyValuePair.Create(OptixEdge_WizardApp.ObjectTypes.MQTTPayloadAdd, "AccordionContainer"),
            KeyValuePair.Create(OptixEdge_WizardApp.ObjectTypes.TagViewer, "TagSourceDataCollector"),
        ]);

    public static readonly ImmutableDictionary<NodeId, string> sourceWidgetMapping = ImmutableDictionary.CreateRange(
        [
            KeyValuePair.Create(FTOptix.S7TiaProfinet.ObjectTypes.Driver, nameof(S7TIAProfinetStationUIObject)),
            KeyValuePair.Create(FTOptix.S7TCP.ObjectTypes.Driver, nameof(S7TCPStationUIObject)),
            KeyValuePair.Create(FTOptix.RAEtherNetIP.ObjectTypes.Driver, nameof(RAEthernetIPStationUIObject)),
            KeyValuePair.Create(FTOptix.Modbus.ObjectTypes.Driver, nameof(ModbusStationUIObject)),
            KeyValuePair.Create(FTOptix.HMIProject.ObjectTypes.MQTTCategoryFolder, nameof(MQTTClientUIObj)),
            KeyValuePair.Create(FTOptix.MQTTClient.ObjectTypes.MQTTClient, nameof(MQTTPublisherUIObj)),
            KeyValuePair.Create(FTOptix.HMIProject.ObjectTypes.LoggersCategoryFolder, nameof(DataloggerUIObj)),
            KeyValuePair.Create(FTOptix.HMIProject.ObjectTypes.OPCUACategoryFolder, nameof(OPCUAServerStationUIObj)),
            KeyValuePair.Create(FTOptix.OPCUAServer.ObjectTypes.OPCUAServer, nameof(OPCUAServerNodesToPublishUIObj)),
            KeyValuePair.Create(FTOptix.UI.ObjectTypes.DataGrid, nameof(DataGridUIObj)),
            KeyValuePair.Create(FTOptix.UI.ObjectTypes.Trend, nameof(TrendUIObj)),
        ]);

    public static readonly ImmutableDictionary<NodeId, NodeId> stationKindCommunicationDriverMapping = ImmutableDictionary.CreateRange(
        [
            KeyValuePair.Create(FTOptix.S7TiaProfinet.ObjectTypes.Station, FTOptix.S7TiaProfinet.ObjectTypes.Driver),
            KeyValuePair.Create(FTOptix.S7TCP.ObjectTypes.Station, FTOptix.S7TCP.ObjectTypes.Driver),
            KeyValuePair.Create(FTOptix.RAEtherNetIP.ObjectTypes.Station, FTOptix.RAEtherNetIP.ObjectTypes.Driver),
            KeyValuePair.Create(FTOptix.Modbus.ObjectTypes.Station, FTOptix.Modbus.ObjectTypes.Driver),
        ]);

    public static readonly ImmutableDictionary<NodeId, string> communicationFolderComboBoxMapping = ImmutableDictionary.CreateRange(
       [
           KeyValuePair.Create(FTOptix.S7TiaProfinet.ObjectTypes.Driver, "S7Profinet"),
            KeyValuePair.Create(FTOptix.S7TCP.ObjectTypes.Driver, "S7TCP"),
            KeyValuePair.Create(FTOptix.RAEtherNetIP.ObjectTypes.Driver, "RAEIP"),
            KeyValuePair.Create(FTOptix.Modbus.ObjectTypes.Driver, "ModbusTCP_ModbusRTU"),
        ]);

    public static readonly ImmutableDictionary<string, NodeId> LogixDataTypeMapping = ImmutableDictionary.CreateRange(
        [
            KeyValuePair.Create("BOOL", OpcUa.DataTypes.Boolean),
            KeyValuePair.Create("SINT", OpcUa.DataTypes.SByte),
            KeyValuePair.Create("INT", OpcUa.DataTypes.Int16),
            KeyValuePair.Create("DINT", OpcUa.DataTypes.Int32),
            KeyValuePair.Create("LINT", OpcUa.DataTypes.Int64),
            KeyValuePair.Create("USINT", OpcUa.DataTypes.Byte),
            KeyValuePair.Create("UINT", OpcUa.DataTypes.UInt16),
            KeyValuePair.Create("UDINT", OpcUa.DataTypes.UInt32),
            KeyValuePair.Create("ULINT", OpcUa.DataTypes.UInt64),
            KeyValuePair.Create("REAL", OpcUa.DataTypes.Float),
            KeyValuePair.Create("LREAL", OpcUa.DataTypes.Double),
            KeyValuePair.Create("STRING", OpcUa.DataTypes.String),
            KeyValuePair.Create("DT", OpcUa.DataTypes.DateTime),
            KeyValuePair.Create("DLT", OpcUa.DataTypes.DateTime),
        ]);

    public static readonly ImmutableDictionary<string, NodeId> CSVDriverMapping = ImmutableDictionary.CreateRange(
        [
            KeyValuePair.Create("S7TiaProfinet", FTOptix.S7TiaProfinet.ObjectTypes.Driver),
            KeyValuePair.Create("S7TCP", FTOptix.S7TCP.ObjectTypes.Driver),
            KeyValuePair.Create("RAEIP", FTOptix.RAEtherNetIP.ObjectTypes.Driver),
            KeyValuePair.Create("FX3U", FTOptix.MelsecFX3U.ObjectTypes.Driver),
            KeyValuePair.Create("MELQ", FTOptix.MelsecQ.ObjectTypes.Driver),
            KeyValuePair.Create("MODBUS", FTOptix.Modbus.ObjectTypes.Driver),
        ]);

    public static readonly ImmutableDictionary<string, NodeId> CSVDataTypeMapping = ImmutableDictionary.CreateRange(
        [
            KeyValuePair.Create("Boolean", OpcUa.DataTypes.Boolean),
            KeyValuePair.Create("SByte", OpcUa.DataTypes.SByte),
            KeyValuePair.Create("Int16", OpcUa.DataTypes.Int16),
            KeyValuePair.Create("Int32", OpcUa.DataTypes.Int32),
            KeyValuePair.Create("Int64", OpcUa.DataTypes.Int64),
            KeyValuePair.Create("Byte", OpcUa.DataTypes.Byte),
            KeyValuePair.Create("UInt16", OpcUa.DataTypes.UInt16),
            KeyValuePair.Create("UInt32", OpcUa.DataTypes.UInt32),
            KeyValuePair.Create("UInt64", OpcUa.DataTypes.UInt64),
            KeyValuePair.Create("Float", OpcUa.DataTypes.Float),
            KeyValuePair.Create("Double", OpcUa.DataTypes.Double),
            KeyValuePair.Create("String", OpcUa.DataTypes.String),
            KeyValuePair.Create("DateTime", OpcUa.DataTypes.DateTime),
            KeyValuePair.Create("Duration", OpcUa.DataTypes.Duration),
            KeyValuePair.Create("UtcTime", OpcUa.DataTypes.UtcTime),
        ]);
    #endregion

    private NetLogicObject opcUaServerNetlogic;
    private NetLogicObject commDriversNetlogic;
    private NetLogicObject mqttClientNetlogic;
    private NetLogicObject loggersNetLogic;
    private Folder dashboardCollectionFolder;
    private List<CommunicationDriverObserver> communicationDriverObserverList;
    private List<IEventRegistration> eventRegistrationList;
}

public class TagDataImported
{
    public string BrowseName { get; set; }
    public NodeId NodeId { get; set; }
    public NodeId DataType { get; set; }
    public string Description { get; set; }
    public uint[] ArrayDimensions { get; set; }
    public DynamicLinkMode LinkDirection { get; set; } = DynamicLinkMode.Read;
}

public class TagDataFromCSV
{
    public string Driver { get; set; }
    public string Name { get; set; }
    public string DataType { get; set; }
    public string Address { get; set; }
    public string ArrayDimension { get; set; }
    public string StringLength { get; set; }
    public string Description { get; set; }
}

public record InternalTagCustomGridRowData
{
    public string BrowseName { get; set; } = string.Empty;
    public bool Checked { get; set; }
    public string VariableName { get; set; }  = string.Empty;
    public string VariableDataType { get; set; }  = string.Empty;
    public NodeId VariableDataTypeNodeId { get; set; } = NodeId.Empty;
    public string VariableAddress { get; set; }  = string.Empty;
    public string VariableComment { get; set; }  = string.Empty;
    public bool VariableIsArray { get; set; }
    public uint[] VariableArrayDimension { get; set; } = [];
    public ushort VariableStringLength { get; set; }
    public NodeId VariableNodeId { get; set; } = NodeId.Empty;
    public DynamicLinkMode VariableLinkDirection { get; set; }
}

public enum ConfirmOverwriteFileResult
{
    NoEntry = 0,
    Confirmed = 1,
    Cancelled = 99
}

public class CommunicationDriverObserver() : IReferenceObserver
{
    public void OnReferenceAdded(IUANode sourceNode, IUANode targetNode, NodeId referenceTypeId, ulong senderId)
    {
        CommonLogic.PopulateComboBoxElements();
    }

    public void OnReferenceRemoved(IUANode sourceNode, IUANode targetNode, NodeId referenceTypeId, ulong senderId)
    {
        CommonLogic.PopulateComboBoxElements();
    }
}
