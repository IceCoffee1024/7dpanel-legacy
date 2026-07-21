namespace LSTY.SevenDPanel.Hosting
{
    public interface IPanelRuntimeStatus
    {
        ModHostState State { get; }
        GameReadinessState GameReadiness { get; }
    }
}
