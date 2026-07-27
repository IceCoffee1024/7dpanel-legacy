using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application.Rewards;
using LSTY.SevenDPanel.Domain.Rewards;

namespace LSTY.SevenDPanel.Application.Commerce
{
    public sealed class SaveShopProductUseCase
    {
        private readonly ICommerceStore store;
        private readonly Func<DateTimeOffset> utcClock;

        public SaveShopProductUseCase(ICommerceStore store)
            : this(store, () => DateTimeOffset.UtcNow) { }

        internal SaveShopProductUseCase(ICommerceStore store, Func<DateTimeOffset> utcClock)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
        }

        public ShopProductSnapshot Execute(ShopProductDraft product) =>
            store.SaveProduct(product ?? throw new ArgumentNullException(nameof(product)), UtcNow());

        private DateTimeOffset UtcNow() => CommerceGrantSupport.UtcNow(utcClock);
    }

    public sealed class PurchaseProductUseCase
    {
        private readonly ICommerceStore store;
        private readonly GrantRewardUseCase grant;
        private readonly Func<string> purchaseIdFactory;
        private readonly Func<string> reservationIdFactory;
        private readonly Func<string> captureTransactionIdFactory;
        private readonly Func<DateTimeOffset> utcClock;

        public PurchaseProductUseCase(ICommerceStore store, GrantRewardUseCase grant)
            : this(
                store,
                grant,
                () => "purchase-" + Guid.NewGuid().ToString("N"),
                () => "purchase-reservation-" + Guid.NewGuid().ToString("N"),
                () => "purchase-capture-" + Guid.NewGuid().ToString("N"),
                () => DateTimeOffset.UtcNow)
        {
        }

        internal PurchaseProductUseCase(
            ICommerceStore store,
            GrantRewardUseCase grant,
            Func<string> purchaseIdFactory,
            Func<string> reservationIdFactory,
            Func<string> captureTransactionIdFactory,
            Func<DateTimeOffset> utcClock)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.grant = grant ?? throw new ArgumentNullException(nameof(grant));
            this.purchaseIdFactory = purchaseIdFactory ?? throw new ArgumentNullException(nameof(purchaseIdFactory));
            this.reservationIdFactory = reservationIdFactory ?? throw new ArgumentNullException(nameof(reservationIdFactory));
            this.captureTransactionIdFactory = captureTransactionIdFactory ??
                throw new ArgumentNullException(nameof(captureTransactionIdFactory));
            this.utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
        }

        public async Task<PurchaseProductResult> ExecuteAsync(
            PurchaseProductCommand command,
            CancellationToken cancellationToken)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            var now = UtcNow();
            var reserved = store.ReservePurchase(new PurchaseReservationRequest(
                CommerceValidation.RequireText(purchaseIdFactory(), nameof(purchaseIdFactory)),
                CommerceValidation.RequireText(reservationIdFactory(), nameof(reservationIdFactory)),
                command.ProductId,
                command.CrossplatformId,
                command.Quantity,
                command.IdempotencyKey,
                command.CorrelationId,
                now,
                now.AddMinutes(15)));
            if (reserved.Status != PurchaseReservationStatus.Reserved)
                return new PurchaseProductResult(Map(reserved.Status), null);
            if (reserved.Purchase == null)
                throw new InvalidOperationException("commerce_purchase_reservation_missing");
            if (!reserved.Created)
                return Result(reserved.Purchase);

            var dispatching = store.TryStartPurchaseDispatch(reserved.Purchase.PurchaseId, UtcNow());
            if (dispatching == null) return Result(store.GetPurchase(reserved.Purchase.PurchaseId));

            try
            {
                var grantResult = await grant.ExecuteAsync(new GrantRewardCommand(
                        dispatching.RewardPackageId,
                        dispatching.CrossplatformId,
                        command.ExpectedEntityId,
                        command.ExpectedWorldId,
                        "purchase-grant:" + dispatching.PurchaseId,
                        "purchase:" + dispatching.PurchaseId,
                        "Purchase",
                        dispatching.ProductId,
                        "System",
                        "commerce:purchase",
                        command.CorrelationId,
                        dispatching.ReservationId),
                    cancellationToken).ConfigureAwait(false);
                var classification = CommerceGrantSupport.Classify(grantResult.Operation);
                var resolved = store.ResolvePurchaseGrant(new PurchaseGrantResolution(
                    dispatching.PurchaseId,
                    classification,
                    grantResult.Operation.OperationId,
                    classification == CommerceGrantResolutionKind.Completed
                        ? CommerceValidation.RequireText(
                            captureTransactionIdFactory(), nameof(captureTransactionIdFactory))
                        : null,
                    grantResult.Operation.ErrorCode,
                    UtcNow()));
                return Result(resolved);
            }
            catch (Exception exception) when (CommerceGrantSupport.IsKnownPreDispatchFailure(exception))
            {
                var failed = store.ResolvePurchaseGrant(new PurchaseGrantResolution(
                    dispatching.PurchaseId,
                    CommerceGrantResolutionKind.FailedBeforeSideEffects,
                    null,
                    null,
                    exception.Message,
                    UtcNow()));
                return Result(failed);
            }
            catch
            {
                var pending = store.ResolvePurchaseGrant(new PurchaseGrantResolution(
                    dispatching.PurchaseId,
                    CommerceGrantResolutionKind.PendingReconciliation,
                    null,
                    null,
                    "reward_delivery_result_unknown",
                    UtcNow()));
                return Result(pending);
            }
        }

        private DateTimeOffset UtcNow() => CommerceGrantSupport.UtcNow(utcClock);

        private static PurchaseProductResult Result(ShopPurchaseSnapshot purchase) =>
            new PurchaseProductResult(purchase.State switch
            {
                PurchaseState.Completed => PurchaseRequestStatus.Completed,
                PurchaseState.PendingReconciliation => PurchaseRequestStatus.PendingReconciliation,
                PurchaseState.Failed => PurchaseRequestStatus.Failed,
                _ => PurchaseRequestStatus.Reserved
            }, purchase);

        private static PurchaseRequestStatus Map(PurchaseReservationStatus status) => status switch
        {
            PurchaseReservationStatus.ProductDisabled => PurchaseRequestStatus.ProductDisabled,
            PurchaseReservationStatus.AccountDisabled => PurchaseRequestStatus.AccountDisabled,
            PurchaseReservationStatus.AccountFrozen => PurchaseRequestStatus.AccountFrozen,
            PurchaseReservationStatus.InsufficientFunds => PurchaseRequestStatus.InsufficientFunds,
            PurchaseReservationStatus.OutOfStock => PurchaseRequestStatus.OutOfStock,
            PurchaseReservationStatus.PlayerLimitReached => PurchaseRequestStatus.PlayerLimitReached,
            _ => PurchaseRequestStatus.Reserved
        };
    }

    public sealed class CreateRedeemCodeUseCase
    {
        private readonly ICommerceStore store;
        private readonly Func<string> codeFactory;
        private readonly Func<string> codeIdFactory;
        private readonly Func<DateTimeOffset> utcClock;

        public CreateRedeemCodeUseCase(ICommerceStore store)
            : this(
                store,
                RedeemCodeCodec.Generate,
                () => "redeem-code-" + Guid.NewGuid().ToString("N"),
                () => DateTimeOffset.UtcNow)
        {
        }

        internal CreateRedeemCodeUseCase(
            ICommerceStore store,
            Func<string> codeFactory,
            Func<string> codeIdFactory,
            Func<DateTimeOffset> utcClock)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.codeFactory = codeFactory ?? throw new ArgumentNullException(nameof(codeFactory));
            this.codeIdFactory = codeIdFactory ?? throw new ArgumentNullException(nameof(codeIdFactory));
            this.utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
        }

        public GeneratedRedeemCode Execute(CreateRedeemCodeCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            var plaintext = CommerceValidation.RequireText(codeFactory(), nameof(codeFactory));
            var normalized = RedeemCodeCodec.Normalize(plaintext);
            var canonical = RedeemCodeCodec.Format(normalized);
            if (!string.Equals(plaintext, canonical, StringComparison.Ordinal))
                throw new InvalidOperationException("redeem_code_factory_must_return_canonical_code");
            var definition = store.SaveRedeemCode(new RedeemCodeSecretDraft(
                CommerceValidation.RequireText(codeIdFactory(), nameof(codeIdFactory)),
                RedeemCodeCodec.Digest(normalized),
                normalized.Substring(12, 4),
                RedeemCodeCodec.NormalizationVersion,
                command.RewardPackageId,
                command.Enabled,
                command.ValidFromUtc,
                command.ExpiresAtUtc,
                command.MaxRedemptions,
                command.PerPlayerLimit),
                CommerceGrantSupport.UtcNow(utcClock));
            return new GeneratedRedeemCode(canonical, definition);
        }
    }

    public sealed class RedeemCodeUseCase
    {
        private readonly ICommerceStore store;
        private readonly GrantRewardUseCase grant;
        private readonly Func<string> attemptIdFactory;
        private readonly Func<DateTimeOffset> utcClock;

        public RedeemCodeUseCase(ICommerceStore store, GrantRewardUseCase grant)
            : this(
                store,
                grant,
                () => "redeem-attempt-" + Guid.NewGuid().ToString("N"),
                () => DateTimeOffset.UtcNow)
        {
        }

        internal RedeemCodeUseCase(
            ICommerceStore store,
            GrantRewardUseCase grant,
            Func<string> attemptIdFactory,
            Func<DateTimeOffset> utcClock)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.grant = grant ?? throw new ArgumentNullException(nameof(grant));
            this.attemptIdFactory = attemptIdFactory ?? throw new ArgumentNullException(nameof(attemptIdFactory));
            this.utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
        }

        public async Task<RedeemCodeResult> ExecuteAsync(
            RedeemCodeCommand command,
            CancellationToken cancellationToken)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            string normalized;
            try { normalized = RedeemCodeCodec.Normalize(command.Code); }
            catch (ArgumentException) { return new RedeemCodeResult(RedeemRequestStatus.InvalidCode, null); }

            var reservation = store.ReserveRedemption(new RedeemReservationRequest(
                CommerceValidation.RequireText(attemptIdFactory(), nameof(attemptIdFactory)),
                RedeemCodeCodec.Digest(normalized),
                RedeemCodeCodec.NormalizationVersion,
                command.CrossplatformId,
                command.CorrelationId,
                UtcNow()));
            if (reservation.Status != RedeemReservationStatus.Reserved)
                return new RedeemCodeResult(Map(reservation.Status), reservation.Attempt);
            if (reservation.Attempt == null)
                throw new InvalidOperationException("redeem_attempt_reservation_missing");
            if (!reservation.Created) return Result(reservation.Attempt);

            try
            {
                var grantResult = await grant.ExecuteAsync(new GrantRewardCommand(
                        reservation.Attempt.RewardPackageId,
                        reservation.Attempt.CrossplatformId,
                        command.ExpectedEntityId,
                        command.ExpectedWorldId,
                        "redeem-grant:" + reservation.Attempt.AttemptId,
                        "redeem:" + reservation.Attempt.CodeId,
                        "Redeem",
                        reservation.Attempt.CodeId,
                        "System",
                        "commerce:redeem",
                        command.CorrelationId),
                    cancellationToken).ConfigureAwait(false);
                var resolved = store.ResolveRedemptionGrant(new RedeemGrantResolution(
                    reservation.Attempt.AttemptId,
                    CommerceGrantSupport.Classify(grantResult.Operation),
                    grantResult.Operation.OperationId,
                    grantResult.Operation.ErrorCode,
                    UtcNow()));
                return Result(resolved);
            }
            catch (Exception exception) when (CommerceGrantSupport.IsKnownPreDispatchFailure(exception))
            {
                return Result(store.ResolveRedemptionGrant(new RedeemGrantResolution(
                    reservation.Attempt.AttemptId,
                    CommerceGrantResolutionKind.FailedBeforeSideEffects,
                    null,
                    exception.Message,
                    UtcNow())));
            }
            catch
            {
                return Result(store.ResolveRedemptionGrant(new RedeemGrantResolution(
                    reservation.Attempt.AttemptId,
                    CommerceGrantResolutionKind.PendingReconciliation,
                    null,
                    "reward_delivery_result_unknown",
                    UtcNow())));
            }
        }

        private DateTimeOffset UtcNow() => CommerceGrantSupport.UtcNow(utcClock);

        private static RedeemCodeResult Result(RedeemAttemptSnapshot attempt) =>
            new RedeemCodeResult(attempt.State switch
            {
                RedeemAttemptState.Succeeded => RedeemRequestStatus.Succeeded,
                RedeemAttemptState.PendingReconciliation => RedeemRequestStatus.PendingReconciliation,
                RedeemAttemptState.Failed => RedeemRequestStatus.Failed,
                RedeemAttemptState.Rejected => RedeemRequestStatus.Rejected,
                _ => RedeemRequestStatus.Pending
            }, attempt);

        private static RedeemRequestStatus Map(RedeemReservationStatus status) => status switch
        {
            RedeemReservationStatus.InvalidCode => RedeemRequestStatus.InvalidCode,
            RedeemReservationStatus.Disabled => RedeemRequestStatus.Disabled,
            RedeemReservationStatus.NotYetValid => RedeemRequestStatus.NotYetValid,
            RedeemReservationStatus.Expired => RedeemRequestStatus.Expired,
            RedeemReservationStatus.GlobalLimitReached => RedeemRequestStatus.GlobalLimitReached,
            RedeemReservationStatus.PlayerLimitReached => RedeemRequestStatus.PlayerLimitReached,
            _ => RedeemRequestStatus.Pending
        };
    }

    internal static class CommerceGrantSupport
    {
        internal static CommerceGrantResolutionKind Classify(GrantOperationSnapshot operation)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            if (operation.State == GrantOperationState.Completed)
                return CommerceGrantResolutionKind.Completed;
            if (operation.State == GrantOperationState.Failed && operation.Entries.All(entry =>
                    entry.DeliveryOperationId == null && entry.LedgerTransactionId == null))
                return CommerceGrantResolutionKind.FailedBeforeSideEffects;
            return CommerceGrantResolutionKind.PendingReconciliation;
        }

        internal static bool IsKnownPreDispatchFailure(Exception exception) =>
            exception is RewardPackageNotFoundException ||
            exception is RewardCatalogValidationException ||
            exception is RewardIdempotencyConflictException ||
            exception is InvalidOperationException invalid &&
                string.Equals(invalid.Message, "reward_package_disabled", StringComparison.Ordinal);

        internal static DateTimeOffset UtcNow(Func<DateTimeOffset> utcClock)
        {
            var value = utcClock();
            CommerceValidation.RequireUtc(value, nameof(utcClock));
            return value;
        }
    }
}
