using System;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application
{
    public sealed class ShutdownServerUseCase
    {
        private readonly IShutdownServerGateway gateway;
        private readonly IServerOperationAuditTrail auditTrail;
        private readonly IRecentActivityWriter recentActivity;
        private readonly Func<string> operationIdFactory;
        private readonly Func<DateTimeOffset> utcClock;
        private readonly IServerOperationStore operationStore;
        private readonly ServerOperationProcessInstance processInstance;
        private readonly TimeSpan completionWindow;
        private int inFlight;

        public ShutdownServerUseCase(
            IShutdownServerGateway gateway,
            IServerOperationAuditTrail auditTrail,
            IRecentActivityWriter recentActivity)
            : this(
                gateway,
                auditTrail,
                recentActivity,
                () => Guid.NewGuid().ToString("N"),
                () => DateTimeOffset.UtcNow,
                new InMemoryServerOperationStore(), new ServerOperationProcessInstance(), TimeSpan.FromMinutes(5))
        {
        }

        internal ShutdownServerUseCase(
            IShutdownServerGateway gateway,
            IServerOperationAuditTrail auditTrail,
            IRecentActivityWriter recentActivity,
            Func<string> operationIdFactory,
            Func<DateTimeOffset> utcClock)
            : this(gateway, auditTrail, recentActivity, operationIdFactory, utcClock,
                new InMemoryServerOperationStore(), new ServerOperationProcessInstance(), TimeSpan.FromMinutes(5))
        {
        }

        public ShutdownServerUseCase(
            IShutdownServerGateway gateway,
            IServerOperationAuditTrail auditTrail,
            IRecentActivityWriter recentActivity,
            IServerOperationStore operationStore,
            ServerOperationProcessInstance processInstance)
            : this(gateway, auditTrail, recentActivity, () => Guid.NewGuid().ToString("N"),
                () => DateTimeOffset.UtcNow, operationStore, processInstance, TimeSpan.FromMinutes(5))
        {
        }

        internal ShutdownServerUseCase(
            IShutdownServerGateway gateway,
            IServerOperationAuditTrail auditTrail,
            IRecentActivityWriter recentActivity,
            Func<string> operationIdFactory,
            Func<DateTimeOffset> utcClock,
            IServerOperationStore operationStore,
            ServerOperationProcessInstance processInstance,
            TimeSpan completionWindow)
        {
            this.gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            this.auditTrail = auditTrail ?? throw new ArgumentNullException(nameof(auditTrail));
            this.recentActivity = recentActivity ?? throw new ArgumentNullException(nameof(recentActivity));
            this.operationIdFactory = operationIdFactory ?? throw new ArgumentNullException(nameof(operationIdFactory));
            this.utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
            this.operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
            this.processInstance = processInstance ?? throw new ArgumentNullException(nameof(processInstance));
            if (completionWindow <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(completionWindow));
            this.completionWindow = completionWindow;
        }

        public async Task<ServerOperationResult> ExecuteAsync(
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

                try
                {
                    await gateway.RequestShutdownAsync(cancellationToken).ConfigureAwait(false);
                    Volatile.Write(ref inFlight, 0);
                }
                catch (OperationCanceledException)
                {
                    throw Fail(operationId, actorSubject, ServerOperationCodeContract.ShutdownCancelled, ServerOperationLifecycleStatus.Cancelled);
                }
                catch (TimeoutException)
                {
                    throw Fail(operationId, actorSubject, ServerOperationCodeContract.ShutdownTimeout, ServerOperationLifecycleStatus.Failed);
                }
                catch (ServerOperationFailedException exception)
                {
                    RecordFailure(operationId, actorSubject, exception.FailureCode, ServerOperationLifecycleStatus.Failed);
                    throw;
                }
                catch
                {
                    throw Fail(operationId, actorSubject, ServerOperationCodeContract.ShutdownFailed, ServerOperationLifecycleStatus.Failed);
                }

                var acceptedAtUtc = utcClock();
                var auditStatus = TryMarkStarted(operationId, acceptedAtUtc)
                    ? "recorded"
                    : "audit_degraded";
                RecordActivity(() => recentActivity.RecordShutdownRequestedAsync(
                    actorSubject,
                    acceptedAtUtc,
                    CancellationToken.None));

                return new ServerOperationResult(
                    operationId,
                    ServerOperationCodeContract.Shutdown,
                    "shutdown_requested",
                    requestedAtUtc,
                    acceptedAtUtc,
                    auditStatus);
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
                    ServerOperationCodeContract.Shutdown,
                    actorSubject,
                    requestedAtUtc));
            }
            catch (Exception exception)
            {
                throw new ServerOperationAuditUnavailableException(exception);
            }
        }

        private ServerOperationFailedException Fail(
            string operationId,
            string actorSubject,
            string failureCode,
            ServerOperationLifecycleStatus status)
        {
            RecordFailure(operationId, actorSubject, failureCode, status);
            return new ServerOperationFailedException(failureCode);
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
                ServerOperationCodeContract.Shutdown,
                failureCode,
                failedAtUtc,
                CancellationToken.None));
        }

        private void CreateQueued(string operationId, string actorSubject, DateTimeOffset requestedAtUtc)
        {
            operationStore.CreateQueued(new ServerOperationSnapshot(
                operationId, ServerOperationCodeContract.Shutdown, ServerOperationLifecycleStatus.Queued,
                actorSubject, processInstance.Value, requestedAtUtc, null, null,
                requestedAtUtc.Add(completionWindow), null, "recorded"));
        }

        private bool TryMarkStarted(string operationId, DateTimeOffset acceptedAtUtc)
        {
            try
            {
                var recorded = auditTrail.TryMarkStarted(operationId, acceptedAtUtc);
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
