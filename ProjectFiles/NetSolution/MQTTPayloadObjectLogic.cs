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
using System.Collections.Generic;
using System.Linq;
#endregion

public class MQTTPayloadObjectLogic : BaseNetLogic
{
    public override void Start()
    {
        var accordionAncestor = CommonLogic.GetOwner(Owner, OptixEdge_WizardApp.ObjectTypes.MQTTPublisherUIObj) as MQTTPublisherUIObj;
        var stationProps = accordionAncestor.Header.FindByType<StationProps>();
        enableSaveParameter = stationProps.GetVariable("EnableSave");
    }

    public override void Stop()
    {
        // Insert code to be executed when the user-defined logic is stopped
    }

    [ExportMethod]
    public void GenerateTagsList()
    {
        if (Project.Current.Get("Model").Get($"{Owner.Owner.Owner.BrowseName}/{Owner.Owner.BrowseName}") is not IUAObject temporaryFolder)
        {
            NotificationsMessageHandlerLogic.Instance.RequestBannerNotification(ToastBannerNotificationLevel.Warning, $"Cannot add variables to payload element {Owner.Owner.BrowseName} - Check application logs");
            Log.Warning(LogicObject.BrowseName, $"Missing temporary folder for variable collection {Owner.Owner.BrowseName}");
            return;
        }
        if (Owner.Owner.GetAlias(CommonLogic.sourceAliasNameMapping.GetValueOrDefault(OptixEdge_WizardApp.ObjectTypes.MQTTPayloadObject)) is not MQTTPayloadFieldInfo fieldInfoData)
        {
            NotificationsMessageHandlerLogic.Instance.RequestBannerNotification(ToastBannerNotificationLevel.Warning, $"Cannot add variables to payload element {Owner.Owner.BrowseName} - Check application logs");
            Log.Warning(LogicObject.BrowseName, $"Missing MQTTPayloadFieldInfo for variable collection {Owner.Owner.BrowseName}");
            return;
        }
        var dataConfiguration = (MQTTPublisherDataConfiguration)CommonLogic.GetOwner(fieldInfoData, OptixEdge_WizardApp.ObjectTypes.MQTTPublisherDataConfiguration);
        int createdTags = 0;
        var dataPath = $"{fieldInfoData.Owner.BrowseName}_{fieldInfoData.Key}";
        fieldInfoData.ValueDataVariablePath = dataPath;
        var publisherTagFolder = dataConfiguration.Data.GetObject(dataPath);
        if (publisherTagFolder == null)
        {
            publisherTagFolder = InformationModel.MakeObject(dataPath);
            dataConfiguration.Data.Add(publisherTagFolder);
        }
        foreach (var sourceFieldFolder in temporaryFolder.GetNodesByType<Folder>())
        {
            var dataFromTagImporter = sourceFieldFolder.GetNodesByType<TagCustomGridRowData>();
            foreach (var tagData in dataFromTagImporter.Where(x => x.Checked))
            {
                string variableName = $"{sourceFieldFolder.BrowseName}.{tagData.VariableName}";
                IUAVariable targetTag = publisherTagFolder.GetVariable(variableName);
                if (targetTag == null)
                {
                    if (tagData.VariableIsArray)
                    {
                        targetTag = InformationModel.MakeVariable(variableName, tagData.VariableDataTypeNodeId, tagData.VariableArrayDimension);
                    }
                    else
                    {
                        targetTag = InformationModel.MakeVariable(variableName, tagData.VariableDataTypeNodeId);
                    }
                    targetTag.Description = new(tagData.VariableComment, Session.ActualLocaleId);
                    publisherTagFolder.Add(targetTag);
                    if (InformationModel.GetVariable(tagData.VariableNodeId) is IUAVariable sourceTag)
                    {
                        targetTag.SetDynamicLink(sourceTag);
                    }
                    else
                    {
                        Log.Warning(LogicObject.BrowseName, $"sourceTag {tagData.VariableName} not found!");
                    }
                    createdTags++;
                }
            }
            DeleteMissingTag(fieldInfoData, dataConfiguration, dataFromTagImporter.Where(x => !x.Checked).ToList());
        }
        enableSaveParameter.Value = true;
    }

    private int DeleteMissingTag(MQTTPayloadFieldInfo fieldInfoData, MQTTPublisherDataConfiguration dataConfiguration, List<TagCustomGridRowData> tagDatasUnchecked)
    {
        int deletedTagsCounter = 0;
        var dataPath = $"{fieldInfoData.Owner.BrowseName}_{fieldInfoData.Key}";
        var dataNode = dataConfiguration.Data.GetObject(dataPath);
        List<TagDataImported> tagsImported = CommonLogic.ReadTagsFromSourceDataCollector(dataNode, (MQTTPayloadObject)Owner.Owner);
        foreach (var tagData in tagsImported.Where(x => tagDatasUnchecked.Exists(y => y.VariableName == x.BrowseName)))
        {
            InformationModel.Get(tagData.NodeId)?.Delete();
            deletedTagsCounter++;
        }
        return deletedTagsCounter;
    }
    
    IUAVariable enableSaveParameter;
}
