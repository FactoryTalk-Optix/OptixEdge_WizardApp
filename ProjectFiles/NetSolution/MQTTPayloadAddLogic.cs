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
using FTOptix.MQTTClient;
using FTOptix.OPCUAServer;
using FTOptix.DataLogger;
using FTOptix.OPCUAClient;
using FTOptix.Core;
using System.Linq;
using System.Collections.Generic;
#endregion

public class MQTTPayloadAddLogic : BaseNetLogic
{
    public override void Start()
    {
        if (Owner.GetAlias(CommonLogic.sourceAliasNameMapping[OptixEdge_WizardApp.ObjectTypes.MQTTPayloadAdd]) is Accordion accordionContainer)
        {
            this.accordionContainer = accordionContainer;
            // Find the parent publisher to get the index counter
            accordionAncestor = (Accordion)CommonLogic.GetOwner(accordionContainer, OptixEdge_WizardApp.ObjectTypes.MQTTPublisherUIObj);
            GetOwnerDataConfiguration(accordionContainer, out ownerDataConfiguration);
        }
        if (Owner.FindByType<PanelLoader>() is PanelLoader panelLoader)
        {
            contentPanelLoader = panelLoader;
        }
        else
        {
            Log.Error(LogicObject.BrowseName, "Could not find the PanelLoader in the dialog.");
        }
        if ((MQTTPayloadFieldKind)ownerDataConfiguration.FieldKind == MQTTPayloadFieldKind.NestedObjectArray)
        {
            LogicObject.GetVariable("IsArrayNestedItem").Value = true;
            LogicObject.GetVariable("IndexAvailable").Value = ownerDataConfiguration.GetNodesByType<MQTTPayloadFieldInfo>().Any();
            RegenerateComboBoxIndexEntries();
        }
    }

    public override void Stop()
    {
        // Insert code to be executed when the user-defined logic is stopped
    }

    /// <summary>
    /// Adds a new field to the MQTT payload configuration. Validates input, creates the field data structure,
    /// generates the corresponding UI widget, and opens the field configuration dialog if applicable.
    /// </summary>
    [ExportMethod]
    public void AddField(int selectedKind, string keyValue)
    {
        // Check for duplicate field keys to prevent conflicts
        if (ownerDataConfiguration.Get(keyValue) != null)
        {
            NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Warning, "A field with the same key already exists. Please choose another key.");
            Log.Warning(LogicObject.BrowseName, $"A field with the same key {keyValue} already exists. Please choose another key.");
            return;
        }

        // Create the payload field data structure
        var newPayloadData = GenerateNewPayloadFieldInfo((MQTTPayloadFieldKind)selectedKind, keyValue);
        ownerDataConfiguration.Add(newPayloadData);

        // Generate and add the corresponding UI widget
        var newWidget = GenerateNewPayloadFieldUIItem(newPayloadData);
        accordionContainer.Content.Get("Content").Add(newWidget);

        // Handle field types that require additional configuration
        switch ((MQTTPayloadFieldKind)selectedKind)
        {
            case MQTTPayloadFieldKind.Field:
            case MQTTPayloadFieldKind.ArrayField:
            case MQTTPayloadFieldKind.LocalTimestampField:
            case MQTTPayloadFieldKind.UTCTimestampField:
                // Assign field index and open configuration dialog
                newWidget.GetVariable("FieldIndex").Value = GetLastIndexReleased(accordionAncestor) + 1;
                newWidget.GetVariable("CurrentSelectedField").SetDynamicLink(accordionAncestor.GetVariable("CurrentSelectedField"), DynamicLinkMode.ReadWrite);
                SetLastIndexReleased(accordionAncestor, newWidget.GetVariable("FieldIndex").Value);
                if (InformationModel.Get<DialogType>(OptixEdge_WizardApp.ObjectTypes.MQTTPayloadInfoEdit) is DialogType payloadInfoEdit)
                {
                    UICommands.OpenDialog(Session.FindByType<Window>(), payloadInfoEdit, newWidget.NodeId);
                }
                break;
        }
        accordionAncestor.FindByType<StationProps>().GetVariable("EnableSave").Value = true;
        // Close the add field dialog
        (Owner as Dialog).Close();
    }

    /// <summary>
    /// Adds a new empty element to the nested array by finding the first available index.
    /// Handles both sequential addition and filling gaps in the array indices.
    /// </summary>
    [ExportMethod]
    public void AddEmptyArrayNestedElement()
    {
        int newIndex = LogicObject.GetVariable("IndexAvailable").Value ? GetFirstAvaiableIndex() : 0;
        // Create the new array element with the found index
        var newPayloadData = GenerateNewPayloadFieldInfo(MQTTPayloadFieldKind.NestedObjectArrayElement, $"[{newIndex}]");

        // Add to the configuration
        ownerDataConfiguration.Add(newPayloadData);

        // Generate and add the corresponding UI widget
        var newWidget = GenerateNewPayloadFieldUIItem(newPayloadData);
        accordionContainer.Content.Get("Content").Add(newWidget);

        // Update the IndexAvailable flag
        LogicObject.GetVariable("IndexAvailable").Value = true;
        RegenerateComboBoxIndexEntries();
        accordionAncestor.FindByType<StationProps>().GetVariable("EnableSave").Value = true;
        NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Info, $"New array element [{newIndex}] added. Please save to finalize the changes.");
    }

    [ExportMethod]
    public void CloneArrayIndex(uint selectedIndex)
    {
        // Get the source configuration to clone based on selected index
        if (ownerDataConfiguration.Get<MQTTPayloadFieldInfo>($"[{selectedIndex}]") is not MQTTPayloadFieldInfo sourceConfiguration)
        {
            Log.Error(LogicObject.BrowseName, "Could not find the source configuration to clone.");
            return;
        }

        // Clone the selected configuration
        var newConfiguration = LogicObject.Context.NodeFactory.CloneNode(sourceConfiguration, ownerDataConfiguration.NodeId.NamespaceIndex, NamingRuleType.None);

        // Determine the new index for the cloned element
        int newIndex = GetFirstAvaiableIndex();
        newConfiguration.BrowseName = $"[{newIndex}]";
        newConfiguration.Key = $"[{newIndex}]";

        // Add to the configuration
        ownerDataConfiguration.Add(newConfiguration);

        // Generate and add the corresponding UI widget
        var newWidget = GenerateNewPayloadFieldUIItem(newConfiguration);
        accordionContainer.Content.Get("Content").Add(newWidget);

        // Generate and add the corresponding UI sub-widget
        new LongRunningTask(RegenerateSubWidgetFromClonedNode, new object[] { newConfiguration, newWidget, accordionAncestor }, newWidget).Start();
        RegenerateComboBoxIndexEntries();
        accordionAncestor.FindByType<StationProps>().GetVariable("EnableSave").Value = true;
        NotificationsMessageHandlerLogic.Instance.RequestToastNotification(ToastBannerNotificationLevel.Info, $"Array element [{newIndex}] cloned successfully from element [{selectedIndex}]. Please save to finalize the changes.");
    }

    /// <summary>
    /// Generates the appropriate UI widget for a payload field based on its type.
    /// Creates either a basic field widget or a nested object widget depending on the field kind.
    /// </summary>
    /// <param name="newPayloadData">The payload field data structure to create a widget for</param>
    /// <returns>UI item widget configured for the specified field type</returns>
    private Item GenerateNewPayloadFieldUIItem(MQTTPayloadFieldInfo newPayloadData)
    {
        Item newItem = null;

        // Create appropriate widget type based on field kind
        switch ((MQTTPayloadFieldKind)newPayloadData.FieldKind)
        {
            case MQTTPayloadFieldKind.Field:
            case MQTTPayloadFieldKind.ArrayField:
            case MQTTPayloadFieldKind.LocalTimestampField:
            case MQTTPayloadFieldKind.UTCTimestampField:
                // Create basic field widget for simple data types
                newItem = InformationModel.MakeObject<MQTTPayloadFieldBase>(newPayloadData.Key);
                newItem.SetAlias(CommonLogic.sourceAliasNameMapping.GetValueOrDefault(OptixEdge_WizardApp.ObjectTypes.MQTTPayloadFieldBase), newPayloadData);
                newItem.Find<Panel>("ValueContainer").Add(MqttClientLogic.GenerateUIItemFromValue(newPayloadData));
                break;
            case MQTTPayloadFieldKind.VariablesCollection:
            case MQTTPayloadFieldKind.NestedObject:
            case MQTTPayloadFieldKind.NestedObjectArray:
            case MQTTPayloadFieldKind.NestedObjectArrayElement:
                // Create object widget for complex nested structures
                newItem = InformationModel.MakeObject<MQTTPayloadObject>(newPayloadData.Key);
                newItem.SetAlias(CommonLogic.sourceAliasNameMapping.GetValueOrDefault(OptixEdge_WizardApp.ObjectTypes.MQTTPayloadObject), newPayloadData);
                if ((MQTTPayloadFieldKind)newPayloadData.FieldKind == MQTTPayloadFieldKind.VariablesCollection)
                {
                    var tagViewer = InformationModel.MakeObject<TagViewer>("TagViewer");
                    tagViewer.SetAlias(CommonLogic.editAliasNameMapping.GetValueOrDefault(OptixEdge_WizardApp.ObjectTypes.TagViewer), newItem);
                    ((MQTTPayloadObject)newItem).Content.Add(tagViewer);
                }
                break;
        }

        // Set localized display name for the widget
        newItem.DisplayName = new LocalizedText(newPayloadData.BrowseName, Session.ActualLocaleId);
        return newItem;
    }

    /// <summary>
    /// Creates a new payload field data structure with appropriate configuration based on field type.
    /// Sets up the basic properties and configures data types for different field kinds.
    /// </summary>
    /// <param name="selectedItem">Selected field type from the dialog</param>
    /// <param name="keyValue">Text box containing the field key/name</param>
    /// <returns>Configured MQTTPayloadFieldInfo object</returns>
    private MQTTPayloadFieldInfo GenerateNewPayloadFieldInfo(MQTTPayloadFieldKind selectedKind, string keyValue)
    {
        // Create new payload field data object
        var newPayloadData = InformationModel.MakeObject<MQTTPayloadFieldInfo>(keyValue);

        // Set basic field properties
        newPayloadData.FieldKind = selectedKind;
        newPayloadData.Key = keyValue;
        newPayloadData.DisplayName = new LocalizedText(keyValue, Session.ActualLocaleId);

        // Configure data type and structure based on field kind
        switch (selectedKind)
        {
            case MQTTPayloadFieldKind.Field:
                // String data type for simple scalar fields
                newPayloadData.ValueVariable.DataType = OpcUa.DataTypes.String;
                break;
            case MQTTPayloadFieldKind.ArrayField:
                // String data type with array dimensions for array fields
                newPayloadData.ValueVariable.DataType = OpcUa.DataTypes.String;
                newPayloadData.ValueVariable.ArrayDimensions = [1]; // Single dimension array with default size
                break;
            case MQTTPayloadFieldKind.LocalTimestampField:
            case MQTTPayloadFieldKind.UTCTimestampField:
                // DateTime data type for timestamp fields (always scalar)
                newPayloadData.ValueVariable.DataType = OpcUa.DataTypes.DateTime;
                break;
        }

        return newPayloadData;
    }

    /// <summary>
    /// Retrieves the data configuration container based on the accordion container type (nested object or main publisher).
    /// </summary>
    /// <param name="accordionContainer">The accordion container to get configuration from</param>
    /// <param name="ownerFieldInfo">Output: The data configuration node</param>
    private static void GetOwnerDataConfiguration(Accordion accordionContainer, out MQTTPayloadFieldInfo ownerFieldInfo)
    {
        if (accordionContainer is MQTTPayloadObject)
        {
            // Handle nested object case: get configuration from object alias
            ownerFieldInfo = (MQTTPayloadFieldInfo)accordionContainer.GetAlias(CommonLogic.sourceAliasNameMapping.GetValueOrDefault(OptixEdge_WizardApp.ObjectTypes.MQTTPayloadObject));
        }
        else
        {
            // Handle main publisher case: get configuration directly
            var ownerDataConfiguration = (MQTTPublisherDataConfiguration)accordionContainer.GetAlias(CommonLogic.sourceAliasNameMapping.GetValueOrDefault(OptixEdge_WizardApp.ObjectTypes.MQTTPublisherUIObj));
            ownerFieldInfo = ownerDataConfiguration.PayloadStructure.Get<MQTTPayloadFieldInfo>("root");
        }
    }

    private static uint GetLastIndexReleased(Accordion accordion)
    {
        return accordion.GetVariable("LastIndexReleased").Value;
    }

    private static void SetLastIndexReleased(Accordion accordion, uint value)
    {
        accordion.GetVariable("LastIndexReleased").Value = value;
    }

    private int GetFirstAvaiableIndex()
    {
        int newIndex;
        // Get all existing array elements with their indices
        var usedIndices = new HashSet<int>();

        // Extract indices from BrowseName format [x]
        foreach (var element in ownerDataConfiguration.GetNodesByType<MQTTPayloadFieldInfo>())
        {
            var browseName = element.BrowseName;
            if (browseName.StartsWith('[') && browseName.EndsWith(']'))
            {
                var indexStr = browseName.Substring(1, browseName.Length - 2);
                if (int.TryParse(indexStr, out int index))
                {
                    usedIndices.Add(index);
                }
            }
        }

        // Find the first available index (fill gaps first, then continue sequentially)
        newIndex = 0;
        while (usedIndices.Contains(newIndex))
        {
            newIndex++;
        }

        return newIndex;
    }

    private void RegenerateComboBoxIndexEntries()
    {
        if (LogicObject.GetObject("IndexCollection") is IUAObject indexContainer)
        {
            indexContainer.Children.Clear();
            for (uint i = 0; i < ownerDataConfiguration.GetNodesByType<MQTTPayloadFieldInfo>().Count(); i++)
            {
                var indexEntry = InformationModel.MakeVariable(i.ToString(), OpcUa.DataTypes.UInt32);
                indexEntry.Value = i;
                indexContainer.Add(indexEntry);
            }
        }
    }

    private void RegenerateSubWidgetFromClonedNode(LongRunningTask task, object arguments)
    {
        object[] args = (object[])arguments;
        var payloadRoot = (MQTTPayloadFieldInfo)args[0];
        var accordionContainer = (Accordion)args[1];
        var accordionAncestor = (Accordion)args[2];
        var fieldIndex = GetLastIndexReleased(accordionAncestor);
        // Call unsubscribe UI observer method for disable the save management on user edit
        accordionAncestor.Find<NetLogicObject>("UIFieldParameterObserverLogic")?.ExecuteMethod("UnsubscribeObserverSingleControl", [accordionContainer.NodeId]);
        // Regenerate UI from payload structure
        foreach (var child in payloadRoot.GetNodesByType<MQTTPayloadFieldInfo>())
        {
            MqttClientLogic.GeneratePayloadWidgetFromConfiguration(child, accordionContainer.Content.Get("Content"), ref fieldIndex, accordionAncestor.GetVariable("CurrentSelectedField"));
        }
        SetLastIndexReleased(accordionAncestor, fieldIndex);
        // Call subscribe UI observer method for enable the save management on user edit
        accordionAncestor.Find<NetLogicObject>("UIFieldParameterObserverLogic")?.ExecuteMethod("SubscribeObserverSingleControl", [accordionContainer.NodeId]);
        task.Dispose();
    }


    private MQTTPayloadFieldInfo ownerDataConfiguration;
    private PanelLoader contentPanelLoader;
    private Accordion accordionContainer;
    private Accordion accordionAncestor;
}
