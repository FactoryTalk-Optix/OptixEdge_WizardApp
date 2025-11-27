#region Using directives
using System;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.HMIProject;
using FTOptix.NetLogic;
using FTOptix.CoreBase;
using FTOptix.Core;
#endregion

public class ApplicationSettingsLogic : BaseNetLogic
{
    public override void Start()
    {
        if (LogicObject.Get("EditModel") is not ApplicationSettings _editModel)
        {
            Log.Error(LogicObject.BrowseName, "EditModel not found");
            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Error, "Critical Error on general page, check logs!");
            return;
        }
        if (InformationModel.Get(LogicObject.GetVariable("ApplicationSettingsNode")?.Value ?? NodeId.Empty) is not ApplicationSettings _applicationConfiguration)
        {
            Log.Error(LogicObject.BrowseName, "ApplicationSettings not found or invalid node pointer");
            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Error, "Critical Error on general page, check logs!");
            return;
        }
        editModel = _editModel;
        applicationConfiguration = _applicationConfiguration;
        InitEditModel();
        editModel.WPEConfiguration.PortVariable.VariableChange += PortVariableChange;
        editModel.WPEConfiguration.IPAddressVariable.VariableChange += IPAddressVariableChange;
    }

    public override void Stop()
    {
        if (editModel != null)
        {
            editModel.WPEConfiguration.PortVariable.VariableChange -= PortVariableChange;
            editModel.WPEConfiguration.IPAddressVariable.VariableChange -= IPAddressVariableChange;
        }
    }

    [ExportMethod]
    public void SaveProjectSettings()
    {
        if (editModel != null && applicationConfiguration != null)
        {
            // Check if any WPE configuration settings have changed
            bool wpeParametersChanged = applicationConfiguration.WPEConfiguration.Port != editModel.WPEConfiguration.Port ||
            applicationConfiguration.WPEConfiguration.IPAddress != editModel.WPEConfiguration.IPAddress ||
            applicationConfiguration.WPEConfiguration.Hostname != editModel.WPEConfiguration.Hostname ||
            applicationConfiguration.WPEConfiguration.CertificateFile.Uri != editModel.WPEConfiguration.CertificateFile.Uri ||
            applicationConfiguration.WPEConfiguration.PrivateKey.Uri != editModel.WPEConfiguration.PrivateKey.Uri;
            // Save the configurations
            applicationConfiguration.WPEConfiguration.Port = editModel.WPEConfiguration.Port;
            applicationConfiguration.WPEConfiguration.IPAddress = editModel.WPEConfiguration.IPAddress;
            applicationConfiguration.WPEConfiguration.Hostname = editModel.WPEConfiguration.Hostname;
            applicationConfiguration.WPEConfiguration.CertificateFile = editModel.WPEConfiguration.CertificateFile;
            applicationConfiguration.WPEConfiguration.PrivateKey = editModel.WPEConfiguration.PrivateKey;
            applicationConfiguration.StartFromDashboard = editModel.StartFromDashboard;
            // Notify user
            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Success, "Optix Wizard settings saved!");
            if (wpeParametersChanged)
            {
                NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Info, "Web server settings changed, please restart the application to apply changes.");
                LogicObject.GetVariable("RestartBannerRequest").Value = true;
            }
        }
    }

    private void InitEditModel()
    {
        if (editModel != null && applicationConfiguration != null)
        {
            editModel.StartFromDashboard = applicationConfiguration.StartFromDashboard;
            editModel.WPEConfiguration.Port = applicationConfiguration.WPEConfiguration.Port;
            editModel.WPEConfiguration.IPAddress = applicationConfiguration.WPEConfiguration.IPAddress;
            editModel.WPEConfiguration.Hostname = applicationConfiguration.WPEConfiguration.Hostname;
            editModel.WPEConfiguration.CertificateFile = applicationConfiguration.WPEConfiguration.CertificateFile;
            editModel.WPEConfiguration.PrivateKey = applicationConfiguration.WPEConfiguration.PrivateKey;
        }
    }

    private void PortVariableChange(object sender, VariableChangeEventArgs e)
    {
        if (e.NewValue != e.OldValue && e.NewValue != applicationConfiguration.WPEConfiguration.Port)
        {
            uint newPort = e.NewValue;
            // Validate if the port number is in valid TCP port range
            if (!CommonLogic.IsValidTcpPort(newPort))
            {
                NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Error, $"Invalid port number {newPort}. TCP ports must be between 1 and 65535.", durationOnScreen: TOAST_ERROR_DURATION);
                // Revert to previous value
                editModel.WPEConfiguration.Port = (uint)e.OldValue;
                return;
            }
            // Check if the port is a well-known system port (not allowed)
            if (newPort < 1024)
            {
                NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Error, $"Port {newPort} is a well-known system port. Using a port above 1024.", durationOnScreen: TOAST_ERROR_DURATION);
                // Revert to previous value
                editModel.WPEConfiguration.Port = (uint)e.OldValue;
                return;
            }
            // Check if the port is already in use
            if (CommonLogic.IsPortInUse(newPort))
            {
                NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Error, $"Port {newPort} is already in use, please select another port.", durationOnScreen: TOAST_ERROR_DURATION);
                // Revert to previous value
                editModel.WPEConfiguration.Port = (uint)e.OldValue;
                return;
            }
        }
    }

    private void IPAddressVariableChange(object sender, VariableChangeEventArgs e)
    {
        if (e.NewValue != e.OldValue && e.NewValue != applicationConfiguration.WPEConfiguration.IPAddress)
        {
            if (!CommonLogic.IsValidIPv4Address(e.NewValue))
            {
                NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Error, $"Invalid IP address format: {e.NewValue.Value}", durationOnScreen: TOAST_ERROR_DURATION);
                // Revert to previous value
                editModel.WPEConfiguration.IPAddress = e.OldValue;
                return;
            }
            if (CommonLogic.IsLoopbackAddress(e.NewValue) || !CommonLogic.IsIPAddressAssignedToSystem(e.NewValue) && !CommonLogic.IsAnyIpAddress(e.NewValue))
            {
                NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Error, $"The IP address {e.NewValue.Value} is not assigned to any network interface on the system.", durationOnScreen: TOAST_ERROR_DURATION);
                // Revert to previous value
                editModel.WPEConfiguration.IPAddress = e.OldValue;
                return;
            }
        }
    }




    ApplicationSettings editModel;
    ApplicationSettings applicationConfiguration;
    const int TOAST_ERROR_DURATION = 2200;
}
