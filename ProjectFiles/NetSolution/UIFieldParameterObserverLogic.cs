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
using System.Linq;
using System.Collections.Generic;
#endregion

public class UIFieldParameterObserverLogic : BaseNetLogic
{
    public override void Start()
    {
        ownerAccordionContent = Owner as AccordionContent;
    }

    public override void Stop()
    {
        UnsubscribeObserver();
    }

    [ExportMethod]
    public void UnsubscribeObserver()
    {
        if (eventsHandled)
        {
            UnregisterUIItemChange(Owner);
            eventsHandled = false;
        }
    }

    [ExportMethod]
    public void SubscribeObserver()
    {
        // Delay the subscription to ensure that all UI elements are properly initialized
        new DelayedTask(TaskRegisterUIItemChange, Owner, 1000, ownerAccordionContent).Start();
    }

    [ExportMethod]
    public void SubscribeObserverSingleControl(NodeId uiItemToObserve)
    {
        RegistertUIItemChange(InformationModel.Get(uiItemToObserve));
    }

    [ExportMethod]
    public void UnsubscribeObserverSingleControl(NodeId uiItemToObserve)
    {
        enableSaveParameter.Value = true;
        UnregisterUIItemChange(InformationModel.Get(uiItemToObserve));
    }

    private void TaskRegisterUIItemChange(BaseTaskWrapper task, object argument)
    {
        if (argument is IUANode nodeToAnalyze)
        {
            if (ownerAccordionContent.Owner is Accordion ownerAccordionNode && ownerAccordionNode.FindByType<StationProps>() is IUAObject stationProps)
            {
                enableSaveParameter = stationProps.GetVariable("EnableSave");
                RegistertUIItemChange(nodeToAnalyze);
                eventsHandled = true;
            }
        }
        task.Dispose();
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
                        textBox.TextVariable.VariableChange += OnVariableChanged;
                        break;
                    case ComboBox comboBox:
                        comboBox.SelectedItemVariable.VariableChange += OnVariableChanged;
                        break;
                    case SpinBox spinBox:
                        spinBox.ValueVariable.VariableChange += OnVariableChanged;
                        break;
                    case Switch uiSwitch:
                        uiSwitch.CheckedVariable.VariableChange += OnVariableChanged;
                        break;
                    case DurationPicker durationPicker:
                        durationPicker.ValueVariable.VariableChange += OnVariableChanged;
                        break;
                    case CheckBox checkBox:
                        checkBox.CheckedVariable.VariableChange += OnVariableChanged;
                        break;
                    case DateTimePicker dateTimePicker:
                        dateTimePicker.ValueVariable.VariableChange += OnVariableChanged;
                        break;
                    case Button:
                        // Buttons do not require registration as they do not have user interaction events
                        break;
                    case Label:
                        // Labels do not require registration as they do not have user interaction events
                        break;
                    case IUAVariable:
                        // Avoid registration of variables that are not UI related
                        break;
                    case TagViewer tagViewer:
                        tagViewer.GetVariable("CounterDeleteAction").VariableChange += OnVariableChanged;
                        break;
                    case FileValueContainer fileValueContainer:
                        if (fileValueContainer.Find<IUAVariable>("PathSelected") is IUAVariable pathSelected)
                        {
                            pathSelected.VariableChange += OnVariableChanged;
                        }
                        break;
                    case Accordion accordion:
                        // Avoid multiple registration of the same UIFieldParameterObserverLogic in case of nested accordions
                        if (accordion.Content.Get("UIFieldParameterObserverLogic") == null)
                        {
                            RegistertUIItemChange(accordion.Content);
                        }
                        break;
                    case MQTTPayloadFieldBase mqttPayloadField:
                        RegistertUIItemChange(mqttPayloadField);
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
                        textBox.TextVariable.VariableChange -= OnVariableChanged;
                        break;
                    case ComboBox comboBox:
                        comboBox.SelectedItemVariable.VariableChange -= OnVariableChanged;
                        break;
                    case SpinBox spinBox:
                        spinBox.ValueVariable.VariableChange -= OnVariableChanged;
                        break;
                    case FTOptix.UI.Switch uiSwitch:
                        uiSwitch.CheckedVariable.VariableChange -= OnVariableChanged;
                        break;
                    case DurationPicker durationPicker:
                        durationPicker.ValueVariable.VariableChange -= OnVariableChanged;
                        break;
                    case CheckBox checkBox:
                        checkBox.CheckedVariable.VariableChange -= OnVariableChanged;
                        break;
                    case DateTimePicker dateTimePicker:
                        dateTimePicker.ValueVariable.VariableChange -= OnVariableChanged;
                        break;
                    case Button:
                        // Buttons do not require unregistration as they do not have user interaction events
                        break;
                    case Label:
                        // Labels do not require unregistration as they do not have user interaction events
                        break;
                    case IUAVariable:
                        // Avoid unregistration of variables that are not UI related
                        break;
                    case TagViewer tagViewer:
                        tagViewer.GetVariable("CounterDeleteAction").VariableChange -= OnVariableChanged;
                        break;
                    case FileValueContainer fileValueContainer:
                        if (fileValueContainer.Find<IUAVariable>("PathSelected") is IUAVariable pathSelected)
                        {
                            pathSelected.VariableChange -= OnVariableChanged;
                        }
                        break;
                    case Accordion accordion:
                        // Avoid multiple unregistration of the same UIFieldParameterObserverLogic in case of nested accordions
                        if (accordion.Content.Get("UIFieldParameterObserverLogic") == null)
                        {
                            UnregisterUIItemChange(accordion.Content);
                        }
                        break;
                    case MQTTPayloadFieldBase mqttPayloadField:
                        UnregisterUIItemChange(mqttPayloadField);
                        break;
                    default:
                        UnregisterUIItemChange(children);
                        break;
                }
            }
        }
    }

    private void OnVariableChanged(object sender, VariableChangeEventArgs e)
    {        
        ParameterChanged();
    }

    [ExportMethod]
    public void ParameterChanged()
    {
        enableSaveParameter.Value = true;
    }

    bool eventsHandled = false;
    IUAVariable enableSaveParameter;
    AccordionContent ownerAccordionContent;
}