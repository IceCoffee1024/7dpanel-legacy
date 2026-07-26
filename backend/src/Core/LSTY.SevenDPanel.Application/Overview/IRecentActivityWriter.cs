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

    public static class RecentActivityWriterGovernanceExtensions
    {
        public static Task RecordAccessListChangedAsync(
            this IRecentActivityWriter writer,
            string actorSubject,
            string list,
            string action,
            string playerId,
            string outcome,
            DateTimeOffset occurredAtUtc,
            CancellationToken cancellationToken)
        {
            if (writer is IServerGovernanceActivityWriter governanceWriter)
            {
                return governanceWriter.RecordAccessListChangedAsync(
                    actorSubject, list, action, playerId, outcome, occurredAtUtc, cancellationToken);
            }

            return Task.CompletedTask;
        }
    }

    public interface IServerGovernanceActivityWriter
    {
        Task RecordAccessListChangedAsync(
            string actorSubject,
            string list,
            string action,
            string playerId,
            string outcome,
            DateTimeOffset occurredAtUtc,
            CancellationToken cancellationToken);
    }
}
