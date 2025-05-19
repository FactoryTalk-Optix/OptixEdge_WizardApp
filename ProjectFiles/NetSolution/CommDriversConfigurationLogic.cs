#region Using directives
using System;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.HMIProject;
using FTOptix.Retentivity;
using FTOptix.UI;
using FTOptix.NativeUI;
using FTOptix.CoreBase;
using FTOptix.Core;
using FTOptix.NetLogic;
using FTOptix.CommunicationDriver;
using FTOptix.Modbus;
using FTOptix.MelsecFX3U;
using FTOptix.S7TCP;
using FTOptix.OmronEthernetIP;
using FTOptix.MelsecQ;
using FTOptix.S7TiaProfinet;
using FTOptix.OmronFins;
using FTOptix.CODESYS;
using FTOptix.TwinCAT;
using FTOptix.RAEtherNetIP;
using FTOptix.MicroController;
using FTOptix.OPCUAClient;
using FTOptix.OPCUAServer;
using FTOptix.WebUI;
using FTOptix.SQLiteStore;
using FTOptix.Store;
using FTOptix.DataLogger;
using FTOptix.MQTTBroker;
using FTOptix.Recipe;
using System.Net;
using System.Threading;
using System.Linq;
using System.Security;
using System.Collections.Generic;
using static System.Collections.Specialized.BitVector32;
using System.Reflection;
using L5Sharp.Core;
using FTOptix.System;
using FTOptix.InfluxDBStore;
using FTOptix.InfluxDBStoreLocal;
using FTOptix.ODBCStore;
using FTOptix.InfluxDBStoreRemote;
using System.Text.RegularExpressions;
using System.Collections.Immutable;
using FTOptix.AuditSigning;
using System.IO;
using System.Diagnostics;
#endregion

public class CommDriversConfigurationLogic : BaseNetLogic
{

    public override void Start()
    {
        RemoveDefaultRAEIPStation();
        rebrowseAllProfinetStationTask = new LongRunningTask(RebrowseAllProfinetStation, LogicObject);
        rebrowseAllProfinetStationTask.Start();
    }

    public override void Stop()
    {
        CommonLogic.DisposeTask(rebrowseAllProfinetStationTask);
    }

    #region Methods exposed to Optix

    [ExportMethod]
    public void CreateNewCommunicationStation(NodeId destinationDriver, NodeId widgetOwner)
    {
        CommunicationStation newStation = null;
        Item newWidget = null;
        var communicationDriver = InformationModel.Get(destinationDriver);
        var widgetContainer = InformationModel.Get(widgetOwner) as ColumnLayout;
        int countCurrentClient = communicationDriver.GetNodesByType<CommunicationStation>().Count();
        string browseName = string.Empty;
        switch (InformationModel.Get(destinationDriver))
        {
            case FTOptix.Modbus.Driver modbusDriver:
                browseName = $"ModbusStation{countCurrentClient + 1}";
                newStation = InformationModel.MakeObject<FTOptix.Modbus.Station>(browseName);
                InitModbusStationProperties(newStation as FTOptix.Modbus.Station, modbusDriver);
                newWidget = InformationModel.MakeObject<ModbusStationUIObject>(browseName);
                break;
            case FTOptix.MelsecFX3U.Driver:
                browseName = $"MelsecFX3U{countCurrentClient + 1}";
                newStation = InformationModel.MakeObject<FTOptix.MelsecFX3U.Station>(browseName);
                newWidget = null;
                break;
            case FTOptix.S7TCP.Driver:
                browseName = $"S7TCP{countCurrentClient + 1}";
                newStation = InformationModel.MakeObject<FTOptix.S7TCP.Station>(browseName);
                InitS7TCPStationProperties(newStation as FTOptix.S7TCP.Station);
                newWidget = InformationModel.MakeObject<S7TCPStationUIObject>(browseName);
                break;
            case FTOptix.RAEtherNetIP.Driver:
                browseName = $"RAEtherNetIP{countCurrentClient + 1}";
                newStation = InformationModel.MakeObject<FTOptix.RAEtherNetIP.Station>(browseName);
                InitRAEIPStationProperties(newStation as FTOptix.RAEtherNetIP.Station);
                newWidget = InformationModel.MakeObject<RAEthernetIPStationUIObject>(browseName);
                break;
            case FTOptix.S7TiaProfinet.Driver:
                browseName = $"S7TiaProfinet{countCurrentClient + 1}";
                newStation = InformationModel.MakeObject<FTOptix.S7TiaProfinet.Station>(browseName);
                InitS7ProfinetStationProperties(newStation as FTOptix.S7TiaProfinet.Station);
                newWidget = InformationModel.MakeObject<S7TIAProfinetStationUIObject>(browseName);
                break;
        }
        if (newStation != null && newWidget != null)
        {
            string aliasName = CommonLogic.editAliasNameMapping.GetValueOrDefault(newStation.ObjectType.NodeId);
            newWidget.SetAlias(aliasName, newStation);            
            widgetContainer.Add(newWidget);
            newWidget.FindByType<StationProps>().GetVariable("EnableSave").Value = true;
            newWidget.FindByType<StationProps>().GetVariable("EnableImport").Value = false;
            if (Project.Current.Get<Folder>($"Model/{browseName}") == null && !string.IsNullOrEmpty(browseName))
            {
                Project.Current.Get<Folder>("Model").Add(InformationModel.MakeObject<Folder>(browseName));
            }
        }
    }

    [ExportMethod]
    public void DeleteStation(NodeId station, NodeId widget)
    {
        IUANode[] taskArguments = [InformationModel.Get(station),InformationModel.Get(widget)];
        removeClientTask = new DelayedTask(DeleteStationTask, taskArguments, 100, LogicObject);
        removeClientTask.Start();
    }

    [ExportMethod]
    public void SaveProperties(NodeId station, NodeId widget)
    {
        try
        {
            if (InformationModel.GetObject(station) is CommunicationStation editStation && InformationModel.GetObject(widget) is IUAObject widgetNode)
            {
                string stationNodeAlias = CommonLogic.sourceAliasNameMapping.GetValueOrDefault(editStation.ObjectType.NodeId);            
                var sourceStation = (CommunicationStation)widgetNode.GetAlias(stationNodeAlias);
                NodeId communicationDriver = CommonLogic.stationKindCommunicationDriverMapping.GetValueOrDefault(editStation.ObjectType.NodeId);
                var communicationDriverNode = Project.Current.GetObject("CommDrivers").GetNodesByType<IUAObject>().FirstOrDefault(x => x.IsInstanceOf(communicationDriver));
                if (communicationDriverNode?.IsInstanceOf(FTOptix.CommunicationDriver.ObjectTypes.CommunicationDriver) == true)
                {
                    if (sourceStation == null)
                    {      
                        string editNodeAlias = CommonLogic.editAliasNameMapping.GetValueOrDefault(editStation.ObjectType.NodeId);            
                        sourceStation = (CommunicationStation)InformationModel.MakeObject(editStation.BrowseName, editStation.ObjectType.NodeId);
                        ApplyStationParametersValues(sourceStation, editStation, communicationDriverNode);
                        SetRuntimeTagImport(communicationDriverNode, true);
                        communicationDriverNode.Add(sourceStation);
                        widgetNode.SetAlias(stationNodeAlias, sourceStation);
                        NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Success, GetNewStationMessageForToast(communicationDriverNode));
                    }
                    else
                    {
                        sourceStation.Stop();
                        ApplyStationParametersValues(sourceStation, editStation, communicationDriverNode);
                        sourceStation.Start();
                        NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Success, $"Parameters for station {sourceStation.BrowseName} successfully saved.");
                    } 
                    widgetNode.Find("StationActions").GetVariable("EnableImport").Value = true;
                    widgetNode.Find("StationActions").GetVariable("EnableSave").Value = false;            
                }
                else
                {
                    throw new InvalidCastException("Communication driver node is not an instance of CommunicationDriver");
                }
            }
            else
            {
                throw new NullReferenceException("Missing station or widget node!");
            }
        }
        catch (Exception ex)
        {
            NotificationsMessageHandlerLogic.Instance.RequestBannerNotification(ToastBannerNotificationLevel.Error, "Critical error - Check application logs");
            Log.Error(LogicObject.BrowseName, $"{ex.Message} - Stack: {ex.StackTrace}");
        }
    }

    #endregion

    private void DeleteStationTask(DelayedTask task, object arguments)
    {
        IUANode[] listOfNodes = (IUANode[]) arguments;
        if (listOfNodes[0] is FTOptix.CommunicationDriver.CommunicationStation editStation && listOfNodes[1] is Item plcWidget)
        {
            string stationNodeAlias = CommonLogic.sourceAliasNameMapping.GetValueOrDefault(editStation.ObjectType.NodeId);         
            var sourceStation = (CommunicationStation)plcWidget.GetAlias(stationNodeAlias);            
            var sourceCommunicationDriver = sourceStation?.Owner;   
            try
            {
                    plcWidget.Delete();
            }
            catch
            {
                // nothing important
            }
            try
            {
                    editStation.Delete();
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
                    NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Success, $"Station {sourceStationName} successfully deleted.");
                }                    
            }
            catch
            {
                // nothing important
            }
            if (sourceCommunicationDriver is IUAObject communicationDriver && !communicationDriver.GetNodesByType<CommunicationStation>().Any())
            {
                SetRuntimeTagImport(communicationDriver, false);
            }            
        }      
        removeClientTask.Dispose();
        removeClientTask = null;
    }

    private static string GetNewStationMessageForToast(IUAObject communicationDriver)
    {
        switch(communicationDriver)
        {
            case FTOptix.Modbus.Driver modbusDriver:
                if (modbusDriver.Protocol == ModbusProtocol.ModbusTCPProtocol)
                {
                    return "New Modbus TCP station successfully created";
                }
                else
                {
                    return "New Modbus RTU station successfully created";
                }                
            case FTOptix.S7TCP.Driver s7TCPDriver:
                return "New Siemens S7TCP station successfully created";
            case FTOptix.RAEtherNetIP.Driver raEthernetIPDriver:
                return "New RA Ethernet/IP station successfully created";
            case FTOptix.S7TiaProfinet.Driver profinetDriver:
                return "New Siemens S7 TIA profinet station successfully created";
            default:
                return "New generic station successfully created";
        }
    }

    private void ApplyStationParametersValues(IUAObject stationNode, IUAObject editNode, IUAObject communicationDriver)
    {
        switch(communicationDriver)
        {
            case FTOptix.Modbus.Driver modbusDriver:
                ApplyModbusStationProperties((FTOptix.Modbus.Station)stationNode, (FTOptix.Modbus.Station)editNode, modbusDriver);
                break;
            case FTOptix.S7TCP.Driver s7TCPDriver:
                ApplyS7TCPStationProperties((FTOptix.S7TCP.Station)stationNode,(FTOptix.S7TCP.Station)editNode);
                break;
            case FTOptix.RAEtherNetIP.Driver raEthernetIPDriver:
                ApplyRAEIPStationProperties((FTOptix.RAEtherNetIP.Station)stationNode,(FTOptix.RAEtherNetIP.Station)editNode);
                break;
            case FTOptix.S7TiaProfinet.Driver profinetDriver:
                ApplyS7ProfinetStationProperties((FTOptix.S7TiaProfinet.Station)stationNode,(FTOptix.S7TiaProfinet.Station)editNode);
                break;
        }
    }

    private static void InitS7TCPStationProperties(FTOptix.S7TCP.Station newStation)
    {
        _ = newStation.MaximumGapInBytesVariable;
        _ = newStation.MaximumJobSizeInBytesVariable;
        _ = newStation.TimeoutVariable;
        newStation.IPAddress = "127.0.0.1";
        newStation.Port = 102;
        newStation.RemoteDeviceId = 2;
        newStation.RemoteRack = 0;
        newStation.RemoteSlot = 1;
    }

    private static void ApplyS7TCPStationProperties(FTOptix.S7TCP.Station stationNode, FTOptix.S7TCP.Station editNode)
    {
        stationNode.MaximumGapInBytes = editNode.MaximumGapInBytes;
        stationNode.MaximumJobSizeInBytes = editNode.MaximumJobSizeInBytes;
        stationNode.Timeout = editNode.Timeout;
        stationNode.IPAddress = editNode.IPAddress;
        stationNode.Port = editNode.Port;
        stationNode.RemoteDeviceId = editNode.RemoteDeviceId;
        stationNode.RemoteRack = editNode.RemoteRack;
        stationNode.RemoteSlot = editNode.RemoteSlot;
    }    

    private static void InitRAEIPStationProperties(FTOptix.RAEtherNetIP.Station newStation)
    {
        newStation.Route = "127.0.0.1/Backplane/0";
        newStation.Timeout = 30000;
        newStation.UseAlarms = false;
        newStation.EnableExtendedProperties = false;
    }

    private static void ApplyRAEIPStationProperties(FTOptix.RAEtherNetIP.Station stationNode, FTOptix.RAEtherNetIP.Station editNode)
    {
        stationNode.Route = editNode.Route;
        stationNode.Timeout = editNode.Timeout;
        stationNode.UseAlarms = editNode.UseAlarms;
        stationNode.EnableExtendedProperties = editNode.EnableExtendedProperties;
    }

    private static void InitS7ProfinetStationProperties(FTOptix.S7TiaProfinet.Station newStation)
    {
        _ = newStation.TimeoutVariable;
        _ = newStation.CertificateFileVariable;
        newStation.IPAddress = "127.0.0.1";
        newStation.Port = 102;
    }

    private static void ApplyS7ProfinetStationProperties(FTOptix.S7TiaProfinet.Station stationNode, FTOptix.S7TiaProfinet.Station editNode)
    {
        stationNode.Timeout = editNode.Timeout;
        stationNode.CertificateFile = editNode.CertificateFile;
        stationNode.IPAddress = editNode.IPAddress;
        stationNode.Port = editNode.Port;
    }

    private static void InitModbusStationProperties(FTOptix.Modbus.Station newStation, FTOptix.Modbus.Driver modbusDriver)
    {
        newStation.TagOptimization = true;
        newStation.SwapBytes = true;
        newStation.SwapWords = true;
        newStation.SwapDWords = false;
        newStation.UnitIdentifier = 1;
        newStation.WriteMultipleCoils = true;
        newStation.WriteMultipleRegisters = true;
        _ = newStation.MaximumJobSizeInBytesVariable;
        _ = newStation.MaximumGapInBytesVariable;
        _ = newStation.TimeoutVariable;
        if (modbusDriver.Protocol == ModbusProtocol.ModbusTCPProtocol)
        {
            newStation.IPAddress = "127.0.0.1";
            newStation.Port = 502;
        }
    }

    private static void ApplyModbusStationProperties(FTOptix.Modbus.Station stationNode, FTOptix.Modbus.Station editNode, FTOptix.Modbus.Driver modbusDriver)
    {
        stationNode.TagOptimization = editNode.TagOptimization;
        stationNode.SwapBytes = editNode.SwapBytes;
        stationNode.SwapWords = editNode.SwapWords;
        stationNode.SwapDWords = editNode.SwapDWords;
        stationNode.UnitIdentifier = editNode.UnitIdentifier;
        stationNode.WriteMultipleCoils = editNode.WriteMultipleCoils;
        stationNode.WriteMultipleRegisters = editNode.WriteMultipleRegisters;
        stationNode.MaximumJobSizeInBytes = editNode.MaximumJobSizeInBytes;
        stationNode.MaximumGapInBytes = editNode.MaximumGapInBytes;
        stationNode.Timeout = editNode.Timeout;
        if (modbusDriver.Protocol == ModbusProtocol.ModbusTCPProtocol)
        {
            stationNode.IPAddress = editNode.IPAddress;
            stationNode.Port = editNode.Port;
        }
    }

    private void RemoveDefaultRAEIPStation()
    {
        var defaultStationToDelete = Project.Current.Get("CommDrivers/WorkAroundCommHostStart/RADefaultStation");
        if (defaultStationToDelete is FTOptix.RAEtherNetIP.Station defaultRAEIPStation)
        {
            defaultRAEIPStation.Stop();
            defaultRAEIPStation.Delete();
        }
    }

    private void RebrowseAllProfinetStation()
    {
        var profinetDriver = Project.Current.Get("CommDrivers").GetByType<FTOptix.S7TiaProfinet.Driver>();  
        if (profinetDriver.GetNodesByType<FTOptix.S7TiaProfinet.Station>().Any())
        {
            SetRuntimeTagImport(profinetDriver, true);
        }
        foreach (var profinetStation in profinetDriver.GetNodesByType<FTOptix.S7TiaProfinet.Station>())
        {   
            int timeoutCounter = 0;
            while (profinetStation.OperationCode != CommunicationOperationCode.Connected)
            {
                Thread.Sleep(100);               
                if (timeoutCounter > 50)
                {
                    break;
                }
                try
                {
                    profinetStation.Browse(out Struct[] plcItem, out _);
                    if (plcItem != null)
                    {
                        break;
                    }
                }
                catch
                {
                    // Nothing relevant
                }
                timeoutCounter ++;
            }
        }
    }

    private static void SetRuntimeTagImport(IUAObject communicationDriver, bool enableValue)
    {
        IUAVariable runtimeTagImportVariable = communicationDriver switch
        {
            FTOptix.S7TiaProfinet.Driver => communicationDriver.GetOrCreateVariable("AllowRuntimeTagImport"),
            FTOptix.RAEtherNetIP.Driver  => communicationDriver.GetOrCreateVariable("AllowRuntimeTagImport"),
            _ => null
        };
        if (runtimeTagImportVariable != null && runtimeTagImportVariable.Value != enableValue)
        {
            communicationDriver.Stop();
            runtimeTagImportVariable.Value = enableValue;
            communicationDriver.Start();
        }
    }

    LongRunningTask rebrowseAllProfinetStationTask;
    DelayedTask removeClientTask;
}
