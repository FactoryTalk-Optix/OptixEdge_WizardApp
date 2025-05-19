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
using System.Collections.Generic;
using System.Linq;
using System.Collections.Concurrent;
using FTOptix.NativeUI;
#endregion

public class NotificationsMessageHandlerLogic : BaseNetLogic
{
    public static NotificationsMessageHandlerLogic Instance { get; private set; }

    public override void Start()
    {
        Instance = this;
        toastRequestQueue = [];
        bannerRequestQueue = [];
        toastBannerTemporaryConfigurationFodler = Project.Current.Get<Folder>("Model/ToastBannerTemporaryConfiguration");
        globalBanner = Owner.Get<Item>("MainBody/GlobalBanner");
        queueHandler = new PeriodicTask(RequestQueueHandler, 100, Owner);
        queueHandler.Start();
    }

    public override void Stop()
    {
        Instance = null;
        CommonLogic.DisposeTask(queueHandler);
    }

    private void RequestQueueHandler()
    {
        if (queueHandler.IsCancellationRequested)
        {
            return;
        }
        bool toastOpen = false;
        try
        {
            toastOpen = Owner.FindNodesByType<ToastNotification>().Any();
        }
        catch
        {
            toastOpen = false;
        }
        if (!toastOpen && toastRequestQueue.TryDequeue(out NotificationMessageConfiguration toastToGenerate))
        {
            toastBannerTemporaryConfigurationFodler.Children.Clear();
            var toastConfigurationData = InformationModel.MakeObject<ToastBannerConfiguration>("ToastConfiguration1");
            toastConfigurationData.MessageToDisplay = new LocalizedText(toastToGenerate.Messagge, "");
            toastConfigurationData.GetVariable("NotificationLevel").Value = (int)toastToGenerate.Level;
            toastConfigurationData.GetVariable("StartingPosition").Value = (int)toastToGenerate.Position;
            toastConfigurationData.DurationOnScreen = toastToGenerate.DurationOnScreen;
            toastConfigurationData.CounterVariable = toastToGenerate.DurationOnScreen;
            toastBannerTemporaryConfigurationFodler.Add(toastConfigurationData);
            var dialogType = InformationModel.Get<DialogType>( Optix_DefaultApplication_OptixEdge.ObjectTypes.ToastNotification);
            UICommands.OpenDialog(Owner, dialogType, toastConfigurationData.NodeId);
        }
        if (globalBanner != null && !globalBanner.Visible && bannerRequestQueue.TryDequeue(out NotificationMessageConfiguration bannerMessageToDisplay))
        {
            globalBanner.GetVariable("NotificationLevel").Value = (int)bannerMessageToDisplay.Level;
            globalBanner.GetVariable("MessageToDisplay").Value = new LocalizedText(bannerMessageToDisplay.Messagge, "");
            globalBanner.Visible = true;
        }
    }

    public void RequestToastNotification (ToastBannerNotificationLevel level, string message, ToastPosition position = ToastPosition.BottomCenter, int durationOnScreen = -1)
    {
        var duration = durationOnScreen;
        if (duration == -1)
        {
            duration = level switch
            {
                ToastBannerNotificationLevel.Error => 4000,
                ToastBannerNotificationLevel.Warning => 2400,
                ToastBannerNotificationLevel.Info => 1300,
                ToastBannerNotificationLevel.Success => 1600,
                _ => 2000
            };
        }
        var toastConfigurationData = new NotificationMessageConfiguration()
        {
            Level = level,
            Position = position,
            Messagge = message,
            DurationOnScreen = duration
        };
        toastRequestQueue.Enqueue(toastConfigurationData);
    }

    public void RequestBannerNotification(ToastBannerNotificationLevel level, string message)
    {
        if (globalBanner != null)
        {
            var bannerConfigurationData = new NotificationMessageConfiguration()
            {
                Level = level,
                Messagge = message,
            };
            bannerRequestQueue.Enqueue(bannerConfigurationData);
        }
    }

    private PeriodicTask queueHandler;

    private Item globalBanner;

    private ConcurrentQueue<NotificationMessageConfiguration> toastRequestQueue;

    private ConcurrentQueue<NotificationMessageConfiguration> bannerRequestQueue;

    private Folder toastBannerTemporaryConfigurationFodler; 

}

    public record NotificationMessageConfiguration
    {
        public ToastBannerNotificationLevel Level { get; init; } = ToastBannerNotificationLevel.Info;
        public ToastPosition Position { get; init; } = ToastPosition.BottomCenter;
        public string Messagge { get; init; } = String.Empty;

        public int DurationOnScreen {get; init; } = 3000;
    }
    
    public enum ToastBannerNotificationLevel
    {
        Error = 0,
        Info = 1,
        Success = 2,
        Warning = 3
    }

    public enum ToastPosition
    {
        TopLeft = 0,
        TopRight = 1,
        TopCenter = 2,
        CenterLeft = 3,
        CenterRight = 4,
        CenterCenter = 5,
        BottomLeft = 6,
        BottomRight = 7,
        BottomCenter = 8
    }
