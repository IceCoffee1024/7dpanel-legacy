using System;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application
{
    public sealed class RestartServerUseCase
    {
        private readonly IRestartScriptLauncher launcher;
        private readonly IServerOperationAuditTrail auditTrail;
        private readonly IRecentActivityWriter recentActivity;
        private readonly Func<string> operationIdFactory;
        private readonly Func<DateTimeOffset> utcClock;
        private readonly IServerOperationStore operationStore;
        private readonly ServerOperationProcessInstance processInstance;
        private readonly TimeSpan completionWindow;
        private int inFlight;

        public RestartServerUseCase(
            IRestartScriptLauncher launcher,
            IServerOperationAuditTrail auditTrail,
            IRecentActivityWriter recentActivity)
            : this(
                launcher,
                auditTrail,
                recentActivity,
                () => Guid.NewGuid().ToString("N"),
                () => DateTimeOffset.UtcNow,
                new InMemoryServerOperationStore(),
                new ServerOperationProcessInstance(),
                TimeSpan.FromMinutes(5))
        {
        }

        internal RestartServerUseCase(
            IRestartScriptLauncher launcher,
            IServerOperationAuditTrail auditTrail,
            IRecentActivityWriter recentActivity,
            Func<string> operationIdFactory,
            Func<DateTimeOffset> utcClock)
            : this(launcher, auditTrail, recentActivity, operationIdFactory, utcClock,
                new InMemoryServerOperationStore(), new ServerOperationProcessInstance(), TimeSpan.FromMinutes(5))
        {
        }

        public RestartServerUseCase(
            IRestartScriptLauncher launcher,
            IServerOperationAuditTrail auditTrail,
            IRecentActivityWriter recentActivity,
            IServerOperationStore operationStore,
            ServerOperationProcessInstance processInstance)
            : this(launcher, auditTrail, recentActivity, () => Guid.NewGuid().ToString("N"),
                () => DateTimeOffset.UtcNow, operationStore, processInstance, TimeSpan.FromMinutes(5))
        {
        }

        internal RestartServerUseCase(
            IRestartScriptLauncher launcher,
            IServerOperationAuditTrail auditTrail,
            IRecentActivityWriter recentActivity,
            Func<string> operationIdFactory,
            Func<DateTimeOffset> utcClock,
            IServerOperationStore operationStore,
            ServerOperationProcessInstance processInstance,
            TimeSpan completionWindow)
        {
            this.launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
            this.auditTrail = auditTrail ?? throw new ArgumentNullException(nameof(auditTrail));
            this.recentActivity = recentActivity ?? throw new ArgumentNullException(nameof(recentActivity));
            this.operationIdFactory = operationIdFactory ?? throw new ArgumentNullException(nameof(operationIdFactory));
            this.utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
            this.operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
            this.processInstance = processInstance ?? throw new ArgumentNullException(nameof(processInstance));
            if (completionWindow <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(completionWindow));
            this.completionWindow = completionWindow;
        }

        public Task<ServerOperationResult> ExecuteAsync(
            string actorSubject,
            bool confirmed,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(actorSubject))
                throw new ArgumentException("An actor subject is required.", nameof(actorSubject));
            if (!confirmed) throw new ServerOperationConfirmationRequiredException();
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.CompareExchange(ref inFlight, 1, 0) != 0)
                throw new ServerOperationBusyException();

            try
            {
                var operationId = operationIdFactory();
                var requestedAtUtc = utcClock();
                CreateQueued(operationId, actorSubject, requestedAtUtc);
                CreatePending(operationId, actorSubject, requestedAtUtc);
                if (!operationStore.TryTransition(operationId, ServerOperationLifecycleStatus.Queued,
                        ServerOperationLifecycleStatus.Running, requestedAtUtc, null))
                {
                    throw new InvalidOperationException("server_operation_transition_failed");
                }

                DateTimeOffset startedAtUtc;
                try
                {
                    startedAtUtc = launcher.StartConfiguredScript();
                    Volatile.Write(ref inFlight, 0);
                }
                catch (ServerOperationFailedException exception)
                {
                    RecordFailure(operationId, actorSubject, exception.FailureCode, ServerOperationLifecycleStatus.Failed);
                    throw;
                }
                catch
                {
                    const string failureCode = ServerOperationCodeContract.RestartScriptStartFailed;
                    RecordFailure(operationId, actorSubject, failureCode, ServerOperationLifecycleStatus.Failed);
                    throw new ServerOperationFailedException(failureCode);
                }

                var auditStatus = TryMarkStarted(operationId, startedAtUtc)
                    ? "recorded"
                    : "audit_degraded";
                RecordActivity(() => recentActivity.RecordRestartScriptStartedAsync(
                    actorSubject,
                    startedAtUtc,
                    CancellationToken.None));

                return Task.FromResult(new ServerOperationResult(
                    operationId,
                    ServerOperationCodeContract.RestartScript,
                    "restart_script_started",
                    requestedAtUtc,
                    startedAtUtc,
                    auditStatus));
            }
            finally
            {
                Volatile.Write(ref inFlight, 0);
            }
        }

        private void CreatePending(
            string operationId,
            string actorSubject,
            DateTimeOffset requestedAtUtc)
        {
            try
            {
                auditTrail.CreatePending(new ServerOperationAuditIntent(
                    operationId,
                    ServerOperationCodeContract.RestartScript,
                    actorSubject,
                    requestedAtUtc));
            }
            catch (Exception exception)
            {
                throw new ServerOperationAuditUnavailableException(exception);
            }
        }

        private void CreateQueued(string operationId, string actorSubject, DateTimeOffset requestedAtUtc)
        {
            operationStore.CreateQueued(new ServerOperationSnapshot(
                operationId,
                ServerOperationCodeContract.RestartScript,
                ServerOperationLifecycleStatus.Queued,
                actorSubject,
                processInstance.Value,
                requestedAtUtc,
                null,
                null,
                requestedAtUtc.Add(completionWindow),
                null,
                "recorded"));
        }

        private void RecordFailure(
            string operationId,
            string actorSubject,
            string failureCode,
            ServerOperationLifecycleStatus status)
        {
            var failedAtUtc = utcClock();
            operationStore.TryTransition(operationId, ServerOperationLifecycleStatus.Running, status, failedAtUtc, failureCode);
            try
            {
                auditTrail.TryMarkFailed(new ServerOperationAuditFailure(
                    operationId,
                    failedAtUtc,
                    failureCode));
            }
            catch
            {
            }

            RecordActivity(() => recentActivity.RecordServerOperationFailedAsync(
                actorSubject,
                ServerOperationCodeContract.RestartScript,
                failureCode,
                failedAtUtc,
                CancellationToken.None));
        }

        private bool TryMarkStarted(string operationId, DateTimeOffset startedAtUtc)
        {
            try
            {
                var recorded = auditTrail.TryMarkStarted(operationId, startedAtUtc);
                if (!recorded)
                    TryMarkAuditDegraded(operationId);
                return recorded;
            }
            catch
            {
                TryMarkAuditDegraded(operationId);
                return false;
            }
        }

        private void TryMarkAuditDegraded(string operationId)
        {
            try
            {
                operationStore.TrySetAuditStatus(operationId, ServerOperationLifecycleStatus.Running, "audit_degraded");
            }
            catch
            {
            }
        }

        private static void RecordActivity(Func<Task> record)
        {
            try
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await record().ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                });
            }
            catch
            {
            }
        }
    }
}
