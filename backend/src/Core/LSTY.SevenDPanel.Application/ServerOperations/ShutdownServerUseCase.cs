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
                () => DateTimeOffset.UtcNow)
        {
        }

        internal ShutdownServerUseCase(
            IShutdownServerGateway gateway,
            IServerOperationAuditTrail auditTrail,
            IRecentActivityWriter recentActivity,
            Func<string> operationIdFactory,
            Func<DateTimeOffset> utcClock)
        {
            this.gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            this.auditTrail = auditTrail ?? throw new ArgumentNullException(nameof(auditTrail));
            this.recentActivity = recentActivity ?? throw new ArgumentNullException(nameof(recentActivity));
            this.operationIdFactory = operationIdFactory ?? throw new ArgumentNullException(nameof(operationIdFactory));
            this.utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
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
                CreatePending(operationId, actorSubject, requestedAtUtc);

                try
                {
                    await gateway.RequestShutdownAsync(cancellationToken).ConfigureAwait(false);
                    Volatile.Write(ref inFlight, 0);
                }
                catch (OperationCanceledException)
                {
                    throw Fail(operationId, actorSubject, ServerOperationCodeContract.ShutdownCancelled);
                }
                catch (TimeoutException)
                {
                    throw Fail(operationId, actorSubject, ServerOperationCodeContract.ShutdownTimeout);
                }
                catch (ServerOperationFailedException exception)
                {
                    RecordFailure(operationId, actorSubject, exception.FailureCode);
                    throw;
                }
                catch
                {
                    throw Fail(operationId, actorSubject, ServerOperationCodeContract.ShutdownFailed);
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
            string failureCode)
        {
            RecordFailure(operationId, actorSubject, failureCode);
            return new ServerOperationFailedException(failureCode);
        }

        private void RecordFailure(
            string operationId,
            string actorSubject,
            string failureCode)
        {
            var failedAtUtc = utcClock();
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

        private bool TryMarkStarted(string operationId, DateTimeOffset acceptedAtUtc)
        {
            try
            {
                return auditTrail.TryMarkStarted(operationId, acceptedAtUtc);
            }
            catch
            {
                return false;
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
