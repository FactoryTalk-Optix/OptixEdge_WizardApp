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

public class UIFieldParameterObserverLogic : BaseNetLogic
{
    public override void Start()
    {
        if (Owner.Owner is Accordion ownerAccordionNode && ownerAccordionNode.FindByType<StationProps>() is IUAObject stationProps)
        {
            enableSaveParameter = stationProps.GetVariable("EnableSave");
            RegistertUIItemChange(Owner);
            eventsHandled = true;
        }
    }

    public override void Stop()
    {
        if (eventsHandled)
        {
            UnregisterUIItemChange(Owner);
        }
    }

    private void RegistertUIItemChange(IUANode nodeToAnalyze)
    {
        if (nodeToAnalyze is IUAObject)
        {
            foreach (var children in nodeToAnalyze.Children)
            {
                switch (children)
                {
                    case TextBox textBox:
                        textBox.OnUserTextChanged += OnUserTextChanged;
                        break;
                    case ComboBox comboBox:
                        comboBox.OnUserSelectionChanged += OnUserSelectionChanged;
                        break;
                    case SpinBox spinBox:
                        spinBox.OnUserValueChanged += OnUserValueChanged;
                        break;
                    case Switch uiSwitch:
                        uiSwitch.OnUserValueChanged += OnUserValueChanged;
                        break;
                    case DurationPicker durationPicker:
                        durationPicker.OnUserValueChanged += OnUserValueChanged;
                        break;
                    case Accordion accordion:
                        RegistertUIItemChange(accordion.Content);
                        break;
                    default:
                        RegistertUIItemChange(children);
                        break;
                }
            }
        }
    }

    private void UnregisterUIItemChange(IUANode nodeToAnalyze)
    {
        foreach (var children in nodeToAnalyze.Children)
        {
            if (nodeToAnalyze is IUAObject)
            {
                switch (children)
                {
                    case TextBox textBox:
                        textBox.OnUserTextChanged -= OnUserTextChanged;
                        break;
                    case ComboBox comboBox:
                        comboBox.OnUserSelectionChanged -= OnUserSelectionChanged;
                        break;
                    case SpinBox spinBox:
                        spinBox.OnUserValueChanged -= OnUserValueChanged;
                        break;
                    case FTOptix.UI.Switch uiSwitch:
                        uiSwitch.OnUserValueChanged -= OnUserValueChanged;
                        break;
                    case DurationPicker durationPicker:
                        durationPicker.OnUserValueChanged -= OnUserValueChanged;
                        break;
                    case Accordion accordion:
                        UnregisterUIItemChange(accordion.Content);
                        break;
                    default:
                        UnregisterUIItemChange(children);
                        break;
                }
            }
        }
    }

    private void OnUserValueChanged(object sender, UserValueChangedEvent e) => ParameterChanged();

    private void OnUserSelectionChanged(object sender, UserSelectionChangedEvent e) => ParameterChanged();

    private void OnUserTextChanged(object sender, UserTextChangedEvent e) => ParameterChanged();

    private void ParameterChanged()
    {
        enableSaveParameter.Value = true;
    }

    bool eventsHandled = false;
    IUAVariable enableSaveParameter;
}
