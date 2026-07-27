using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LSTY.SevenDPanel.Application.Commerce
{
    public enum PurchaseState
    {
        Reserved,
        Dispatching,
        PendingReconciliation,
        Completed,
        Failed,
        Refunded
    }

    public enum PurchaseReservationStatus
    {
        Reserved,
        ProductDisabled,
        AccountDisabled,
        AccountFrozen,
        InsufficientFunds,
        OutOfStock,
        PlayerLimitReached
    }

    public enum PurchaseRequestStatus
    {
        Reserved,
        Completed,
        PendingReconciliation,
        Failed,
        ProductDisabled,
        AccountDisabled,
        AccountFrozen,
        InsufficientFunds,
        OutOfStock,
        PlayerLimitReached
    }

    public enum RedeemAttemptState
    {
        Pending,
        Succeeded,
        Rejected,
        PendingReconciliation,
        Failed
    }

    public enum RedeemReservationStatus
    {
        Reserved,
        InvalidCode,
        Disabled,
        NotYetValid,
        Expired,
        GlobalLimitReached,
        PlayerLimitReached
    }

    public enum RedeemRequestStatus
    {
        Pending,
        Succeeded,
        Rejected,
        PendingReconciliation,
        Failed,
        InvalidCode,
        Disabled,
        NotYetValid,
        Expired,
        GlobalLimitReached,
        PlayerLimitReached
    }

    public enum AchievementStatistic
    {
        Level,
        ZombieKills,
        PlayerKills,
        Deaths
    }

    public enum EvidenceGapPolicy
    {
        Paused,
        Incomplete
    }

    public enum RewardEligibilityState
    {
        Eligible,
        GrantReserved,
        Granted,
        Paused,
        Incomplete,
        PendingReconciliation,
        Failed
    }

    public enum CommerceGrantResolutionKind
    {
        Completed,
        FailedBeforeSideEffects,
        PendingReconciliation
    }

    public sealed class ShopProductDraft
    {
        public ShopProductDraft(
            string productId,
            string name,
            string description,
            bool enabled,
            long priceAmount,
            long? stockRemaining,
            int? perPlayerLimit,
            string rewardPackageId,
            int sortOrder)
        {
            ProductId = CommerceValidation.RequireText(productId, nameof(productId));
            Name = CommerceValidation.RequireText(name, nameof(name));
            Description = description ?? throw new ArgumentNullException(nameof(description));
            if (priceAmount < 0) throw new ArgumentOutOfRangeException(nameof(priceAmount));
            if (stockRemaining < 0) throw new ArgumentOutOfRangeException(nameof(stockRemaining));
            if (perPlayerLimit <= 0) throw new ArgumentOutOfRangeException(nameof(perPlayerLimit));
            RewardPackageId = CommerceValidation.RequireText(rewardPackageId, nameof(rewardPackageId));
            Enabled = enabled;
            PriceAmount = priceAmount;
            StockRemaining = stockRemaining;
            PerPlayerLimit = perPlayerLimit;
            SortOrder = sortOrder;
        }

        public string ProductId { get; }
        public string Name { get; }
        public string Description { get; }
        public bool Enabled { get; }
        public long PriceAmount { get; }
        public long? StockRemaining { get; }
        public int? PerPlayerLimit { get; }
        public string RewardPackageId { get; }
        public int SortOrder { get; }
    }

    public sealed class ShopProductSnapshot
    {
        public ShopProductSnapshot(
            ShopProductDraft draft,
            DateTimeOffset createdAtUtc,
            DateTimeOffset updatedAtUtc,
            long rowVersion)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            CommerceValidation.RequireUtc(createdAtUtc, nameof(createdAtUtc));
            CommerceValidation.RequireUtc(updatedAtUtc, nameof(updatedAtUtc));
            if (updatedAtUtc < createdAtUtc) throw new ArgumentOutOfRangeException(nameof(updatedAtUtc));
            if (rowVersion < 0) throw new ArgumentOutOfRangeException(nameof(rowVersion));
            ProductId = draft.ProductId;
            Name = draft.Name;
            Description = draft.Description;
            Enabled = draft.Enabled;
            PriceAmount = draft.PriceAmount;
            StockRemaining = draft.StockRemaining;
            PerPlayerLimit = draft.PerPlayerLimit;
            RewardPackageId = draft.RewardPackageId;
            SortOrder = draft.SortOrder;
            CreatedAtUtc = createdAtUtc;
            UpdatedAtUtc = updatedAtUtc;
            RowVersion = rowVersion;
        }

        public string ProductId { get; }
        public string Name { get; }
        public string Description { get; }
        public bool Enabled { get; }
        public long PriceAmount { get; }
        public long? StockRemaining { get; }
        public int? PerPlayerLimit { get; }
        public string RewardPackageId { get; }
        public int SortOrder { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset UpdatedAtUtc { get; }
        public long RowVersion { get; }
    }

    public sealed class PurchaseReservationRequest
    {
        public PurchaseReservationRequest(
            string purchaseId,
            string reservationId,
            string productId,
            string crossplatformId,
            int quantity,
            string idempotencyKey,
            string? correlationId,
            DateTimeOffset occurredAtUtc,
            DateTimeOffset? expiresAtUtc)
        {
            PurchaseId = CommerceValidation.RequireText(purchaseId, nameof(purchaseId));
            ReservationId = CommerceValidation.RequireText(reservationId, nameof(reservationId));
            ProductId = CommerceValidation.RequireText(productId, nameof(productId));
            CrossplatformId = CommerceValidation.RequireText(crossplatformId, nameof(crossplatformId));
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            IdempotencyKey = CommerceValidation.RequireText(idempotencyKey, nameof(idempotencyKey));
            CorrelationId = CommerceValidation.OptionalText(correlationId);
            CommerceValidation.RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
            if (expiresAtUtc.HasValue)
            {
                CommerceValidation.RequireUtc(expiresAtUtc.Value, nameof(expiresAtUtc));
                if (expiresAtUtc.Value <= occurredAtUtc)
                    throw new ArgumentOutOfRangeException(nameof(expiresAtUtc));
            }
            Quantity = quantity;
            OccurredAtUtc = occurredAtUtc;
            ExpiresAtUtc = expiresAtUtc;
        }

        public string PurchaseId { get; }
        public string ReservationId { get; }
        public string ProductId { get; }
        public string CrossplatformId { get; }
        public int Quantity { get; }
        public string IdempotencyKey { get; }
        public string? CorrelationId { get; }
        public DateTimeOffset OccurredAtUtc { get; }
        public DateTimeOffset? ExpiresAtUtc { get; }
    }

    public sealed class ShopPurchaseSnapshot
    {
        public ShopPurchaseSnapshot(
            string purchaseId,
            string productId,
            string rewardPackageId,
            string crossplatformId,
            int quantity,
            long unitPrice,
            long totalAmount,
            PurchaseState state,
            string idempotencyKey,
            string? reservationId,
            string? capturedTransactionId,
            string? grantOperationId,
            string? correlationId,
            string? errorCode,
            DateTimeOffset createdAtUtc,
            DateTimeOffset updatedAtUtc,
            DateTimeOffset? completedAtUtc,
            long rowVersion)
        {
            PurchaseId = CommerceValidation.RequireText(purchaseId, nameof(purchaseId));
            ProductId = CommerceValidation.RequireText(productId, nameof(productId));
            RewardPackageId = CommerceValidation.RequireText(rewardPackageId, nameof(rewardPackageId));
            CrossplatformId = CommerceValidation.RequireText(crossplatformId, nameof(crossplatformId));
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            if (unitPrice < 0) throw new ArgumentOutOfRangeException(nameof(unitPrice));
            if (totalAmount < 0) throw new ArgumentOutOfRangeException(nameof(totalAmount));
            CommerceValidation.RequireDefined(state, nameof(state));
            IdempotencyKey = CommerceValidation.RequireText(idempotencyKey, nameof(idempotencyKey));
            ReservationId = CommerceValidation.OptionalText(reservationId);
            CapturedTransactionId = CommerceValidation.OptionalText(capturedTransactionId);
            GrantOperationId = CommerceValidation.OptionalText(grantOperationId);
            CorrelationId = CommerceValidation.OptionalText(correlationId);
            ErrorCode = CommerceValidation.OptionalText(errorCode);
            CommerceValidation.RequireUtc(createdAtUtc, nameof(createdAtUtc));
            CommerceValidation.RequireUtc(updatedAtUtc, nameof(updatedAtUtc));
            if (completedAtUtc.HasValue) CommerceValidation.RequireUtc(completedAtUtc.Value, nameof(completedAtUtc));
            if (rowVersion < 0) throw new ArgumentOutOfRangeException(nameof(rowVersion));
            Quantity = quantity;
            UnitPrice = unitPrice;
            TotalAmount = totalAmount;
            State = state;
            CreatedAtUtc = createdAtUtc;
            UpdatedAtUtc = updatedAtUtc;
            CompletedAtUtc = completedAtUtc;
            RowVersion = rowVersion;
        }

        public string PurchaseId { get; }
        public string ProductId { get; }
        public string RewardPackageId { get; }
        public string CrossplatformId { get; }
        public int Quantity { get; }
        public long UnitPrice { get; }
        public long TotalAmount { get; }
        public PurchaseState State { get; }
        public string IdempotencyKey { get; }
        public string? ReservationId { get; }
        public string? CapturedTransactionId { get; }
        public string? GrantOperationId { get; }
        public string? CorrelationId { get; }
        public string? ErrorCode { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset UpdatedAtUtc { get; }
        public DateTimeOffset? CompletedAtUtc { get; }
        public long RowVersion { get; }
    }

    public sealed class PurchaseReservationResult
    {
        public PurchaseReservationResult(
            PurchaseReservationStatus status,
            ShopPurchaseSnapshot? purchase,
            bool created)
        {
            CommerceValidation.RequireDefined(status, nameof(status));
            Status = status;
            Purchase = purchase;
            Created = created;
        }

        public PurchaseReservationStatus Status { get; }
        public ShopPurchaseSnapshot? Purchase { get; }
        public bool Created { get; }
    }

    public sealed class PurchaseProductCommand
    {
        public PurchaseProductCommand(
            string productId,
            string crossplatformId,
            int expectedEntityId,
            string expectedWorldId,
            int quantity,
            string idempotencyKey,
            string? correlationId)
        {
            ProductId = CommerceValidation.RequireText(productId, nameof(productId));
            CrossplatformId = CommerceValidation.RequireText(crossplatformId, nameof(crossplatformId));
            if (expectedEntityId < 0) throw new ArgumentOutOfRangeException(nameof(expectedEntityId));
            ExpectedWorldId = CommerceValidation.RequireText(expectedWorldId, nameof(expectedWorldId));
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            IdempotencyKey = CommerceValidation.RequireText(idempotencyKey, nameof(idempotencyKey));
            CorrelationId = CommerceValidation.OptionalText(correlationId);
            ExpectedEntityId = expectedEntityId;
            Quantity = quantity;
        }

        public string ProductId { get; }
        public string CrossplatformId { get; }
        public int ExpectedEntityId { get; }
        public string ExpectedWorldId { get; }
        public int Quantity { get; }
        public string IdempotencyKey { get; }
        public string? CorrelationId { get; }
    }

    public sealed class PurchaseProductResult
    {
        public PurchaseProductResult(PurchaseRequestStatus status, ShopPurchaseSnapshot? purchase)
        {
            CommerceValidation.RequireDefined(status, nameof(status));
            Status = status;
            Purchase = purchase;
        }

        public PurchaseRequestStatus Status { get; }
        public ShopPurchaseSnapshot? Purchase { get; }
    }

    public sealed class PurchaseGrantResolution
    {
        public PurchaseGrantResolution(
            string purchaseId,
            CommerceGrantResolutionKind kind,
            string? grantOperationId,
            string? captureTransactionId,
            string? errorCode,
            DateTimeOffset occurredAtUtc)
        {
            PurchaseId = CommerceValidation.RequireText(purchaseId, nameof(purchaseId));
            CommerceValidation.RequireDefined(kind, nameof(kind));
            GrantOperationId = CommerceValidation.OptionalText(grantOperationId);
            CaptureTransactionId = CommerceValidation.OptionalText(captureTransactionId);
            ErrorCode = CommerceValidation.OptionalText(errorCode);
            CommerceValidation.RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
            if (kind == CommerceGrantResolutionKind.Completed && CaptureTransactionId == null)
                throw new ArgumentException("A completed purchase requires a capture transaction.");
            Kind = kind;
            OccurredAtUtc = occurredAtUtc;
        }

        public string PurchaseId { get; }
        public CommerceGrantResolutionKind Kind { get; }
        public string? GrantOperationId { get; }
        public string? CaptureTransactionId { get; }
        public string? ErrorCode { get; }
        public DateTimeOffset OccurredAtUtc { get; }
    }

    public sealed class RedeemCodeSecretDraft
    {
        public RedeemCodeSecretDraft(
            string codeId,
            string normalizedCodeDigest,
            string lastFour,
            int normalizationVersion,
            string rewardPackageId,
            bool enabled,
            DateTimeOffset? validFromUtc,
            DateTimeOffset? expiresAtUtc,
            int? maxRedemptions,
            int? perPlayerLimit)
        {
            CodeId = CommerceValidation.RequireText(codeId, nameof(codeId));
            NormalizedCodeDigest = CommerceValidation.RequireDigest(normalizedCodeDigest, nameof(normalizedCodeDigest));
            LastFour = CommerceValidation.RequireLastFour(lastFour, nameof(lastFour));
            if (normalizationVersion <= 0) throw new ArgumentOutOfRangeException(nameof(normalizationVersion));
            RewardPackageId = CommerceValidation.RequireText(rewardPackageId, nameof(rewardPackageId));
            if (validFromUtc.HasValue) CommerceValidation.RequireUtc(validFromUtc.Value, nameof(validFromUtc));
            if (expiresAtUtc.HasValue) CommerceValidation.RequireUtc(expiresAtUtc.Value, nameof(expiresAtUtc));
            if (validFromUtc.HasValue && expiresAtUtc.HasValue && expiresAtUtc <= validFromUtc)
                throw new ArgumentOutOfRangeException(nameof(expiresAtUtc));
            if (maxRedemptions <= 0) throw new ArgumentOutOfRangeException(nameof(maxRedemptions));
            if (perPlayerLimit <= 0) throw new ArgumentOutOfRangeException(nameof(perPlayerLimit));
            NormalizationVersion = normalizationVersion;
            Enabled = enabled;
            ValidFromUtc = validFromUtc;
            ExpiresAtUtc = expiresAtUtc;
            MaxRedemptions = maxRedemptions;
            PerPlayerLimit = perPlayerLimit;
        }

        public string CodeId { get; }
        public string NormalizedCodeDigest { get; }
        public string LastFour { get; }
        public int NormalizationVersion { get; }
        public string RewardPackageId { get; }
        public bool Enabled { get; }
        public DateTimeOffset? ValidFromUtc { get; }
        public DateTimeOffset? ExpiresAtUtc { get; }
        public int? MaxRedemptions { get; }
        public int? PerPlayerLimit { get; }
    }

    public sealed class RedeemCodeSnapshot
    {
        public RedeemCodeSnapshot(
            string codeId,
            string maskedCode,
            int normalizationVersion,
            string rewardPackageId,
            bool enabled,
            DateTimeOffset? validFromUtc,
            DateTimeOffset? expiresAtUtc,
            int? maxRedemptions,
            int? perPlayerLimit,
            int redemptionCount,
            DateTimeOffset createdAtUtc,
            DateTimeOffset updatedAtUtc,
            long rowVersion)
        {
            CodeId = CommerceValidation.RequireText(codeId, nameof(codeId));
            MaskedCode = CommerceValidation.RequireText(maskedCode, nameof(maskedCode));
            if (normalizationVersion <= 0) throw new ArgumentOutOfRangeException(nameof(normalizationVersion));
            RewardPackageId = CommerceValidation.RequireText(rewardPackageId, nameof(rewardPackageId));
            if (redemptionCount < 0) throw new ArgumentOutOfRangeException(nameof(redemptionCount));
            CommerceValidation.RequireUtc(createdAtUtc, nameof(createdAtUtc));
            CommerceValidation.RequireUtc(updatedAtUtc, nameof(updatedAtUtc));
            if (rowVersion < 0) throw new ArgumentOutOfRangeException(nameof(rowVersion));
            NormalizationVersion = normalizationVersion;
            Enabled = enabled;
            ValidFromUtc = validFromUtc;
            ExpiresAtUtc = expiresAtUtc;
            MaxRedemptions = maxRedemptions;
            PerPlayerLimit = perPlayerLimit;
            RedemptionCount = redemptionCount;
            CreatedAtUtc = createdAtUtc;
            UpdatedAtUtc = updatedAtUtc;
            RowVersion = rowVersion;
        }

        public string CodeId { get; }
        public string MaskedCode { get; }
        public int NormalizationVersion { get; }
        public string RewardPackageId { get; }
        public bool Enabled { get; }
        public DateTimeOffset? ValidFromUtc { get; }
        public DateTimeOffset? ExpiresAtUtc { get; }
        public int? MaxRedemptions { get; }
        public int? PerPlayerLimit { get; }
        public int RedemptionCount { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset UpdatedAtUtc { get; }
        public long RowVersion { get; }
    }

    public sealed class CreateRedeemCodeCommand
    {
        public CreateRedeemCodeCommand(
            string rewardPackageId,
            bool enabled,
            DateTimeOffset? validFromUtc,
            DateTimeOffset? expiresAtUtc,
            int? maxRedemptions,
            int? perPlayerLimit)
        {
            RewardPackageId = CommerceValidation.RequireText(rewardPackageId, nameof(rewardPackageId));
            if (validFromUtc.HasValue) CommerceValidation.RequireUtc(validFromUtc.Value, nameof(validFromUtc));
            if (expiresAtUtc.HasValue) CommerceValidation.RequireUtc(expiresAtUtc.Value, nameof(expiresAtUtc));
            if (validFromUtc.HasValue && expiresAtUtc.HasValue && expiresAtUtc <= validFromUtc)
                throw new ArgumentOutOfRangeException(nameof(expiresAtUtc));
            if (maxRedemptions <= 0) throw new ArgumentOutOfRangeException(nameof(maxRedemptions));
            if (perPlayerLimit <= 0) throw new ArgumentOutOfRangeException(nameof(perPlayerLimit));
            Enabled = enabled;
            ValidFromUtc = validFromUtc;
            ExpiresAtUtc = expiresAtUtc;
            MaxRedemptions = maxRedemptions;
            PerPlayerLimit = perPlayerLimit;
        }

        public string RewardPackageId { get; }
        public bool Enabled { get; }
        public DateTimeOffset? ValidFromUtc { get; }
        public DateTimeOffset? ExpiresAtUtc { get; }
        public int? MaxRedemptions { get; }
        public int? PerPlayerLimit { get; }
    }

    public sealed class GeneratedRedeemCode
    {
        public GeneratedRedeemCode(string plaintextCode, RedeemCodeSnapshot definition)
        {
            PlaintextCode = CommerceValidation.RequireText(plaintextCode, nameof(plaintextCode));
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        }

        public string PlaintextCode { get; }
        public RedeemCodeSnapshot Definition { get; }
    }

    public sealed class RedeemReservationRequest
    {
        public RedeemReservationRequest(
            string attemptId,
            string normalizedCodeDigest,
            int normalizationVersion,
            string crossplatformId,
            string? correlationId,
            DateTimeOffset attemptedAtUtc)
        {
            AttemptId = CommerceValidation.RequireText(attemptId, nameof(attemptId));
            NormalizedCodeDigest = CommerceValidation.RequireDigest(normalizedCodeDigest, nameof(normalizedCodeDigest));
            if (normalizationVersion <= 0) throw new ArgumentOutOfRangeException(nameof(normalizationVersion));
            CrossplatformId = CommerceValidation.RequireText(crossplatformId, nameof(crossplatformId));
            CorrelationId = CommerceValidation.OptionalText(correlationId);
            CommerceValidation.RequireUtc(attemptedAtUtc, nameof(attemptedAtUtc));
            NormalizationVersion = normalizationVersion;
            AttemptedAtUtc = attemptedAtUtc;
        }

        public string AttemptId { get; }
        public string NormalizedCodeDigest { get; }
        public int NormalizationVersion { get; }
        public string CrossplatformId { get; }
        public string? CorrelationId { get; }
        public DateTimeOffset AttemptedAtUtc { get; }
    }

    public sealed class RedeemAttemptSnapshot
    {
        public RedeemAttemptSnapshot(
            string attemptId,
            string codeId,
            string rewardPackageId,
            string crossplatformId,
            RedeemAttemptState state,
            string? resultCode,
            string? grantOperationId,
            string? correlationId,
            DateTimeOffset attemptedAtUtc)
        {
            AttemptId = CommerceValidation.RequireText(attemptId, nameof(attemptId));
            CodeId = CommerceValidation.RequireText(codeId, nameof(codeId));
            RewardPackageId = CommerceValidation.RequireText(rewardPackageId, nameof(rewardPackageId));
            CrossplatformId = CommerceValidation.RequireText(crossplatformId, nameof(crossplatformId));
            CommerceValidation.RequireDefined(state, nameof(state));
            ResultCode = CommerceValidation.OptionalText(resultCode);
            GrantOperationId = CommerceValidation.OptionalText(grantOperationId);
            CorrelationId = CommerceValidation.OptionalText(correlationId);
            CommerceValidation.RequireUtc(attemptedAtUtc, nameof(attemptedAtUtc));
            State = state;
            AttemptedAtUtc = attemptedAtUtc;
        }

        public string AttemptId { get; }
        public string CodeId { get; }
        public string RewardPackageId { get; }
        public string CrossplatformId { get; }
        public RedeemAttemptState State { get; }
        public string? ResultCode { get; }
        public string? GrantOperationId { get; }
        public string? CorrelationId { get; }
        public DateTimeOffset AttemptedAtUtc { get; }
    }

    public sealed class RedemptionReservationResult
    {
        public RedemptionReservationResult(
            RedeemReservationStatus status,
            RedeemAttemptSnapshot? attempt,
            bool created)
        {
            CommerceValidation.RequireDefined(status, nameof(status));
            Status = status;
            Attempt = attempt;
            Created = created;
        }

        public RedeemReservationStatus Status { get; }
        public RedeemAttemptSnapshot? Attempt { get; }
        public bool Created { get; }
    }

    public sealed class RedeemCodeCommand
    {
        public RedeemCodeCommand(
            string code,
            string crossplatformId,
            int expectedEntityId,
            string expectedWorldId,
            string? correlationId)
        {
            Code = CommerceValidation.RequireText(code, nameof(code));
            CrossplatformId = CommerceValidation.RequireText(crossplatformId, nameof(crossplatformId));
            if (expectedEntityId < 0) throw new ArgumentOutOfRangeException(nameof(expectedEntityId));
            ExpectedWorldId = CommerceValidation.RequireText(expectedWorldId, nameof(expectedWorldId));
            CorrelationId = CommerceValidation.OptionalText(correlationId);
            ExpectedEntityId = expectedEntityId;
        }

        public string Code { get; }
        public string CrossplatformId { get; }
        public int ExpectedEntityId { get; }
        public string ExpectedWorldId { get; }
        public string? CorrelationId { get; }
    }

    public sealed class RedeemCodeResult
    {
        public RedeemCodeResult(RedeemRequestStatus status, RedeemAttemptSnapshot? attempt)
        {
            CommerceValidation.RequireDefined(status, nameof(status));
            Status = status;
            Attempt = attempt;
        }

        public RedeemRequestStatus Status { get; }
        public RedeemAttemptSnapshot? Attempt { get; }
    }

    public sealed class RedeemGrantResolution
    {
        public RedeemGrantResolution(
            string attemptId,
            CommerceGrantResolutionKind kind,
            string? grantOperationId,
            string? errorCode,
            DateTimeOffset occurredAtUtc)
        {
            AttemptId = CommerceValidation.RequireText(attemptId, nameof(attemptId));
            CommerceValidation.RequireDefined(kind, nameof(kind));
            GrantOperationId = CommerceValidation.OptionalText(grantOperationId);
            ErrorCode = CommerceValidation.OptionalText(errorCode);
            CommerceValidation.RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
            Kind = kind;
            OccurredAtUtc = occurredAtUtc;
        }

        public string AttemptId { get; }
        public CommerceGrantResolutionKind Kind { get; }
        public string? GrantOperationId { get; }
        public string? ErrorCode { get; }
        public DateTimeOffset OccurredAtUtc { get; }
    }

    public sealed class AchievementDefinitionDraft
    {
        public AchievementDefinitionDraft(
            string achievementId,
            string name,
            string description,
            AchievementStatistic statistic,
            long thresholdValue,
            string rewardPackageId,
            bool enabled,
            int sortOrder)
        {
            AchievementId = CommerceValidation.RequireText(achievementId, nameof(achievementId));
            Name = CommerceValidation.RequireText(name, nameof(name));
            Description = description ?? throw new ArgumentNullException(nameof(description));
            CommerceValidation.RequireDefined(statistic, nameof(statistic));
            if (thresholdValue < 0) throw new ArgumentOutOfRangeException(nameof(thresholdValue));
            RewardPackageId = CommerceValidation.RequireText(rewardPackageId, nameof(rewardPackageId));
            Statistic = statistic;
            ThresholdValue = thresholdValue;
            Enabled = enabled;
            SortOrder = sortOrder;
        }

        public string AchievementId { get; }
        public string Name { get; }
        public string Description { get; }
        public AchievementStatistic Statistic { get; }
        public long ThresholdValue { get; }
        public string RewardPackageId { get; }
        public bool Enabled { get; }
        public int SortOrder { get; }
    }

    public sealed class AchievementDefinitionSnapshot
    {
        public AchievementDefinitionSnapshot(
            AchievementDefinitionDraft draft,
            DateTimeOffset createdAtUtc,
            DateTimeOffset updatedAtUtc,
            long rowVersion)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            CommerceValidation.RequireUtc(createdAtUtc, nameof(createdAtUtc));
            CommerceValidation.RequireUtc(updatedAtUtc, nameof(updatedAtUtc));
            if (rowVersion < 0) throw new ArgumentOutOfRangeException(nameof(rowVersion));
            AchievementId = draft.AchievementId;
            Name = draft.Name;
            Description = draft.Description;
            Statistic = draft.Statistic;
            ThresholdValue = draft.ThresholdValue;
            RewardPackageId = draft.RewardPackageId;
            Enabled = draft.Enabled;
            SortOrder = draft.SortOrder;
            CreatedAtUtc = createdAtUtc;
            UpdatedAtUtc = updatedAtUtc;
            RowVersion = rowVersion;
        }

        public string AchievementId { get; }
        public string Name { get; }
        public string Description { get; }
        public AchievementStatistic Statistic { get; }
        public long ThresholdValue { get; }
        public string RewardPackageId { get; }
        public bool Enabled { get; }
        public int SortOrder { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset UpdatedAtUtc { get; }
        public long RowVersion { get; }
    }

    public sealed class AchievementProgressSnapshot
    {
        public AchievementProgressSnapshot(
            string achievementId,
            string crossplatformId,
            long currentValue,
            string? eligibilityKey,
            string? grantOperationId,
            DateTimeOffset? completedAtUtc,
            DateTimeOffset updatedAtUtc,
            long rowVersion)
        {
            AchievementId = CommerceValidation.RequireText(achievementId, nameof(achievementId));
            CrossplatformId = CommerceValidation.RequireText(crossplatformId, nameof(crossplatformId));
            if (currentValue < 0) throw new ArgumentOutOfRangeException(nameof(currentValue));
            EligibilityKey = CommerceValidation.OptionalText(eligibilityKey);
            GrantOperationId = CommerceValidation.OptionalText(grantOperationId);
            CommerceValidation.RequireUtc(updatedAtUtc, nameof(updatedAtUtc));
            if (completedAtUtc.HasValue) CommerceValidation.RequireUtc(completedAtUtc.Value, nameof(completedAtUtc));
            if (rowVersion < 0) throw new ArgumentOutOfRangeException(nameof(rowVersion));
            CurrentValue = currentValue;
            CompletedAtUtc = completedAtUtc;
            UpdatedAtUtc = updatedAtUtc;
            RowVersion = rowVersion;
        }

        public string AchievementId { get; }
        public string CrossplatformId { get; }
        public long CurrentValue { get; }
        public string? EligibilityKey { get; }
        public string? GrantOperationId { get; }
        public DateTimeOffset? CompletedAtUtc { get; }
        public DateTimeOffset UpdatedAtUtc { get; }
        public long RowVersion { get; }
    }

    public sealed class ObserveAchievementCommand
    {
        public ObserveAchievementCommand(
            string evidenceId,
            string crossplatformId,
            int expectedEntityId,
            string expectedWorldId,
            AchievementStatistic statistic,
            long value,
            string? correlationId,
            DateTimeOffset observedAtUtc)
        {
            EvidenceId = CommerceValidation.RequireText(evidenceId, nameof(evidenceId));
            CrossplatformId = CommerceValidation.RequireText(crossplatformId, nameof(crossplatformId));
            if (expectedEntityId < 0) throw new ArgumentOutOfRangeException(nameof(expectedEntityId));
            ExpectedWorldId = CommerceValidation.RequireText(expectedWorldId, nameof(expectedWorldId));
            CommerceValidation.RequireDefined(statistic, nameof(statistic));
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            CorrelationId = CommerceValidation.OptionalText(correlationId);
            CommerceValidation.RequireUtc(observedAtUtc, nameof(observedAtUtc));
            ExpectedEntityId = expectedEntityId;
            Statistic = statistic;
            Value = value;
            ObservedAtUtc = observedAtUtc;
        }

        public string EvidenceId { get; }
        public string CrossplatformId { get; }
        public int ExpectedEntityId { get; }
        public string ExpectedWorldId { get; }
        public AchievementStatistic Statistic { get; }
        public long Value { get; }
        public string? CorrelationId { get; }
        public DateTimeOffset ObservedAtUtc { get; }
    }

    public sealed class OnlineRewardRuleDraft
    {
        public OnlineRewardRuleDraft(
            string ruleId,
            string name,
            TimeSpan requiredOnline,
            TimeSpan? repeatInterval,
            EvidenceGapPolicy gapPolicy,
            string rewardPackageId,
            bool enabled,
            int sortOrder)
        {
            RuleId = CommerceValidation.RequireText(ruleId, nameof(ruleId));
            Name = CommerceValidation.RequireText(name, nameof(name));
            if (requiredOnline <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(requiredOnline));
            if (repeatInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(repeatInterval));
            CommerceValidation.RequireDefined(gapPolicy, nameof(gapPolicy));
            RewardPackageId = CommerceValidation.RequireText(rewardPackageId, nameof(rewardPackageId));
            RequiredOnline = requiredOnline;
            RepeatInterval = repeatInterval;
            GapPolicy = gapPolicy;
            Enabled = enabled;
            SortOrder = sortOrder;
        }

        public string RuleId { get; }
        public string Name { get; }
        public TimeSpan RequiredOnline { get; }
        public TimeSpan? RepeatInterval { get; }
        public EvidenceGapPolicy GapPolicy { get; }
        public string RewardPackageId { get; }
        public bool Enabled { get; }
        public int SortOrder { get; }
    }

    public sealed class OnlineRewardRuleSnapshot
    {
        public OnlineRewardRuleSnapshot(
            OnlineRewardRuleDraft draft,
            DateTimeOffset createdAtUtc,
            DateTimeOffset updatedAtUtc,
            long rowVersion)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            CommerceValidation.RequireUtc(createdAtUtc, nameof(createdAtUtc));
            CommerceValidation.RequireUtc(updatedAtUtc, nameof(updatedAtUtc));
            if (rowVersion < 0) throw new ArgumentOutOfRangeException(nameof(rowVersion));
            RuleId = draft.RuleId;
            Name = draft.Name;
            RequiredOnline = draft.RequiredOnline;
            RepeatInterval = draft.RepeatInterval;
            GapPolicy = draft.GapPolicy;
            RewardPackageId = draft.RewardPackageId;
            Enabled = draft.Enabled;
            SortOrder = draft.SortOrder;
            CreatedAtUtc = createdAtUtc;
            UpdatedAtUtc = updatedAtUtc;
            RowVersion = rowVersion;
        }

        public string RuleId { get; }
        public string Name { get; }
        public TimeSpan RequiredOnline { get; }
        public TimeSpan? RepeatInterval { get; }
        public EvidenceGapPolicy GapPolicy { get; }
        public string RewardPackageId { get; }
        public bool Enabled { get; }
        public int SortOrder { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset UpdatedAtUtc { get; }
        public long RowVersion { get; }
    }

    public sealed class EvaluateOnlineRewardsCommand
    {
        public EvaluateOnlineRewardsCommand(
            string crossplatformId,
            int expectedEntityId,
            string expectedWorldId,
            DateTimeOffset evidenceToUtc,
            string? correlationId)
        {
            CrossplatformId = CommerceValidation.RequireText(crossplatformId, nameof(crossplatformId));
            if (expectedEntityId < 0) throw new ArgumentOutOfRangeException(nameof(expectedEntityId));
            ExpectedWorldId = CommerceValidation.RequireText(expectedWorldId, nameof(expectedWorldId));
            CommerceValidation.RequireUtc(evidenceToUtc, nameof(evidenceToUtc));
            CorrelationId = CommerceValidation.OptionalText(correlationId);
            ExpectedEntityId = expectedEntityId;
            EvidenceToUtc = evidenceToUtc;
        }

        public string CrossplatformId { get; }
        public int ExpectedEntityId { get; }
        public string ExpectedWorldId { get; }
        public DateTimeOffset EvidenceToUtc { get; }
        public string? CorrelationId { get; }
    }

    public sealed class RewardEligibilitySnapshot
    {
        public RewardEligibilitySnapshot(
            string eligibilityId,
            string ruleKind,
            string ruleId,
            string rewardPackageId,
            string crossplatformId,
            string eligibilityKey,
            RewardEligibilityState state,
            string? grantOperationId,
            string? correlationId,
            DateTimeOffset? evidenceFromUtc,
            DateTimeOffset? evidenceToUtc,
            DateTimeOffset createdAtUtc,
            DateTimeOffset updatedAtUtc,
            long rowVersion)
        {
            EligibilityId = CommerceValidation.RequireText(eligibilityId, nameof(eligibilityId));
            RuleKind = CommerceValidation.RequireText(ruleKind, nameof(ruleKind));
            RuleId = CommerceValidation.RequireText(ruleId, nameof(ruleId));
            RewardPackageId = CommerceValidation.RequireText(rewardPackageId, nameof(rewardPackageId));
            CrossplatformId = CommerceValidation.RequireText(crossplatformId, nameof(crossplatformId));
            EligibilityKey = CommerceValidation.RequireText(eligibilityKey, nameof(eligibilityKey));
            CommerceValidation.RequireDefined(state, nameof(state));
            GrantOperationId = CommerceValidation.OptionalText(grantOperationId);
            CorrelationId = CommerceValidation.OptionalText(correlationId);
            if (evidenceFromUtc.HasValue) CommerceValidation.RequireUtc(evidenceFromUtc.Value, nameof(evidenceFromUtc));
            if (evidenceToUtc.HasValue) CommerceValidation.RequireUtc(evidenceToUtc.Value, nameof(evidenceToUtc));
            CommerceValidation.RequireUtc(createdAtUtc, nameof(createdAtUtc));
            CommerceValidation.RequireUtc(updatedAtUtc, nameof(updatedAtUtc));
            if (rowVersion < 0) throw new ArgumentOutOfRangeException(nameof(rowVersion));
            State = state;
            EvidenceFromUtc = evidenceFromUtc;
            EvidenceToUtc = evidenceToUtc;
            CreatedAtUtc = createdAtUtc;
            UpdatedAtUtc = updatedAtUtc;
            RowVersion = rowVersion;
        }

        public string EligibilityId { get; }
        public string RuleKind { get; }
        public string RuleId { get; }
        public string RewardPackageId { get; }
        public string CrossplatformId { get; }
        public string EligibilityKey { get; }
        public RewardEligibilityState State { get; }
        public string? GrantOperationId { get; }
        public string? CorrelationId { get; }
        public DateTimeOffset? EvidenceFromUtc { get; }
        public DateTimeOffset? EvidenceToUtc { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset UpdatedAtUtc { get; }
        public long RowVersion { get; }
    }

    public sealed class EligibilityGrantResolution
    {
        public EligibilityGrantResolution(
            string eligibilityId,
            CommerceGrantResolutionKind kind,
            string? grantOperationId,
            string? errorCode,
            DateTimeOffset occurredAtUtc)
        {
            EligibilityId = CommerceValidation.RequireText(eligibilityId, nameof(eligibilityId));
            CommerceValidation.RequireDefined(kind, nameof(kind));
            GrantOperationId = CommerceValidation.OptionalText(grantOperationId);
            ErrorCode = CommerceValidation.OptionalText(errorCode);
            CommerceValidation.RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
            Kind = kind;
            OccurredAtUtc = occurredAtUtc;
        }

        public string EligibilityId { get; }
        public CommerceGrantResolutionKind Kind { get; }
        public string? GrantOperationId { get; }
        public string? ErrorCode { get; }
        public DateTimeOffset OccurredAtUtc { get; }
    }

    public sealed class ManualOnlineRewardCommand
    {
        public ManualOnlineRewardCommand(
            string ruleId,
            string crossplatformId,
            int expectedEntityId,
            string expectedWorldId,
            string idempotencyKey,
            string actorId,
            string? correlationId,
            DateTimeOffset occurredAtUtc)
        {
            RuleId = CommerceValidation.RequireText(ruleId, nameof(ruleId));
            CrossplatformId = CommerceValidation.RequireText(crossplatformId, nameof(crossplatformId));
            if (expectedEntityId < 0) throw new ArgumentOutOfRangeException(nameof(expectedEntityId));
            ExpectedWorldId = CommerceValidation.RequireText(expectedWorldId, nameof(expectedWorldId));
            IdempotencyKey = CommerceValidation.RequireText(idempotencyKey, nameof(idempotencyKey));
            ActorId = CommerceValidation.RequireText(actorId, nameof(actorId));
            CorrelationId = CommerceValidation.OptionalText(correlationId);
            CommerceValidation.RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
            ExpectedEntityId = expectedEntityId;
            OccurredAtUtc = occurredAtUtc;
        }

        public string RuleId { get; }
        public string CrossplatformId { get; }
        public int ExpectedEntityId { get; }
        public string ExpectedWorldId { get; }
        public string IdempotencyKey { get; }
        public string ActorId { get; }
        public string? CorrelationId { get; }
        public DateTimeOffset OccurredAtUtc { get; }
    }

    public sealed class CommerceIdempotencyConflictException : InvalidOperationException
    {
        public CommerceIdempotencyConflictException() : base("commerce_idempotency_conflict") { }
    }

    public sealed class CommerceConcurrencyException : InvalidOperationException
    {
        public CommerceConcurrencyException() : base("commerce_concurrency_conflict") { }
    }

    public static class RedeemCodeCodec
    {
        private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        public const int NormalizationVersion = 1;

        public static string Generate()
        {
            var bytes = new byte[16];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            var normalized = new char[16];
            for (var index = 0; index < normalized.Length; index++)
                normalized[index] = Alphabet[bytes[index] & 31];
            return Format(new string(normalized));
        }

        public static string Normalize(string code)
        {
            if (code == null) throw new ArgumentNullException(nameof(code));
            var builder = new StringBuilder(16);
            foreach (var value in code)
            {
                if (value == '-') continue;
                if (!((value >= 'A' && value <= 'Z') || (value >= '0' && value <= '9')))
                    throw new ArgumentException("A redeem code must contain uppercase ASCII letters and digits only.", nameof(code));
                builder.Append(value);
            }
            if (builder.Length != 16)
                throw new ArgumentException("A redeem code must contain exactly sixteen characters.", nameof(code));
            return builder.ToString();
        }

        public static string Digest(string normalizedCode)
        {
            var normalized = Normalize(normalizedCode);
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.ASCII.GetBytes(normalized));
            var builder = new StringBuilder(64);
            foreach (var value in bytes) builder.Append(value.ToString("X2"));
            return builder.ToString();
        }

        public static string Mask(string normalizedCode)
        {
            var normalized = Normalize(normalizedCode);
            return "****-****-****-" + normalized.Substring(12, 4);
        }

        public static string Format(string normalizedCode)
        {
            var normalized = Normalize(normalizedCode);
            return string.Join("-", Enumerable.Range(0, 4)
                .Select(index => normalized.Substring(index * 4, 4)));
        }
    }

    internal static class CommerceValidation
    {
        internal static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty value is required.", parameterName);
            return value.Trim();
        }

        internal static string? OptionalText(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

        internal static string RequireDigest(string value, string parameterName)
        {
            value = RequireText(value, parameterName);
            if (value.Length != 64 || value.Any(character =>
                    !((character >= '0' && character <= '9') ||
                      (character >= 'A' && character <= 'F'))))
                throw new ArgumentException("A SHA-256 hexadecimal digest is required.", parameterName);
            return value;
        }

        internal static string RequireLastFour(string value, string parameterName)
        {
            value = RequireText(value, parameterName);
            if (value.Length != 4 || value.Any(character =>
                    !((character >= '0' && character <= '9') ||
                      (character >= 'A' && character <= 'Z'))))
                throw new ArgumentException("Four uppercase ASCII characters are required.", parameterName);
            return value;
        }

        internal static void RequireUtc(DateTimeOffset value, string parameterName)
        {
            if (value.Offset != TimeSpan.Zero)
                throw new ArgumentException("A UTC timestamp is required.", parameterName);
        }

        internal static void RequireDefined<T>(T value, string parameterName) where T : struct, Enum
        {
            if (!Enum.IsDefined(typeof(T), value)) throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
