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
using FTOptix.OPCUAServer;
using FTOptix.DataLogger;
using FTOptix.MQTTClient;
using FTOptix.Core;
using FTOptix.NativeUI;
#endregion

public class ToastNotificationDialogLogic : BaseNetLogic
{
    public override void Start()
    {
        counterBarVariable = Owner.GetVariable("CounterVariable");
        ownerDialog = (Dialog) Owner;
        decrementCounterTask = new PeriodicTask(AutoCloseDelay, 10, LogicObject);
        decrementCounterTask.Start();
        ownerDialog.Visible = true;
    }

    public override void Stop()
    {
        try
        {
            decrementCounterTask?.Cancel();
        }
        catch
        {
            // Task not in execution
        }
        try
        {
            closeAfterAnimationTask?.Cancel();
        }
        catch
        {
            // Task not in execution
        }
        decrementCounterTask?.Dispose();
        closeAfterAnimationTask?.Dispose();
    }

     [ExportMethod]
    public void CloseDialogDelayed()
    {
        ownerDialog.GetVariable("AnimationOut/Running").Value = true;
        int duration = ownerDialog.Get<NumberAnimation>("AnimationOut/FadeAnimation").Duration;
        closeAfterAnimationTask = new DelayedTask(DelayedClose, duration,  ownerDialog);
        closeAfterAnimationTask.Start();
    }

    private void AutoCloseDelay()
    {
        if (decrementCounterTask.IsCancellationRequested)
        {
            return;
        }
        if (counterBarVariable.Value <= 0)
        {
            decrementCounterTask.Cancel();
            CloseDialogDelayed();
            return;
        }
        counterBarVariable.Value -= 10;
    }

    private void DelayedClose()
    {
        ownerDialog.Close();
    }

    IUAVariable counterBarVariable;
    Dialog ownerDialog;
    PeriodicTask decrementCounterTask;
    DelayedTask closeAfterAnimationTask;
}
