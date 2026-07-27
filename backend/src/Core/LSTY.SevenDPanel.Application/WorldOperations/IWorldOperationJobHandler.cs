using System;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application.WorldOperations
{
    public sealed record WorldOperationJobCompletion(
        WorldOperationStatus Status,
        string? ErrorCode,
        WorldOperationProgress? Progress);

    public interface IWorldOperationJobHandler
    {
        Task<WorldOperationJobCompletion> ExecuteAsync(
            Guid jobId,
            CancellationToken cancellationToken);
    }
}
