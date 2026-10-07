#region Using directives
using System;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.UI;
using FTOptix.HMIProject;
using FTOptix.NetLogic;
using FTOptix.Core;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using CsvHelper;
using System.Globalization;
using FTOptix.EventLogger;
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
    public void DownloadCompleteHandler(int statusCode)
    {
        if (statusCode == 0)
        {
            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Success, $"The CSV file was generated correctly and downloaded");
        }
    }

    [ExportMethod]
    public void GenerateAndDownloadTemplateCSV()
    {
        var fileName = "TagsImportTemplate.csv";
        var filePath = ResourceUri.FromProjectRelativePath(fileName);
        if (File.Exists(filePath.Uri))
        {
            File.Delete(filePath.Uri);
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
            using var writer = new StreamWriter(filePath.Uri, writerOptions);
            using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
            csv.WriteRecords(records);
            Session.Get<NetLogicObject>("FileTransferManagementLogic").ExecuteMethod("RequestDownloadTask", new object[] { FileOperationRequest.OperationType.FileDownload, fileName, LogicObject.NodeId });
        }
        catch (Exception ex)
        {
            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Error, "Cannot generate the CSV file");
            Log.Error(LogicObject.BrowseName, $"{ex.Message} - Stack: {ex.StackTrace}");
        }
    }

}
