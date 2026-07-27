using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Runtime;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Application.Community;
using LSTY.SevenDPanel.Application.Jobs;
using LSTY.SevenDPanel.Domain.Jobs;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Community
{
    public sealed class VoteKickTarget
    {
        public VoteKickTarget(
            int entityId,
            string crossplatformId,
            PlayerPlatformIdentity platformIdentity)
        {
            if (entityId < 0) throw new ArgumentOutOfRangeException(nameof(entityId));
            if (string.IsNullOrWhiteSpace(crossplatformId))
                throw new ArgumentException("A cross-platform identity is required.", nameof(crossplatformId));
            EntityId = entityId;
            CrossplatformId = crossplatformId.Trim();
            PlatformIdentity = platformIdentity ?? throw new ArgumentNullException(nameof(platformIdentity));
        }

        public int EntityId { get; }
        public string CrossplatformId { get; }
        public PlayerPlatformIdentity PlatformIdentity { get; }
    }

    public interface ICommunityVoteKickTargetResolver
    {
        Task<VoteKickTarget?> ResolveAsync(
            string crossplatformId,
            CancellationToken cancellationToken);
    }

    public sealed class SevenDaysCommunityVoteKickTargetResolver : ICommunityVoteKickTargetResolver
    {
        private static readonly TimeSpan DispatchTimeout = TimeSpan.FromSeconds(5);

        public Task<VoteKickTarget?> ResolveAsync(
            string crossplatformId,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(crossplatformId))
                throw new ArgumentException("A cross-platform identity is required.", nameof(crossplatformId));
            var stableIdentity = crossplatformId.Trim();
            return GameThreadDispatcher.Enqueue(
                "7DPanel.Community.ResolveVoteKickTarget",
                () => ResolveOnGameThread(stableIdentity),
                DispatchTimeout,
                cancellationToken);
        }

        private static VoteKickTarget? ResolveOnGameThread(string crossplatformId)
        {
            var clients = global::ConnectionManager.Instance?.Clients?.List;
            if (clients == null) return null;
            VoteKickTarget? match = null;
            foreach (var client in clients)
            {
                if (client == null || !string.Equals(
                        client.CrossplatformId?.CombinedString,
                        crossplatformId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (match != null)
                    throw new InvalidOperationException("Multiple online clients share one cross-platform identity.");
                var platform = client.PlatformId;
                if (platform == null ||
                    string.IsNullOrWhiteSpace(platform.CombinedString) ||
                    string.IsNullOrWhiteSpace(platform.PlatformIdentifierString))
                {
                    return null;
                }

                match = new VoteKickTarget(
                    client.entityId,
                    crossplatformId,
                    new PlayerPlatformIdentity(
                        platform.CombinedString,
                        platform.PlatformIdentifierString));
            }

            return match;
        }
    }

    public sealed class CommunityVoteActionAdapter : ICommunityVoteActionPort
    {
        private readonly KickPlayerUseCase kickPlayer;
        private readonly ICommunityVoteKickTargetResolver kickTargets;
        private readonly IJobSubmissionStore jobSubmissions;
        private readonly IJobStore jobs;
        private readonly Func<DateTimeOffset> utcClock;

        public CommunityVoteActionAdapter(
            KickPlayerUseCase kickPlayer,
            IJobSubmissionStore jobSubmissions,
            IJobStore jobs,
            Func<DateTimeOffset> utcClock)
            : this(
                kickPlayer,
                new SevenDaysCommunityVoteKickTargetResolver(),
                jobSubmissions,
                jobs,
                utcClock)
        {
        }

        public CommunityVoteActionAdapter(
            KickPlayerUseCase kickPlayer,
            ICommunityVoteKickTargetResolver kickTargets,
            IJobSubmissionStore jobSubmissions,
            IJobStore jobs,
            Func<DateTimeOffset> utcClock)
        {
            this.kickPlayer = kickPlayer ?? throw new ArgumentNullException(nameof(kickPlayer));
            this.kickTargets = kickTargets ?? throw new ArgumentNullException(nameof(kickTargets));
            this.jobSubmissions = jobSubmissions ?? throw new ArgumentNullException(nameof(jobSubmissions));
            this.jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
            this.utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
        }

        public Task<VoteActionResult> ExecuteAsync(
            VoteActionCommand command,
            CancellationToken cancellationToken)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            return command.Kind == VoteKind.Kick
                ? ExecuteKickAsync(command, cancellationToken)
                : ExecuteRestartAsync(command, cancellationToken);
        }

        private async Task<VoteActionResult> ExecuteKickAsync(
            VoteActionCommand command,
            CancellationToken cancellationToken)
        {
            if (command.TargetCrossplatformId == null)
                return VoteActionResult.Failed("vote_target_missing");
            try
            {
                var target = await kickTargets.ResolveAsync(
                        command.TargetCrossplatformId,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (target == null) return VoteActionResult.Failed("vote_target_offline");

                var result = await kickPlayer.ExecuteAsync(
                        new KickPlayerRequest(
                            "vote:" + command.RoundId,
                            target.EntityId,
                            target.PlatformIdentity,
                            "Approved community vote",
                            true),
                        cancellationToken)
                    .ConfigureAwait(false);
                return VoteActionResult.Succeeded(result.OperationId, null);
            }
            catch (PlayerNotOnlineException)
            {
                return VoteActionResult.Failed("vote_target_offline");
            }
            catch (PlayerIdentityChangedException)
            {
                return VoteActionResult.Failed("vote_target_identity_changed");
            }
            catch (PlayerActionBusyException)
            {
                return VoteActionResult.Failed("vote_kick_busy");
            }
            catch (AuditUnavailableException)
            {
                return VoteActionResult.Failed("vote_kick_audit_unavailable");
            }
            catch (TimeoutException)
            {
                return VoteActionResult.ResultUnknown("vote_kick_result_unknown");
            }
            catch (OperationCanceledException)
            {
                return VoteActionResult.ResultUnknown("vote_kick_result_unknown");
            }
            catch (AuditCompletionUnavailableException)
            {
                return VoteActionResult.ResultUnknown("vote_kick_result_unknown");
            }
            catch
            {
                return VoteActionResult.ResultUnknown("vote_kick_result_unknown");
            }
        }

        private Task<VoteActionResult> ExecuteRestartAsync(
            VoteActionCommand command,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromResult(VoteActionResult.ResultUnknown(
                    "vote_restart_result_unknown"));
            }

            try
            {
                if (HasActiveRestart())
                    return Task.FromResult(VoteActionResult.Failed("vote_restart_already_pending"));
                var now = utcClock();
                if (now.Offset != TimeSpan.Zero)
                    throw new InvalidOperationException("vote_action_clock_not_utc");
                var scheduleId = StableGuid("vote-restart:" + command.RoundId);
                var job = jobSubmissions.Enqueue(
                    new NewJob(
                        JobKind.ScheduledRestart,
                        "vote:" + command.RoundId,
                        null,
                        "vote-restart:" + command.RoundId,
                        command.CorrelationId,
                        now),
                    new ScheduledRestartPayload(scheduleId, 60));
                return Task.FromResult(VoteActionResult.Succeeded(
                    null,
                    job.Id.ToString("D")));
            }
            catch (InvalidOperationException exception) when (
                string.Equals(exception.Message, "job_idempotency_conflict", StringComparison.Ordinal))
            {
                return Task.FromResult(VoteActionResult.Failed("vote_restart_conflict"));
            }
            catch
            {
                return Task.FromResult(VoteActionResult.ResultUnknown(
                    "vote_restart_result_unknown"));
            }
        }

        private bool HasActiveRestart() =>
            HasRestart(JobStatus.Queued) ||
            HasRestart(JobStatus.Running) ||
            HasRestart(JobStatus.PendingRestart);

        private bool HasRestart(JobStatus status) =>
            jobs.List(new JobQuery(
                    1,
                    JobKind.ScheduledRestart,
                    status,
                    null,
                    null,
                    null))
                .Items.Any();

        private static Guid StableGuid(string value)
        {
            using var sha256 = SHA256.Create();
            var digest = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
            var bytes = new byte[16];
            Array.Copy(digest, bytes, bytes.Length);
            return new Guid(bytes);
        }
    }
}
