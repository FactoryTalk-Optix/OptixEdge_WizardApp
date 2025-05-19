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

public class BannerMessageLogic : BaseNetLogic
{
    public override void Start()
    {
        if (Owner.GetVariable("AlwaysOn").Value)
        {
            return;
        }
        if (Owner is Item itemOwner)
        {
            visibleVariable = itemOwner.VisibleVariable;
            visibleVariable.VariableChange += VisibleVariable_VariableChange;
        }
        notificationLevelVariable = Owner.GetVariable("NotificationLevel");
        showCloseButtonVariable = Owner.GetVariable("ShowCloseButton");
    }

    public override void Stop()
    {
        CommonLogic.DisposeTask(enableCloseButtonTask);
        if (visibleVariable != null)
        {
            visibleVariable.VariableChange -= VisibleVariable_VariableChange;
        }
    }

    private void VisibleVariable_VariableChange(object sender, VariableChangeEventArgs e)
    {
        if (notificationLevelVariable != null && showCloseButtonVariable != null)
        {
            if (e.NewValue)
            {
                var notificationLevel = (ToastBannerNotificationLevel) notificationLevelVariable.Value.Value;
                int delayToShowCloseButton = 1000;
                switch (notificationLevel)
                {
                    case ToastBannerNotificationLevel.Error:
                    case ToastBannerNotificationLevel.Warning:
                        delayToShowCloseButton = 5000;
                        break;
                    case ToastBannerNotificationLevel.Success:
                        delayToShowCloseButton = 1000;
                        break;
                    case ToastBannerNotificationLevel.Info:
                        delayToShowCloseButton = 10;
                        break;
                }
                enableCloseButtonTask = new DelayedTask(ShowCloseButton, delayToShowCloseButton, Owner);
                enableCloseButtonTask.Start();
            }
            else
            {
                CommonLogic.DisposeTask(enableCloseButtonTask);
                showCloseButtonVariable.Value = false;
            }
        }
    }

    private void ShowCloseButton()
    {
        showCloseButtonVariable.Value = true;
        enableCloseButtonTask.Dispose();
    }

    IUAVariable visibleVariable;
    IUAVariable notificationLevelVariable;
    IUAVariable showCloseButtonVariable;
    DelayedTask enableCloseButtonTask;
}
