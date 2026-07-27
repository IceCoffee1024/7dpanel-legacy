namespace LSTY.SevenDPanel.Domain.Jobs
{
    public enum JobStatus
    {
        Queued,
        Running,
        PendingRestart,
        Succeeded,
        Failed,
        Cancelled,
        Interrupted,
        ResultUnknown
    }
}
