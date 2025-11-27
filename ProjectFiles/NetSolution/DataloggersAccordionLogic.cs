#region Using directives
using System;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.UI;
using FTOptix.HMIProject;
using FTOptix.NetLogic;
#endregion

public class DataloggersAccordionLogic : BaseNetLogic
{
    public override void Start()
    {
        new LongRunningTask(GenerateWidgets, null, LogicObject).Start();
    }

    public override void Stop()
    {
        // Insert code to be executed when the user-defined logic is stopped
    }

    private void GenerateWidgets(BaseTaskWrapper task, object arguments)
    {
        Accordion ownerAccordion = (Accordion)Owner.Owner;
        CommonLogic.Instance.GenerateConfigurationWidgetFromSource(Project.Current.GetObject("Loggers"), ownerAccordion, InformationModel.Get(LogicObject.GetVariable("StationWidgetFolder").Value));
        task.Dispose();
    }
}
