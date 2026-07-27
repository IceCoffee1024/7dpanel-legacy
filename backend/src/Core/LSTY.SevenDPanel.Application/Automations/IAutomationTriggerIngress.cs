namespace LSTY.SevenDPanel.Application.Automations
{
    public interface IAutomationTriggerIngress
    {
        bool TryWrite(AutomationTriggerSnapshot trigger);
    }
}
