#region Using directives
using System;
using UAManagedCore;
using FTOptix.UI;
using FTOptix.HMIProject;
using FTOptix.NetLogic;
using FTOptix.AuditSigning;
using FTOptix.NativeUI;
#endregion

public class OPCUAServerAccordionLogic : BaseNetLogic
{
    public override void Start()
    {
        affinityId = LogicObject.Context.AssignAffinityId();
        Accordion ownerAccordion = (Accordion)Owner.Owner;
        if (ownerAccordion.Find("AddButton") is AddButton addButton && ownerAccordion.Get("Content/Content") is ColumnLayout contentWidgetContainer)
        {
            eventRegistration = contentWidgetContainer.RegisterEventObserver(new AccodionWidgetObserver(addButton), EventType.ForwardReferenceChanged, affinityId);
        }
        CommonLogic.Instance.GenerateConfigurationWidgetFromSource(Project.Current.GetObject(CommonLogic.OPCUAServerFolderPath), ownerAccordion, InformationModel.Get(LogicObject.GetVariable("StationWidgetFolder").Value));
    }

    public override void Stop()
    {
        eventRegistration?.Dispose();
    }

    uint affinityId = 0;
    IEventRegistration eventRegistration;
}

public class AccodionWidgetObserver(AddButton addButton) : IReferenceObserver
{
    public void OnReferenceAdded(IUANode sourceNode, IUANode targetNode, NodeId referenceTypeId, ulong senderId)
    {
        if (targetNode is OPCUAServerStationUIObj)
        {
            _addButton.Enabled = false;
        }
    }

    public void OnReferenceRemoved(IUANode sourceNode, IUANode targetNode, NodeId referenceTypeId, ulong senderId)
    {
        if (targetNode is OPCUAServerStationUIObj)
        {
            _addButton.Enabled = true;
        }
    }

    private AddButton _addButton = addButton;
}
