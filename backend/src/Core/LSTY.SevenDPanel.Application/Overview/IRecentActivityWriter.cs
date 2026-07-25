using System;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application
{
    public interface IRecentActivityWriter
    {
        Task RecordPanelLoginSucceededAsync(
            string actorSubject,
            string actorDisplayName,
            DateTimeOffset occurredAtUtc,
            CancellationToken cancellationToken);

        Task RecordPlayerJoinedAsync(
            string playerDisplayName,
            DateTimeOffset occurredAtUtc,
            CancellationToken cancellationToken);

        Task RecordPlayerLeftAsync(
            string playerDisplayName,
            DateTimeOffset occurredAtUtc,
            CancellationToken cancellationToken);

        Task RecordRestartScriptStartedAsync(
            string actorSubject,
            DateTimeOffset occurredAtUtc,
            CancellationToken cancellationToken);

        Task RecordShutdownRequestedAsync(
            string actorSubject,
            DateTimeOffset occurredAtUtc,
            CancellationToken cancellationToken);

        Task RecordServerOperationFailedAsync(
            string actorSubject,
            string operationCode,
            string failureCode,
            DateTimeOffset occurredAtUtc,
            CancellationToken cancellationToken);
    }
}
