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
using System.IO;
using System.Collections.Generic;
using System.Linq;
using CsvHelper;
using System.Globalization;
#endregion

public class DiagnosticsPageLogic : BaseNetLogic
{
    public override void Start()
    {
        // Insert code to be executed when the user-defined logic is started
    }

    public override void Stop()
    {
        // Insert code to be executed when the user-defined logic is stopped
    }

    [ExportMethod]
    public void GenerateCSV(string folderPath)
    {
        if (!string.IsNullOrEmpty(folderPath) && Path.Exists(new ResourceUri(folderPath).Uri))
        {
            string filePath = Path.Combine(new ResourceUri(folderPath).Uri, "tags_to_import.csv");
            if (File.Exists(filePath) && !overwriteConfirmed)
            {
                pathToCSVFile = new ResourceUri(folderPath);
                LogicObject.Get<MethodInvocation>("InvokeConfirmOverwriteFile").Invoke();
                return;
            }
            try
            {
                var records = new List<TagDataFromCSV>
             {
                 new() { Driver= CommonLogic.CSVDriverMapping.FirstOrDefault(x=> x.Value == FTOptix.S7TCP.ObjectTypes.Driver).Key, Name = "MySiemensTCPVar", DataType = CommonLogic.CSVDataTypeMapping.First(x=> x.Value == OpcUa.DataTypes.Int16).Key, Address="DB10.DBW0", ArrayDimension="", StringLength="",Description="My word"  },
                 new() { Driver= CommonLogic.CSVDriverMapping.FirstOrDefault(x=> x.Value == FTOptix.Modbus.ObjectTypes.Driver).Key, Name = "Modbus_HoldingReg", DataType = CommonLogic.CSVDataTypeMapping.First(x=> x.Value == OpcUa.DataTypes.Int16).Key, Address="HR0", ArrayDimension="", StringLength="",Description="My word on holding register 0"  },
                 new() { Driver= CommonLogic.CSVDriverMapping.FirstOrDefault(x=> x.Value == FTOptix.Modbus.ObjectTypes.Driver).Key, Name = "Modbus_Coil", DataType = CommonLogic.CSVDataTypeMapping.First(x=> x.Value == OpcUa.DataTypes.Boolean).Key, Address="CO0", ArrayDimension="", StringLength="",Description="My bit on coil 0"  },
                 new() { Driver= CommonLogic.CSVDriverMapping.FirstOrDefault(x=> x.Value == FTOptix.Modbus.ObjectTypes.Driver).Key, Name = "Modbus_InputRegister", DataType = CommonLogic.CSVDataTypeMapping.First(x=> x.Value == OpcUa.DataTypes.Int32).Key, Address="IR0", ArrayDimension="", StringLength="",Description="My DWord on input register 0"  },
                 new() { Driver= CommonLogic.CSVDriverMapping.FirstOrDefault(x=> x.Value == FTOptix.Modbus.ObjectTypes.Driver).Key, Name = "Modbus_DiscreteInput", DataType = CommonLogic.CSVDataTypeMapping.First(x=> x.Value == OpcUa.DataTypes.Boolean).Key, Address="DI0", ArrayDimension="", StringLength="",Description="My bit on discrete input 0"  },
                 new() { Driver= CommonLogic.CSVDriverMapping.FirstOrDefault(x=> x.Value == FTOptix.RAEtherNetIP.ObjectTypes.Driver).Key, Name = "MyLogixVar", DataType = CommonLogic.CSVDataTypeMapping.First(x=> x.Value == OpcUa.DataTypes.Float).Key, Address="Application.GlobalVar.MyReal", ArrayDimension="", StringLength="",Description="My real"  },
             };
                var writerOptions = new FileStreamOptions()
                {
                    Access = FileAccess.ReadWrite,
                    Mode = FileMode.OpenOrCreate
                };
                using var writer = new StreamWriter(filePath, writerOptions);
                using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
                csv.WriteRecords(records);
                NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Success, $"The CSV file was generated correctly in {filePath}");
                pathToCSVFile = string.Empty;
                overwriteConfirmed = false;
            }
            catch (Exception ex)
            {
                NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Error, "Cannot generate the CSV file");
                Log.Error(LogicObject.BrowseName, $"{ex.Message} - Stack: {ex.StackTrace}");
            }
        }
    }

    [ExportMethod]
    public void ConfirmOverwriteFile()
    {
        var confirmOverwriteFileResult = (ConfirmOverwriteFileResult)LogicObject.GetVariable("OverwriteReturnResult").Value.Value;
        if (confirmOverwriteFileResult == ConfirmOverwriteFileResult.Confirmed)
        {
            overwriteConfirmed = true;
            Log.Debug(LogicObject.BrowseName, "Overwrite confirmed by user.");
            GenerateCSV(pathToCSVFile.Uri);
        }
        else if (confirmOverwriteFileResult == ConfirmOverwriteFileResult.Cancelled)
        {
            Log.Debug(LogicObject.BrowseName, "Overwrite cancelled by user.");
        }
        LogicObject.GetVariable("OverwriteReturnResult").Value = (int)ConfirmOverwriteFileResult.NoEntry;
    }

    private bool overwriteConfirmed = false;
    private ResourceUri pathToCSVFile;
}
