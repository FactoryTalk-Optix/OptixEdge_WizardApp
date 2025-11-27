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
using FTOptix.MQTTClient;
using FTOptix.CommunicationDriver;
using FTOptix.OPCUAServer;
using FTOptix.DataLogger;
using FTOptix.OPCUAClient;
using FTOptix.Core;
using System.Collections.Generic;
#endregion

public class MQTTPayloadInfoEditDialogLogic : BaseNetLogic
{
    public override void Start()
    {
        // Get references to dialog control variables
        dataTypeValue = LogicObject.GetVariable("DataTypeValue");
        arrayDimension = LogicObject.GetVariable("ArrayDimension");        
        // Retrieve the payload field objects through alias mapping system
        if (Owner.GetAlias(CommonLogic.sourceAliasNameMapping.GetValueOrDefault(OptixEdge_WizardApp.ObjectTypes.MQTTPayloadInfoEdit)) is MQTTPayloadFieldBase _fieldBase
            && _fieldBase.GetAlias(CommonLogic.sourceAliasNameMapping.GetValueOrDefault(OptixEdge_WizardApp.ObjectTypes.MQTTPayloadFieldBase)) is MQTTPayloadFieldInfo _editField)
        {
            // Store references to the field objects
            fieldBase = _fieldBase;
            editField = _editField;                    
            // Initialize data type value and subscribe to changes
            dataTypeValue.Value = editField.ValueVariable.DataType;
            dataTypeValue.VariableChange += OnDataTypeChanged;            
            // Initialize array dimension value and subscribe to changes
            arrayDimension.Value = editField.ValueVariable.ActualArrayDimensions.Length > 0 ? editField.ValueVariable.ActualArrayDimensions[0] : 1;
            arrayDimension.VariableChange += OnArrayDimensionChanged;            
            // Generate the initial UI based on current field configuration
            GenerateUIFieldInternal();
        }
    }

    public override void Stop()
    {
        // Unsubscribe from data type change events
        dataTypeValue.VariableChange -= OnDataTypeChanged;               
    }

    /// <summary>
    /// Removes the link between the payload field and its source variable.
    /// Resets the field to use a default value instead of a dynamic variable reference.
    /// </summary>
    [ExportMethod]
    public void UnlinkVariableFromValue()
    {
        // Clear the node pointer to remove variable binding
        editField.ValueDataVariablePath = string.Empty;
        // Reset value to default
        editField.Value = 0;
        // Refresh the UI to reflect the unlinked state
        GenerateValueField();
    }

    /// <summary>
    /// Generates and refreshes the UI control for the payload field value.
    /// Updates both the dialog container and the main field container with the appropriate
    /// UI element based on the field's current configuration and data type.
    /// </summary>
    [ExportMethod]
    public void GenerateValueField()
    {
        if (editField != null && Owner.Find<Panel>("ValueContainerValue") is Panel valueContainer && fieldBase.Find<Panel>("ValueContainer") is Panel fieldValueContainer)
        {
            // Generate UI element for the dialog value container
            var valueItem = MqttClientLogic.GenerateUIItemFromValue(editField);
            valueContainer.Children.Clear();
            valueContainer.Add(valueItem);            
            // Generate UI element for the main field container
            valueItem = MqttClientLogic.GenerateUIItemFromValue(editField);
            var accordionContainer = CommonLogic.GetOwner(fieldBase, OptixEdge_WizardApp.ObjectTypes.MQTTPublisherUIObj) as Accordion;            
            // Temporarily unsubscribe observer to prevent conflicts during UI update
            accordionContainer.Find<NetLogicObject>("UIFieldParameterObserverLogic").ExecuteMethod("UnsubscribeObserverSingleControl", [fieldBase.Owner.NodeId]);            
            // Update the main field container
            fieldValueContainer.Children.Clear();
            fieldValueContainer.Add(valueItem);            
            // Re-subscribe observer to monitor changes in the updated control
            accordionContainer.Find<NetLogicObject>("UIFieldParameterObserverLogic").ExecuteMethod("SubscribeObserverSingleControl", [fieldBase.Owner.NodeId]);
        }
    }

    /// <summary>
    /// Creates a dynamic link between a payload field and a source variable.
    /// This static method allows external components to establish variable bindings
    /// for MQTT payload fields programmatically.
    /// </summary>
    /// <param name="payloadField">NodeId of the target payload field to link</param>
    /// <param name="sourceVariableToLink">NodeId of the source variable to bind</param>
    public static void LinkVariableToPayloadField(NodeId payloadField, NodeId sourceVariableToLink)
    {
        // Validate all required objects exist and are of correct types
        if (InformationModel.Get(payloadField) is MQTTPayloadInfoEdit mqttPayloadFieldInfo
        && mqttPayloadFieldInfo.GetAlias(CommonLogic.sourceAliasNameMapping.GetValueOrDefault(OptixEdge_WizardApp.ObjectTypes.MQTTPayloadInfoEdit)) is MQTTPayloadFieldBase uiFieldBase
        && uiFieldBase.GetAlias(CommonLogic.sourceAliasNameMapping.GetValueOrDefault(OptixEdge_WizardApp.ObjectTypes.MQTTPayloadFieldBase)) is MQTTPayloadFieldInfo editField
        && InformationModel.GetVariable(sourceVariableToLink) is IUAVariable sourceVariable)
        {           
            // Configure data type as per the source variable
            editField.ValueVariable.DataType = sourceVariable.DataType;
            editField.ValueVariable.ActualDataType = sourceVariable.ActualDataType;
            // Get the dataConfiguration in order to generate the model variable linked to the field
            if (CommonLogic.GetOwner(editField, OptixEdge_WizardApp.ObjectTypes.MQTTPublisherDataConfiguration) is MQTTPublisherDataConfiguration dataConfiguration)
            {
                var linkedVariableAncestral = CommonLogic.GetOwner(sourceVariable, FTOptix.CommunicationDriver.ObjectTypes.CommunicationStation);
                var browseName = $"{linkedVariableAncestral.BrowseName}.{sourceVariable.BrowseName}";
                var modelVariable = dataConfiguration.Data.GetVariable(browseName);
                if (modelVariable == null)
                {
                    modelVariable = InformationModel.MakeVariable(browseName, sourceVariable.DataType, sourceVariable.ArrayDimensions);
                    dataConfiguration.Data.Add(modelVariable);
                }
                modelVariable.SetDynamicLink(sourceVariable, DynamicLinkMode.Read);
                editField.ValueDataVariablePath = browseName;
            }
            // Refresh the UI to display the linked variable
            mqttPayloadFieldInfo.FindByType<NetLogicObject>().ExecuteMethod("GenerateValueField");
        }
    }

    /// <summary>
    /// Internal method to generate and refresh the UI field within the dialog.
    /// Used during initialization and when field properties change to ensure
    /// the UI reflects the current field configuration.
    /// </summary>
    private void GenerateUIFieldInternal()
    {
        if (Owner.Find<Panel>("ValueContainerValue") is Panel valueContainer)
        {
            // Generate appropriate UI element based on field configuration
            var valueItem = MqttClientLogic.GenerateUIItemFromValue(editField);
            
            // Clear existing UI and add the new element
            valueContainer.Children.Clear();
            valueContainer.Add(valueItem);
        }
    }

    /// <summary>
    /// Event handler for data type changes. Updates the field's variable data type
    /// and refreshes the UI to match the new type configuration.
    /// </summary>
    /// <param name="sender">Event sender (data type variable)</param>
    /// <param name="e">Event arguments containing the new data type value</param>
    private void OnDataTypeChanged(object sender, VariableChangeEventArgs e)
    {
        // Update both DataType and ActualDataType to ensure consistency
        editField.ValueVariable.DataType = (NodeId)dataTypeValue.Value;
        editField.ValueVariable.ActualDataType = (NodeId)dataTypeValue.Value;
        
        // Regenerate the UI element to reflect the new data type
        GenerateValueField();
    }

    /// <summary>
    /// Event handler for field kind changes. Adjusts array dimensions and data types
    /// based on the selected field kind (Field, ArrayField, DateTimeField, etc.).
    /// </summary>
    [ExportMethod]
    public void FieldValueChanged()
    {
        switch ((MQTTPayloadFieldKind)editField.FieldKindVariable.Value.Value)
        {
            case MQTTPayloadFieldKind.Field:
                // Standard field: no array dimensions
                editField.ValueVariable.ArrayDimensions = [];
                break;
            case MQTTPayloadFieldKind.ArrayField:
                // Array field: set single dimension with default size
                editField.ValueVariable.ArrayDimensions = [1];
                break;
            case MQTTPayloadFieldKind.LocalTimestampField:
            case MQTTPayloadFieldKind.UTCTimestampField:
                // DateTime fields: no array dimensions, force DateTime data type
                editField.ValueVariable.ArrayDimensions = [];
                dataTypeValue.Value = OpcUa.DataTypes.DateTime;
                break;
        }        
        // Refresh the UI to reflect the field kind changes
        GenerateValueField();
    }

    /// <summary>
    /// Event handler for array dimension changes. Updates the field's variable
    /// array dimensions when the user modifies the array size configuration.
    /// </summary>
    /// <param name="sender">Event sender (array dimension variable)</param>
    /// <param name="e">Event arguments containing the new array dimension value</param>
    private void OnArrayDimensionChanged(object sender, VariableChangeEventArgs e)
    {
        // Update array dimensions with the new size value
        editField.ValueVariable.ArrayDimensions = [(uint)arrayDimension.Value];
    }

    #region Private Fields
    /// <summary>
    /// Variable that holds the current data type selection for the payload field
    /// </summary>
    private IUAVariable dataTypeValue;    
    /// <summary>
    /// Variable that holds the array dimension size configuration
    /// </summary>
    private IUAVariable arrayDimension;    
    /// <summary>
    /// Reference to the MQTT payload field information being edited
    /// </summary>
    private MQTTPayloadFieldInfo editField;    
    /// <summary>
    /// Reference to the base UI field component that contains the field configuration
    /// </summary>
    private MQTTPayloadFieldBase fieldBase;
    #endregion
}
