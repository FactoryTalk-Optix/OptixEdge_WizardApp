#region Using directives
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.SQLiteStore;
using FTOptix.UI;
using FTOptix.HMIProject;
using FTOptix.WebUI;
using FTOptix.NetLogic;
using FTOptix.CoreBase;
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
using FTOptix.EventLogger;
#endregion

public class TagsFilterSelectionLogic : BaseNetLogic
{
    public override void Start()
    {
        if (Owner.GetAlias("TagsFilterDataConfig") is not TagsFilterArguments _tagsFilterArguments)
        {
            Log.Error(LogicObject.BrowseName, "TagsFilterArguments object not found. Please ensure it is properly configured.");
            return;
        }
        tagsFilterArguments = _tagsFilterArguments;
        tagsListBrowseName.AddRange(tagsFilterArguments.SourceListVariableName);
    }

    public override void Stop()
    {
        // Insert code to be executed when the user-defined logic is stopped
    }

    [ExportMethod]
    public void ConfirmAndClose(bool Deselect)
    {
        tagsFilterArguments.Deselect = Deselect;
        tagsFilterArguments.ReturnFilterList = tagsFinded.ToArray();
        var randomID = new Random().Next(1000, 9999);
        while (tagsFilterArguments.IDCounter == randomID)
        {
            randomID = new Random().Next(1000, 9999);
        }
        tagsFilterArguments.IDCounter = randomID;
        (Owner as Dialog)?.Close();
    }

    [ExportMethod]
    public void ValidateRegExPattern(string pattern)
    {
        if (RegexValidator.IsValid(pattern, out string error))
        {
            ApplyRegExToList(pattern);
        }
        else
        {
            LogicObject.GetVariable("Results").Value = -1;
            if (!isErrorRunning)
            {
                new PeriodicTask(SignalError, null, 1000, LogicObject).Start();
            }
        }
    }

    private void ApplyRegExToList(string pattern)
    {
        var matchedTags = RegexValidator.GetMatches(pattern, tagsListBrowseName);
        tagsFinded.Clear();
        tagsFinded.AddRange(matchedTags);
        LogicObject.GetVariable("Results").Value = matchedTags.Count;
    }

    private void SignalError(PeriodicTask task, object arguments)
    {
        const string variableName = "InError";
        isErrorRunning = true;
        if (LogicObject.GetVariable(variableName).Value)
        {
            LogicObject.GetVariable(variableName).Value = false;
            isErrorRunning = false;
            task.Cancel();
            task.Dispose();
            return;
        }
        else
        {
            LogicObject.GetVariable(variableName).Value = true;
        }
    }

    private bool isErrorRunning = false;
    private List<string> tagsListBrowseName = [];
    private List<string> tagsFinded = [];
    private TagsFilterArguments tagsFilterArguments;
}


public static class RegexValidator
{
    /// <summary>
    /// Validates whether the given pattern is a valid Regular Expression.
    /// </summary>
    /// <param name="pattern">The regex pattern to validate.</param>
    /// <param name="error">The error message if invalid, null otherwise.</param>
    /// <returns>True if the pattern is valid, false otherwise.</returns>
    public static bool IsValid(string pattern, out string error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(pattern))
        {
            error = "Pattern cannot be empty.";
            return false;
        }

        try
        {
            Regex.Match(string.Empty, pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
            return true;
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Returns all matching variable names from a list based on the given regex pattern.
    /// </summary>
    /// <param name="pattern">The regex pattern.</param>
    /// <param name="variableNames">The list of variable names to match against.</param>
    /// <param name="matchTimeout">Optional timeout per match (default 1s).</param>
    /// <returns>List of matched variable names.</returns>
    public static List<string> GetMatches(string pattern, IEnumerable<string> variableNames, TimeSpan? matchTimeout = null)
    {
        var timeout = matchTimeout ?? TimeSpan.FromSeconds(1);
        var regex = new Regex(pattern, RegexOptions.None, timeout);

        return variableNames.Where(v => regex.IsMatch(v)).ToList();
    }
}
