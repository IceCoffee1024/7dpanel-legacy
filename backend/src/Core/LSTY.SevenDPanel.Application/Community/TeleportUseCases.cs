using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application.Economy;
using LSTY.SevenDPanel.Domain.Community;

namespace LSTY.SevenDPanel.Application.Community
{
    public sealed class HomeUseCases
    {
        private readonly ICommunityStore store;
        private readonly Func<DateTimeOffset> utcNow;
        private readonly IEconomyLedgerStore? ledger;

        public HomeUseCases(
            ICommunityStore store,
            Func<DateTimeOffset> utcNow,
            IEconomyLedgerStore? ledger = null)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            this.ledger = ledger;
        }

        public PlayerHome Save(
            string homeId,
            string name,
            TeleportPlayerSnapshot player)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            TeleportUseCases.ValidateSafePlayer(player);
            if (!player.WorldBounds.Contains(player.Position))
                throw new TeleportRejectedException(TeleportFailureCodes.DestinationOutOfBounds);

            var settings = store.GetTeleportSettings(TeleportKind.Home);
            if (!settings.Enabled)
                throw new TeleportRejectedException(TeleportFailureCodes.TeleportDisabled);
            var now = RequireUtc(utcNow());
            return store.SaveHome(
                new PlayerHome(
                    homeId,
                    player.CrossplatformId,
                    name,
                    player.Position,
                    now,
                    now,
                    0),
                settings.MaxHomes ?? int.MaxValue);
        }

        public IReadOnlyList<PlayerHome> List(string crossplatformId) =>
            store.ListHomes(crossplatformId);

        public TeleportSettings Settings() => store.GetTeleportSettings(TeleportKind.Home);

        public PlayerHome SaveFromPlayerCommand(
            string homeId,
            string name,
            TeleportPlayerSnapshot player,
            string operationId)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (string.IsNullOrWhiteSpace(operationId)) throw new ArgumentException("An operation id is required.", nameof(operationId));
            var experience = store.GetTeleportSettings(TeleportKind.Home).HomeExperience!;
            if (experience.SetFeeAmount == 0) return Save(homeId, name, player);
            if (ledger == null) throw new InvalidOperationException("The economy ledger is unavailable.");
            var now = RequireUtc(utcNow());
            var reservationId = "home-save-reservation:" + operationId;
            FundsReservationResult reservation;
            try
            {
                reservation = ledger.TryReserve(new FundsReservationDraft(
                    reservationId,
                    "player:" + player.CrossplatformId,
                    experience.SetFeeAmount,
                    operationId + ":reserve",
                    "HomeSave",
                    operationId,
                    now,
                    null));
            }
            catch (EconomyAccountNotFoundException)
            {
                throw new TeleportRejectedException(TeleportFailureCodes.AccountUnavailable);
            }
            if (reservation.Status == FundsReservationStatus.InsufficientFunds)
                throw new TeleportRejectedException(TeleportFailureCodes.InsufficientFunds);
            if (reservation.Status != FundsReservationStatus.Reserved &&
                reservation.Status != FundsReservationStatus.Captured)
                throw new TeleportRejectedException(TeleportFailureCodes.AccountUnavailable);

            try
            {
                var home = Save(homeId, name, player);
                if (reservation.Status != FundsReservationStatus.Captured)
                    ledger.Capture(reservationId, "home-save-capture:" + operationId, operationId + ":capture", RequireUtc(utcNow()));
                return home;
            }
            catch
            {
                if (reservation.Status != FundsReservationStatus.Captured)
                    ledger.Release(reservationId, RequireUtc(utcNow()));
                throw;
            }
        }

        public bool Delete(string crossplatformId, string name) =>
            store.DeleteHome(crossplatformId, name);

        private static DateTimeOffset RequireUtc(DateTimeOffset value)
        {
            if (value.Offset != TimeSpan.Zero)
                throw new InvalidOperationException("The community clock must return UTC.");
            return value;
        }
    }

    public sealed class CityUseCases
    {
        private readonly ICommunityStore store;

        public CityUseCases(ICommunityStore store) =>
            this.store = store ?? throw new ArgumentNullException(nameof(store));

        public City Save(City city) => store.SaveCity(city);

        public IReadOnlyList<City> ListEnabled() => store.ListEnabledCities();
    }

    public sealed class FriendUseCases
    {
        private readonly ICommunityStore store;

        public FriendUseCases(ICommunityStore store) =>
            this.store = store ?? throw new ArgumentNullException(nameof(store));

        public FriendRequest Invite(FriendRequest request) => store.CreateFriendRequest(request);

        public FriendRequest Respond(
            string requestId,
            string responderCrossplatformId,
            bool accept,
            string? friendshipId,
            DateTimeOffset respondedAtUtc) => store.RespondToFriendRequest(
                requestId,
                responderCrossplatformId,
                accept,
                friendshipId,
                respondedAtUtc);

        public bool Remove(string firstCrossplatformId, string secondCrossplatformId) =>
            store.RemoveFriendship(firstCrossplatformId, secondCrossplatformId);
    }

    public sealed class TeleportUseCases
    {
        private readonly ICommunityStore store;
        private readonly IEconomyLedgerStore ledger;
        private readonly IChargedTeleportOperationStore? chargedTeleportOperations;
        private readonly ICommunityGameGateway gateway;
        private readonly Func<DateTimeOffset> utcNow;

        public TeleportUseCases(
            ICommunityStore store,
            IEconomyLedgerStore ledger,
            ICommunityGameGateway gateway,
            Func<DateTimeOffset> utcNow,
            IChargedTeleportOperationStore? chargedTeleportOperations = null)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            this.gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            this.chargedTeleportOperations = chargedTeleportOperations;
        }

        public Task<TeleportOperation> TeleportHomeAsync(
            TeleportExecutionRequest request,
            string homeName,
            CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var home = store.FindHome(request.Player.CrossplatformId, homeName) ??
                throw new TeleportRejectedException(TeleportFailureCodes.DestinationNotFound);
            return ExecuteAsync(request, TeleportKind.Home, home.Position, null, cancellationToken);
        }

        public Task<TeleportOperation> TeleportCityAsync(
            TeleportExecutionRequest request,
            string cityName,
            CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var city = store.FindEnabledCity(cityName) ??
                throw new TeleportRejectedException(TeleportFailureCodes.DestinationNotFound);
            return ExecuteAsync(request, TeleportKind.City, city.Position, null, cancellationToken);
        }

        public Task<TeleportOperation> TeleportFriendAsync(
            TeleportExecutionRequest request,
            TeleportPlayerSnapshot target,
            CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (!store.AreFriends(request.Player.CrossplatformId, target.CrossplatformId))
                throw new TeleportRejectedException(TeleportFailureCodes.TargetNotAllowed);
            if (!target.IsOnline)
                throw new TeleportRejectedException(TeleportFailureCodes.PlayerNotOnline);
            if (!target.IsAlive)
                throw new TeleportRejectedException(TeleportFailureCodes.PlayerDead);
            if (!target.IsSpawned)
                throw new TeleportRejectedException(TeleportFailureCodes.PlayerNotSpawned);
            if (!target.AllowsFriendTeleport)
                throw new TeleportRejectedException(TeleportFailureCodes.TargetNotAllowed);
            if (!string.Equals(
                    request.Player.Position.WorldId,
                    target.Position.WorldId,
                    StringComparison.Ordinal))
            {
                throw new TeleportRejectedException(
                    TeleportFailureCodes.DestinationWorldMismatch);
            }

            return ExecuteAsync(
                request,
                TeleportKind.Friend,
                target.Position,
                target.CrossplatformId,
                cancellationToken);
        }

        public Task<TeleportOperation> TeleportBackAsync(
            TeleportExecutionRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var returnPoint = store.GetReturnPoint(request.Player.CrossplatformId) ??
                throw new TeleportRejectedException(TeleportFailureCodes.DestinationNotFound);
            return ExecuteAsync(
                request,
                TeleportKind.Return,
                returnPoint.Position,
                null,
                cancellationToken);
        }

        public Task<TeleportOperation> TeleportAdminAsync(
            TeleportExecutionRequest request,
            WorldPosition destination,
            CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return ExecuteAsync(
                request,
                TeleportKind.Admin,
                destination ?? throw new ArgumentNullException(nameof(destination)),
                null,
                cancellationToken);
        }

        internal static void ValidateSafePlayer(TeleportPlayerSnapshot player)
        {
            if (!player.IsOnline)
                throw new TeleportRejectedException(TeleportFailureCodes.PlayerNotOnline);
            if (!player.IsAlive)
                throw new TeleportRejectedException(TeleportFailureCodes.PlayerDead);
            if (!player.IsSpawned)
                throw new TeleportRejectedException(TeleportFailureCodes.PlayerNotSpawned);
        }

        private async Task<TeleportOperation> ExecuteAsync(
            TeleportExecutionRequest request,
            TeleportKind kind,
            WorldPosition destination,
            string? targetCrossplatformId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = GetUtcNow();
            var settings = store.GetTeleportSettings(kind);
            ValidatePreflight(request.Player, destination, settings);

            var reservationId = settings.FeeAmount == 0
                ? null
                : "teleport-reservation:" + request.OperationId;
            var draft = new TeleportOperationDraft(
                request.OperationId,
                kind,
                request.Player.CrossplatformId,
                targetCrossplatformId,
                request.Player.EntityId,
                request.Player.Position.WorldId,
                destination,
                request.IdempotencyKey,
                reservationId,
                request.ActorKind,
                request.ActorId,
                request.CorrelationId,
                now);

            TeleportOperation operation;
            if (reservationId != null && chargedTeleportOperations != null)
            {
                var result = ReserveFeeAndCreateOperation(
                    request,
                    settings.FeeAmount,
                    reservationId,
                    draft,
                    now);
                var reservation = result.Reservation;
                if (reservation.Status != FundsReservationStatus.Reserved &&
                    reservation.Status != FundsReservationStatus.Captured)
                {
                    throw new TeleportRejectedException(MapReservationFailure(reservation.Status));
                }

                operation = result.Operation ?? throw new InvalidOperationException(
                    "A successful teleport reservation must include an operation intent.");
            }
            else
            {
                if (reservationId != null)
                {
                    var reservation = ReserveFee(request, settings.FeeAmount, reservationId, now);
                    if (reservation.Status != FundsReservationStatus.Reserved &&
                        reservation.Status != FundsReservationStatus.Captured)
                    {
                        throw new TeleportRejectedException(MapReservationFailure(reservation.Status));
                    }
                }

                try
                {
                    operation = store.CreateTeleportOperation(draft);
                }
                catch
                {
                    if (reservationId != null) ledger.Release(reservationId, now);
                    throw;
                }
            }

            if (operation.State != TeleportOperationState.Reserved) return operation;
            if (!store.TryTransitionTeleportOperation(
                    operation.OperationId,
                    TeleportOperationState.Reserved,
                    TeleportOperationState.Dispatching,
                    null,
                    now))
            {
                return RequireOperation(operation.OperationId);
            }

            TeleportActionResult delivery;
            try
            {
                delivery = await gateway.TeleportAsync(
                        new TeleportActionCommand(
                            operation.OperationId,
                            request.Player.CrossplatformId,
                            request.Player.EntityId,
                            request.Player.Position.WorldId,
                            destination,
                            settings.DenyDuringBloodMoon),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                MoveToPending(operation.OperationId, GetUtcNow());
                return RequireOperation(operation.OperationId);
            }

            var completedAtUtc = GetUtcNow();
            if (delivery.Status == TeleportActionStatus.ResultUnknown)
            {
                MoveToPending(operation.OperationId, completedAtUtc);
                return RequireOperation(operation.OperationId);
            }
            if (delivery.Status != TeleportActionStatus.Succeeded)
            {
                return FailBeforeSideEffect(
                    operation,
                    delivery.FailureCode ?? TeleportFailureCodes.GatewayFailure,
                    completedAtUtc);
            }

            try
            {
                if (reservationId != null)
                {
                    ledger.Capture(
                        reservationId,
                        "teleport-capture:" + operation.OperationId,
                        request.IdempotencyKey + ":capture",
                        completedAtUtc);
                }

                return store.CompleteTeleportOperation(
                    operation.OperationId,
                    delivery.Origin ?? throw new InvalidOperationException(
                        "A confirmed teleport must include its game-thread origin."),
                    completedAtUtc.Add(settings.Cooldown),
                    completedAtUtc.Add(settings.GlobalCooldown),
                    completedAtUtc);
            }
            catch
            {
                MoveToPending(operation.OperationId, completedAtUtc);
                return RequireOperation(operation.OperationId);
            }
        }

        private FundsReservationResult ReserveFee(
            TeleportExecutionRequest request,
            long feeAmount,
            string reservationId,
            DateTimeOffset now)
        {
            try
            {
                return ledger.TryReserve(new FundsReservationDraft(
                    reservationId,
                    "player:" + request.Player.CrossplatformId,
                    feeAmount,
                    request.IdempotencyKey + ":reserve",
                    "Teleport",
                    request.OperationId,
                    now,
                    null));
            }
            catch (EconomyAccountNotFoundException)
            {
                throw new TeleportRejectedException(TeleportFailureCodes.AccountUnavailable);
            }
        }

        private ChargedTeleportOperationResult ReserveFeeAndCreateOperation(
            TeleportExecutionRequest request,
            long feeAmount,
            string reservationId,
            TeleportOperationDraft operation,
            DateTimeOffset now)
        {
            try
            {
                return chargedTeleportOperations!.ReserveAndCreate(
                    new FundsReservationDraft(
                        reservationId,
                        "player:" + request.Player.CrossplatformId,
                        feeAmount,
                        request.IdempotencyKey + ":reserve",
                        "Teleport",
                        request.OperationId,
                        now,
                        null),
                    operation);
            }
            catch (EconomyAccountNotFoundException)
            {
                throw new TeleportRejectedException(TeleportFailureCodes.AccountUnavailable);
            }
        }

        private TeleportOperation FailBeforeSideEffect(
            TeleportOperation operation,
            string errorCode,
            DateTimeOffset now)
        {
            var released = operation.ReservationId == null || ledger.Release(operation.ReservationId, now);
            if (!released)
            {
                MoveToPending(operation.OperationId, now);
                return RequireOperation(operation.OperationId);
            }

            store.TryTransitionTeleportOperation(
                operation.OperationId,
                TeleportOperationState.Dispatching,
                TeleportOperationState.Failed,
                errorCode,
                now);
            return RequireOperation(operation.OperationId);
        }

        private void MoveToPending(string operationId, DateTimeOffset now)
        {
            store.TryTransitionTeleportOperation(
                operationId,
                TeleportOperationState.Dispatching,
                TeleportOperationState.PendingReconciliation,
                TeleportFailureCodes.ResultUnknown,
                now);
        }

        private static void ValidatePreflight(
            TeleportPlayerSnapshot player,
            WorldPosition destination,
            TeleportSettings settings)
        {
            ValidateSafePlayer(player);
            if (!settings.Enabled)
                throw new TeleportRejectedException(TeleportFailureCodes.TeleportDisabled);
            if (settings.DenyDuringBloodMoon && player.IsBloodMoon)
                throw new TeleportRejectedException(TeleportFailureCodes.BloodMoonDenied);
            if (!string.Equals(
                    player.Position.WorldId,
                    destination.WorldId,
                    StringComparison.Ordinal))
            {
                throw new TeleportRejectedException(
                    TeleportFailureCodes.DestinationWorldMismatch);
            }
            if (!player.WorldBounds.Contains(destination))
                throw new TeleportRejectedException(TeleportFailureCodes.DestinationOutOfBounds);
        }

        private static string MapReservationFailure(FundsReservationStatus status) => status switch
        {
            FundsReservationStatus.InsufficientFunds => TeleportFailureCodes.InsufficientFunds,
            FundsReservationStatus.AccountFrozen => TeleportFailureCodes.AccountUnavailable,
            FundsReservationStatus.AccountDisabled => TeleportFailureCodes.AccountUnavailable,
            _ => TeleportFailureCodes.StateConflict
        };

        private TeleportOperation RequireOperation(string operationId) =>
            store.FindTeleportOperation(operationId) ??
            throw new InvalidOperationException("The teleport operation disappeared.");

        private DateTimeOffset GetUtcNow()
        {
            var value = utcNow();
            if (value.Offset != TimeSpan.Zero)
                throw new InvalidOperationException("The community clock must return UTC.");
            return value;
        }
    }
}
