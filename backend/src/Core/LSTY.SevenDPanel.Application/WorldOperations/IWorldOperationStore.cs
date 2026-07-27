namespace LSTY.SevenDPanel.Application.WorldOperations
{
    public interface IWorldOperationStore
    {
        WorldOperationRecord Get(string operationId);
        WorldOperationPage Query(WorldOperationQuery query);
    }

    public interface IWorldOperationExecutionStore
    {
        WorldOperationExecutionRecord ReadForExecution(System.Guid jobId);

        void MarkRollbackFailed(
            System.Guid jobId,
            string errorCode,
            System.DateTimeOffset failedAtUtc);
    }

    public interface IWorldOperationRecoveryStore
    {
        int RecoverRunning(System.DateTimeOffset recoveredAtUtc);
    }
}
