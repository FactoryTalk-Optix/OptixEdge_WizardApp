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
using FTOptix.OPCUAServer;
using FTOptix.MQTTClient;
using FTOptix.DataLogger;
using FTOptix.OPCUAClient;
using FTOptix.Core;
using FTOptix.NativeUI;
using System.Linq;
#endregion

public class CommunicationStatusViewLogic : BaseNetLogic
{
    public override void Start()
    {
        dataCollection = LogicObject.GetObject("DataCollection");
        updateDataCollectionTask = new PeriodicTask(UpdateDataCollection, 2000, LogicObject);
        updateDataCollectionTask.Start();
    }

    public override void Stop()
    {
        CommonLogic.DisposeTask(updateDataCollectionTask);
    }

    private void UpdateDataCollection()
    {
        var driversFolder = Project.Current.Get<Folder>("CommDrivers");
        var mqttFolders = Project.Current.Get<Folder>(CommonLogic.MQTTClientFolderPath);
        foreach (var communicationStation in driversFolder.GetNodesByType<CommunicationDriver>()
                                                   .Where(x => x.GetNodesByType<CommunicationStation>().Any() && x.BrowseName != "WorkAroundCommHostStart")
                                                   .SelectMany(communicationDriver => communicationDriver.GetNodesByType<CommunicationStation>()))
        {
            GetOrCreateStationData(communicationStation);
        }
        foreach (var mqttClient in mqttFolders.GetNodesByType<MQTTClient>())
        {
            GetOrCreateStationData(mqttClient);
        }
        if (Owner.FindByType<DataGrid>() is DataGrid gridStatus)
        {
            gridStatus.Refresh();
        }
    }

    private void GetOrCreateStationData(IUANode communicationNode)
    {
        var stationData = dataCollection.Get<ConnectionStatusData>(communicationNode.BrowseName);
        if (stationData == null)
        {
            stationData = InformationModel.MakeObject<ConnectionStatusData>(communicationNode.BrowseName);
            dataCollection.Add(stationData);
        }
        stationData.Driver = GetDriverFromCommunicationNode(communicationNode);
        stationData.Station = communicationNode.BrowseName;
        stationData.Address = GetAddressFromCommunicationNode(communicationNode);
        stationData.Connected = GetStatusFromCommunicationNode(communicationNode);
    }

    private static string GetDriverFromCommunicationNode(IUANode communicationNode)
    {
        return communicationNode switch
        {
            FTOptix.S7TiaProfinet.Station => communicationNode.Owner.DisplayName.Text,
            FTOptix.S7TCP.Station => communicationNode.Owner.DisplayName.Text,
            FTOptix.RAEtherNetIP.Station => communicationNode.Owner.DisplayName.Text,
            FTOptix.Modbus.Station => communicationNode.Owner.DisplayName.Text,
            FTOptix.MQTTClient.MQTTClient mqttClient => "MQTT Client",
            _ => string.Empty
        };
    }

    private static string GetAddressFromCommunicationNode(IUANode communicationNode)
    {
        return communicationNode switch
        {
            FTOptix.S7TiaProfinet.Station profinetStation => profinetStation.IPAddress,
            FTOptix.S7TCP.Station s7TCPStation => s7TCPStation.IPAddress,
            FTOptix.RAEtherNetIP.Station raEthernetIp => raEthernetIp.Route,
            FTOptix.Modbus.Station modbusStation => modbusStation.IPAddress,
            FTOptix.MQTTClient.MQTTClient mqttClient => mqttClient.BrokerAddress,
            _ => string.Empty
        };
    }

    private static bool GetStatusFromCommunicationNode(IUANode communicationNode)
    {
        return communicationNode switch
        {
            FTOptix.S7TiaProfinet.Station profinetStation => profinetStation.OperationCode == CommunicationOperationCode.Connected,
            FTOptix.S7TCP.Station s7TCPStation => s7TCPStation.OperationCode == CommunicationOperationCode.Connected,
            FTOptix.RAEtherNetIP.Station raEthernetIp => raEthernetIp.OperationCode == CommunicationOperationCode.Connected,
            FTOptix.Modbus.Station modbusStation => modbusStation.OperationCode == CommunicationOperationCode.Connected,
            FTOptix.MQTTClient.MQTTClient mqttClient => mqttClient.Status == Status.Connected,
            _ => false
        };
    }

    IUAObject dataCollection;
    PeriodicTask updateDataCollectionTask;
}
