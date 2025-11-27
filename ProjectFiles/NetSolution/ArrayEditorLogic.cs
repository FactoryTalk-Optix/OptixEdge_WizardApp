#region Using directives
using System;
using System.Collections.Generic;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.UI;
using FTOptix.HMIProject;
using FTOptix.NetLogic;
using FTOptix.CoreBase;
using FTOptix.WebUI;
using FTOptix.Core;
using FTOptix.SQLiteStore;
using FTOptix.Store;
using FTOptix.Retentivity;
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
using FTOptix.CommunicationDriver;
using FTOptix.System;
using FTOptix.MQTTClient;
using FTOptix.OPCUAServer;
using FTOptix.DataLogger;
using FTOptix.OPCUAClient;
#endregion

public class ArrayEditorLogic : BaseNetLogic
{
    public override void Start()
    {
        // Get the array variable from the alias mapping system
        if (Owner.GetAlias(CommonLogic.sourceAliasNameMapping.GetValueOrDefault(OptixEdge_WizardApp.ObjectTypes.ArrayEditor)) is IUAVariable _arrayVariable)
        {
            // Validate that the assigned variable is actually an array
            if (_arrayVariable.ArrayDimensions.Length <= 0)
            {
                Log.Error(LogicObject.BrowseName, "The variable assigned to the Array Editor must be an array.");
                (Owner as Dialog)?.Close();
                return;
            }            
            // Store the array variable reference for later use
            arrayVariable = _arrayVariable;                  
            // Start background task to generate the grid model without blocking the UI thread
            new LongRunningTask(GenerateModelForGrid, null, Owner).Start();
        }
    }

    public override void Stop()
    {
        // Insert code to be executed when the user-defined logic is stopped
    }

    /// <summary>
    /// Background task method that generates the grid model for array editing.
    /// Creates individual ArrayEditorElement objects for each array index and establishes
    /// dynamic data binding between UI elements and array values.
    /// </summary>
    /// <param name="task">The long-running task context for cancellation support</param>
    /// <param name="argument">Task argument (not used in this implementation)</param>
    private void GenerateModelForGrid(LongRunningTask task, object argument)
    {
        // Iterate through each element in the first dimension of the array
        for (uint i = 0; i < arrayVariable.ArrayDimensions[0]; i++)
        {
            // Check if task cancellation has been requested to allow graceful shutdown
            if (task.IsCancellationRequested)
            {
                return;
            }            
            // Create a new row element for the array editor grid
            var newRow = InformationModel.Make<ArrayEditorElement>(i.ToString());            
            // Set the position index for proper ordering in the UI
            newRow.Position = i;            
            // Establish bidirectional data binding between the UI element and the array index
            // This allows real-time synchronization between UI changes and variable values
            newRow.ValueVariable.SetDynamicLink(arrayVariable, i, DynamicLinkMode.ReadWrite);            
            // Add the configured row to the model container for display
            LogicObject.GetObject("ModelContainer").Add(newRow);
        }        
        // Clean up the task resources after completion
        task.Dispose();
    }

    #region Private Fields
    /// <summary>
    /// Reference to the array variable being edited. This variable is obtained from the alias
    /// mapping system and validated to ensure it contains array data.
    /// </summary>
    private IUAVariable arrayVariable;
    #endregion
}
