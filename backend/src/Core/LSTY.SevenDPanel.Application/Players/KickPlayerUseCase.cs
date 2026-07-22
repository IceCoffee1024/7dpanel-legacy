using System;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application
{
    public sealed class KickPlayerUseCase
    {
        private readonly IPlayerActions playerActions;
        private readonly IPlayerActionAuditTrail auditTrail;
        private readonly Func<string> operationIdFactory;
        private readonly Func<DateTimeOffset> utcClock;
        private int inFlight;

        public KickPlayerUseCase(
            IPlayerActions playerActions,
            IPlayerActionAuditTrail auditTrail)
            : this(
                playerActions,
                auditTrail,
                () => Guid.NewGuid().ToString("N"),
                () => DateTimeOffset.UtcNow)
        {
        }

        internal KickPlayerUseCase(
            IPlayerActions playerActions,
            IPlayerActionAuditTrail auditTrail,
            Func<string> operationIdFactory,
            Func<DateTimeOffset> utcClock)
        {
            this.playerActions = playerActions ?? throw new ArgumentNullException(nameof(playerActions));
            this.auditTrail = auditTrail ?? throw new ArgumentNullException(nameof(auditTrail));
            this.operationIdFactory = operationIdFactory ?? throw new ArgumentNullException(nameof(operationIdFactory));
            this.utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
        }

        public async Task<KickPlayerResult> ExecuteAsync(
            KickPlayerRequest request,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.ActorSubject))
                throw new ArgumentException("An actor subject is required.", nameof(request));

            if (request.EntityId < 0)
                throw new ArgumentOutOfRangeException(nameof(request));

            if (request.ExpectedPlatformIdentity == null)
                throw new InvalidPlayerIdentityException();

            if (!request.Confirmed)
                throw new PlayerKickConfirmationRequiredException();

            if (string.IsNullOrWhiteSpace(request.Reason))
                throw new InvalidPlayerKickReasonException();

            var reason = request.Reason.Trim();
            if (reason.Length > 200)
                throw new InvalidPlayerKickReasonException();

            if (Interlocked.CompareExchange(ref inFlight, 1, 0) != 0)
                throw new PlayerActionBusyException();

            try
            {
                var operationId = operationIdFactory();
                var requestedAtUtc = utcClock();
                try
                {
                    auditTrail.CreatePending(new PlayerActionAuditIntent(
                        operationId,
                        request.ActorSubject,
                        request.EntityId,
                        request.ExpectedPlatformIdentity,
                        reason,
                        requestedAtUtc));
                }
                catch (Exception exception)
                {
                    throw new AuditUnavailableException(exception);
                }

                KickPlayerActionResult actionResult;
                try
                {
                    actionResult = await playerActions.KickAsync(
                        new KickPlayerCommand(
                            request.EntityId,
                            request.ExpectedPlatformIdentity,
                            reason),
                        cancellationToken);
                }
                catch (TimeoutException)
                {
                    CompleteFailed(operationId, null, "game_thread_timeout");
                    throw;
                }
                catch (OperationCanceledException)
                {
                    CompleteFailed(operationId, null, "player_kick_cancelled");
                    throw;
                }
                catch (Exception exception)
                {
                    CompleteFailed(operationId, null, "player_kick_failed");
                    throw new PlayerKickFailedException(exception);
                }

                switch (actionResult.Status)
                {
                    case KickPlayerActionStatus.Succeeded:
                        return CompleteSucceeded(operationId, requestedAtUtc, actionResult);
                    case KickPlayerActionStatus.PlayerNotOnline:
                        CompleteFailed(operationId, null, "player_not_online");
                        throw new PlayerNotOnlineException();
                    case KickPlayerActionStatus.PlayerIdentityChanged:
                        CompleteFailed(operationId, actionResult.Target?.Name, "player_identity_changed");
                        throw new PlayerIdentityChangedException();
                    default:
                        CompleteFailed(operationId, actionResult.Target?.Name, "player_kick_failed");
                        throw new PlayerKickFailedException(
                            new InvalidOperationException("The player action returned an unsupported status."));
                }
            }
            finally
            {
                Volatile.Write(ref inFlight, 0);
            }
        }

        private KickPlayerResult CompleteSucceeded(
            string operationId,
            DateTimeOffset requestedAtUtc,
            KickPlayerActionResult actionResult)
        {
            var target = actionResult.Target
                ?? throw new PlayerKickFailedException(
                    new InvalidOperationException("The successful kick result did not contain a target."));
            var completedAtUtc = utcClock();
            Complete(PlayerActionAuditCompletion.Succeeded(
                operationId,
                completedAtUtc,
                target.Name));

            return new KickPlayerResult(
                operationId,
                target,
                requestedAtUtc,
                completedAtUtc);
        }

        private void CompleteFailed(
            string operationId,
            string? targetName,
            string failureCode)
        {
            Complete(PlayerActionAuditCompletion.Failed(
                operationId,
                utcClock(),
                targetName,
                failureCode));
        }

        private void Complete(PlayerActionAuditCompletion completion)
        {
            try
            {
                if (!auditTrail.TryComplete(completion))
                    throw new AuditCompletionUnavailableException();
            }
            catch (AuditCompletionUnavailableException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new AuditCompletionUnavailableException(exception);
            }
        }
    }
}