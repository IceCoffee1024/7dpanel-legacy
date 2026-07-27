namespace LSTY.SevenDPanel.Application.WorldOperations
{
    public interface IWorldOperationJobBridge
    {
        WorldOperationReceipt Enqueue(WorldOperationIntent intent);
        WorldOperationRecord Get(string operationId);
        WorldOperationPage Query(WorldOperationQuery query);
        bool RequestCancellation(string operationId, string actorSubject);
    }
}
