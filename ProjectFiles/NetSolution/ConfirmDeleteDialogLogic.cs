#region Using directives
using System;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.UI;
using FTOptix.HMIProject;
using FTOptix.CoreBase;
using FTOptix.NetLogic;
using FTOptix.WebUI;
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
using FTOptix.Retentivity;
using FTOptix.CommunicationDriver;
using FTOptix.OPCUAClient;
using FTOptix.Core;
using FTOptix.MQTTBroker;
using FTOptix.Store;
using FTOptix.SQLiteStore;
using FTOptix.InfluxDBStore;
using FTOptix.InfluxDBStoreLocal;
using FTOptix.ODBCStore;
using FTOptix.DataLogger;
using FTOptix.InfluxDBStoreRemote;
using System.Reflection;
using FTOptix.AuditSigning;
using FTOptix.NativeUI;
#endregion

public class ConfirmDeleteDialogLogic : BaseNetLogic
{
    public override void Start()
    {
        try
        {
            ownerDialog = (Dialog)Owner;
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
        }
        catch (Exception ex)
        {
            Log.Error(LogicObject.BrowseName, ex.Message);
            ownerDialog.Close();
        }
    }

    public override void Stop()
    {
        // Insert code to be executed when the user-defined logic is stopped
    }

    [ExportMethod]
    public void DeleteStation(NodeId station, NodeId widget)
    {
        const string methodName = "DeleteStation";
        object[] inputArguments = [station, widget];
        if (InformationModel.Get(station) is IUAObject targetStation && InformationModel.Get(widget) is IUAObject)
        {
            switch (targetStation)
            {
                case FTOptix.CommunicationDriver.CommunicationStation:
                    commDriversNetlogic.ExecuteMethod(methodName, inputArguments);
                    break;
                case FTOptix.MQTTClient.MQTTClient:
                case FTOptix.MQTTClient.MQTTPublisher:
                    mqttClientNetlogic.ExecuteMethod(methodName, inputArguments);
                    break;
                case FTOptix.OPCUAServer.OPCUAServer:
                case FTOptix.OPCUAServer.NodesToPublishConfigurationEntry:
                    opcUaServerNetlogic.ExecuteMethod(methodName, inputArguments);
                    break;
                case FTOptix.DataLogger.DataLogger:
                    loggersNetLogic.ExecuteMethod(methodName, inputArguments);
                    break;
            }
        }
        else if (InformationModel.Get(station) is IUAVariable targetVariable && targetVariable.IsInstanceOf(FTOptix.DataLogger.VariableTypes.VariableToLog) && InformationModel.Get(widget) is IUAObject)
        {
            loggersNetLogic.ExecuteMethod(methodName, inputArguments);
        }
    }

    private NetLogicObject opcUaServerNetlogic;
    private NetLogicObject commDriversNetlogic;
    private NetLogicObject mqttClientNetlogic;
    private NetLogicObject loggersNetLogic;
    private Dialog ownerDialog;
}
