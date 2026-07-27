using System;
using System.Collections.Generic;
using System.Linq;
using LSTY.SevenDPanel.Domain.Rewards;

namespace LSTY.SevenDPanel.Application.Rewards
{
    public enum RewardEntryKind
    {
        Item,
        Currency,
        RegisteredAction
    }

    public enum RewardDeliveryStatus
    {
        Succeeded,
        Failed,
        ResultUnknown
    }

    public static class RewardRegisteredActions
    {
        public const string ResetSkills = "ResetSkills";
    }

    public sealed class RewardPackageEntryDraft
    {
        private RewardPackageEntryDraft(
            string entryId,
            RewardEntryKind kind,
            string? itemInternalName,
            GameResourceKind? itemKind,
            int? quantity,
            int? minQuality,
            int? maxQuality,
            string? catalogVersion,
            long? currencyAmount,
            string? registeredAction)
        {
            EntryId = RewardValidation.RequireText(entryId, nameof(entryId));
            RewardValidation.RequireDefined(kind, nameof(kind));
            Kind = kind;
            ItemInternalName = RewardValidation.OptionalText(itemInternalName);
            ItemKind = itemKind;
            Quantity = quantity;
            MinQuality = minQuality;
            MaxQuality = maxQuality;
            CatalogVersion = RewardValidation.OptionalText(catalogVersion);
            CurrencyAmount = currencyAmount;
            RegisteredAction = RewardValidation.OptionalText(registeredAction);
            ValidateTypedValues();
        }

        public string EntryId { get; }
        public RewardEntryKind Kind { get; }
        public string? ItemInternalName { get; }
        public GameResourceKind? ItemKind { get; }
        public int? Quantity { get; }
        public int? MinQuality { get; }
        public int? MaxQuality { get; }
        public string? CatalogVersion { get; }
        public long? CurrencyAmount { get; }
        public string? RegisteredAction { get; }

        public static RewardPackageEntryDraft Item(
            string entryId,
            string internalName,
            GameResourceKind itemKind,
            int quantity,
            int? minQuality,
            int? maxQuality,
            string catalogVersion) => new RewardPackageEntryDraft(
                entryId,
                RewardEntryKind.Item,
                internalName,
                itemKind,
                quantity,
                minQuality,
                maxQuality,
                catalogVersion,
                null,
                null);

        public static RewardPackageEntryDraft Currency(string entryId, long amount) =>
            new RewardPackageEntryDraft(
                entryId,
                RewardEntryKind.Currency,
                null,
                null,
                null,
                null,
                null,
                null,
                amount,
                null);

        public static RewardPackageEntryDraft RegisteredActionEntry(string entryId, string action) =>
            new RewardPackageEntryDraft(
                entryId,
                RewardEntryKind.RegisteredAction,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                action);

        private void ValidateTypedValues()
        {
            switch (Kind)
            {
                case RewardEntryKind.Item:
                    if (ItemInternalName == null || !ItemKind.HasValue ||
                        !Quantity.HasValue || Quantity.Value <= 0 || CatalogVersion == null)
                    {
                        throw new ArgumentException("An item reward requires typed item values.");
                    }
                    RewardValidation.RequireDefined(ItemKind.Value, nameof(ItemKind));
                    if (MinQuality.HasValue != MaxQuality.HasValue ||
                        MinQuality < 0 ||
                        (MinQuality.HasValue && MaxQuality < MinQuality))
                    {
                        throw new ArgumentOutOfRangeException(nameof(MinQuality));
                    }
                    if (CurrencyAmount.HasValue || RegisteredAction != null)
                        throw new ArgumentException("An item reward cannot contain other typed values.");
                    break;
                case RewardEntryKind.Currency:
                    if (!CurrencyAmount.HasValue || CurrencyAmount.Value < 0)
                        throw new ArgumentOutOfRangeException(nameof(CurrencyAmount));
                    if (ItemInternalName != null || ItemKind.HasValue || Quantity.HasValue ||
                        MinQuality.HasValue || MaxQuality.HasValue || CatalogVersion != null ||
                        RegisteredAction != null)
                    {
                        throw new ArgumentException("A currency reward cannot contain other typed values.");
                    }
                    break;
                case RewardEntryKind.RegisteredAction:
                    if (!string.Equals(
                            RegisteredAction,
                            RewardRegisteredActions.ResetSkills,
                            StringComparison.Ordinal))
                    {
                        throw new ArgumentException("Only ResetSkills is a registered reward action.");
                    }
                    if (ItemInternalName != null || ItemKind.HasValue || Quantity.HasValue ||
                        MinQuality.HasValue || MaxQuality.HasValue || CatalogVersion != null ||
                        CurrencyAmount.HasValue)
                    {
                        throw new ArgumentException("A registered action cannot contain other typed values.");
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(Kind));
            }
        }
    }

    public sealed class RewardPackageDraft
    {
        public RewardPackageDraft(
            string packageId,
            string name,
            string description,
            bool enabled,
            int sortOrder,
            IEnumerable<RewardPackageEntryDraft> entries)
        {
            PackageId = RewardValidation.RequireText(packageId, nameof(packageId));
            Name = RewardValidation.RequireText(name, nameof(name));
            Description = description?.Trim() ?? string.Empty;
            Enabled = enabled;
            SortOrder = sortOrder;
            Entries = entries?.ToArray() ?? throw new ArgumentNullException(nameof(entries));
            if (Entries.Count == 0)
                throw new ArgumentException("A reward package requires at least one entry.", nameof(entries));
            if (Entries.Any(entry => entry == null))
                throw new ArgumentException("Reward entries cannot contain null.", nameof(entries));
            if (Entries.Select(entry => entry.EntryId).Distinct(StringComparer.Ordinal).Count() != Entries.Count)
                throw new ArgumentException("Reward entry identifiers must be unique.", nameof(entries));
        }

        public string PackageId { get; }
        public string Name { get; }
        public string Description { get; }
        public bool Enabled { get; }
        public int SortOrder { get; }
        public IReadOnlyList<RewardPackageEntryDraft> Entries { get; }
    }

    public sealed class RewardPackageEntrySnapshot
    {
        public RewardPackageEntrySnapshot(
            string entryId,
            int ordinal,
            RewardEntryKind kind,
            string? itemInternalName,
            GameResourceKind? itemKind,
            int? quantity,
            int? minQuality,
            int? maxQuality,
            string? catalogVersion,
            long? currencyAmount,
            string? registeredAction)
        {
            if (ordinal < 0) throw new ArgumentOutOfRangeException(nameof(ordinal));
            EntryId = RewardValidation.RequireText(entryId, nameof(entryId));
            RewardValidation.RequireDefined(kind, nameof(kind));
            Ordinal = ordinal;
            Kind = kind;
            ItemInternalName = RewardValidation.OptionalText(itemInternalName);
            ItemKind = itemKind;
            Quantity = quantity;
            MinQuality = minQuality;
            MaxQuality = maxQuality;
            CatalogVersion = RewardValidation.OptionalText(catalogVersion);
            CurrencyAmount = currencyAmount;
            RegisteredAction = RewardValidation.OptionalText(registeredAction);
        }

        public string EntryId { get; }
        public int Ordinal { get; }
        public RewardEntryKind Kind { get; }
        public string? ItemInternalName { get; }
        public GameResourceKind? ItemKind { get; }
        public int? Quantity { get; }
        public int? MinQuality { get; }
        public int? MaxQuality { get; }
        public string? CatalogVersion { get; }
        public long? CurrencyAmount { get; }
        public string? RegisteredAction { get; }
    }

    public sealed class RewardPackageSnapshot
    {
        public RewardPackageSnapshot(
            string packageId,
            string name,
            string description,
            bool enabled,
            int sortOrder,
            DateTimeOffset createdAtUtc,
            DateTimeOffset updatedAtUtc,
            long rowVersion,
            IEnumerable<RewardPackageEntrySnapshot> entries)
        {
            PackageId = RewardValidation.RequireText(packageId, nameof(packageId));
            Name = RewardValidation.RequireText(name, nameof(name));
            Description = description ?? string.Empty;
            RewardValidation.RequireUtc(createdAtUtc, nameof(createdAtUtc));
            RewardValidation.RequireUtc(updatedAtUtc, nameof(updatedAtUtc));
            if (updatedAtUtc < createdAtUtc) throw new ArgumentOutOfRangeException(nameof(updatedAtUtc));
            if (rowVersion < 0) throw new ArgumentOutOfRangeException(nameof(rowVersion));
            Entries = entries?.OrderBy(entry => entry.Ordinal).ToArray() ??
                throw new ArgumentNullException(nameof(entries));
            Enabled = enabled;
            SortOrder = sortOrder;
            CreatedAtUtc = createdAtUtc;
            UpdatedAtUtc = updatedAtUtc;
            RowVersion = rowVersion;
        }

        public string PackageId { get; }
        public string Name { get; }
        public string Description { get; }
        public bool Enabled { get; }
        public int SortOrder { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset UpdatedAtUtc { get; }
        public long RowVersion { get; }
        public IReadOnlyList<RewardPackageEntrySnapshot> Entries { get; }
    }

    public sealed class GrantOperationEntryDraft
    {
        public GrantOperationEntryDraft(
            string operationEntryId,
            string packageEntryId,
            int ordinal,
            RewardEntryKind kind)
        {
            if (ordinal < 0) throw new ArgumentOutOfRangeException(nameof(ordinal));
            OperationEntryId = RewardValidation.RequireText(operationEntryId, nameof(operationEntryId));
            PackageEntryId = RewardValidation.RequireText(packageEntryId, nameof(packageEntryId));
            RewardValidation.RequireDefined(kind, nameof(kind));
            Ordinal = ordinal;
            Kind = kind;
        }

        public string OperationEntryId { get; }
        public string PackageEntryId { get; }
        public int Ordinal { get; }
        public RewardEntryKind Kind { get; }
    }

    public sealed class GrantOperationDraft
    {
        public GrantOperationDraft(
            string operationId,
            string packageId,
            string crossplatformId,
            int expectedEntityId,
            string expectedWorldId,
            string idempotencyKey,
            string? eligibilityKey,
            string? sourceKind,
            string? sourceId,
            string actorKind,
            string actorId,
            string? reservationId,
            string? compensatesOperationId,
            string? correlationId,
            DateTimeOffset createdAtUtc,
            IEnumerable<GrantOperationEntryDraft> entries)
        {
            if (expectedEntityId < 0) throw new ArgumentOutOfRangeException(nameof(expectedEntityId));
            OperationId = RewardValidation.RequireText(operationId, nameof(operationId));
            PackageId = RewardValidation.RequireText(packageId, nameof(packageId));
            CrossplatformId = RewardValidation.RequireText(crossplatformId, nameof(crossplatformId));
            ExpectedEntityId = expectedEntityId;
            ExpectedWorldId = RewardValidation.RequireText(expectedWorldId, nameof(expectedWorldId));
            IdempotencyKey = RewardValidation.RequireText(idempotencyKey, nameof(idempotencyKey));
            EligibilityKey = RewardValidation.OptionalText(eligibilityKey);
            SourceKind = RewardValidation.OptionalText(sourceKind);
            SourceId = RewardValidation.OptionalText(sourceId);
            if ((SourceKind == null) != (SourceId == null))
                throw new ArgumentException("Source kind and id must be supplied together.");
            if (EligibilityKey != null && SourceKind == null)
                throw new ArgumentException("An eligibility key requires a source.");
            ActorKind = RewardValidation.RequireText(actorKind, nameof(actorKind));
            ActorId = RewardValidation.RequireText(actorId, nameof(actorId));
            ReservationId = RewardValidation.OptionalText(reservationId);
            CompensatesOperationId = RewardValidation.OptionalText(compensatesOperationId);
            CorrelationId = RewardValidation.OptionalText(correlationId);
            RewardValidation.RequireUtc(createdAtUtc, nameof(createdAtUtc));
            CreatedAtUtc = createdAtUtc;
            Entries = entries?.OrderBy(entry => entry.Ordinal).ToArray() ??
                throw new ArgumentNullException(nameof(entries));
            if (Entries.Select(entry => entry.OperationEntryId).Distinct(StringComparer.Ordinal).Count() != Entries.Count)
                throw new ArgumentException("Operation entry identifiers must be unique.", nameof(entries));
        }

        public string OperationId { get; }
        public string PackageId { get; }
        public string CrossplatformId { get; }
        public int ExpectedEntityId { get; }
        public string ExpectedWorldId { get; }
        public string IdempotencyKey { get; }
        public string? EligibilityKey { get; }
        public string? SourceKind { get; }
        public string? SourceId { get; }
        public string ActorKind { get; }
        public string ActorId { get; }
        public string? ReservationId { get; }
        public string? CompensatesOperationId { get; }
        public string? CorrelationId { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public IReadOnlyList<GrantOperationEntryDraft> Entries { get; }
    }

    public sealed class GrantOperationEntrySnapshot
    {
        public GrantOperationEntrySnapshot(
            string operationEntryId,
            string packageEntryId,
            int ordinal,
            RewardEntryKind kind,
            GrantOperationState state,
            string? deliveryOperationId,
            string? ledgerTransactionId,
            string? errorCode,
            DateTimeOffset updatedAtUtc,
            long rowVersion)
        {
            if (ordinal < 0) throw new ArgumentOutOfRangeException(nameof(ordinal));
            if (rowVersion < 0) throw new ArgumentOutOfRangeException(nameof(rowVersion));
            OperationEntryId = RewardValidation.RequireText(operationEntryId, nameof(operationEntryId));
            PackageEntryId = RewardValidation.RequireText(packageEntryId, nameof(packageEntryId));
            RewardValidation.RequireDefined(kind, nameof(kind));
            RewardValidation.RequireDefined(state, nameof(state));
            RewardValidation.RequireUtc(updatedAtUtc, nameof(updatedAtUtc));
            Ordinal = ordinal;
            Kind = kind;
            State = state;
            DeliveryOperationId = RewardValidation.OptionalText(deliveryOperationId);
            LedgerTransactionId = RewardValidation.OptionalText(ledgerTransactionId);
            ErrorCode = RewardValidation.OptionalText(errorCode);
            UpdatedAtUtc = updatedAtUtc;
            RowVersion = rowVersion;
        }

        public string OperationEntryId { get; }
        public string PackageEntryId { get; }
        public int Ordinal { get; }
        public RewardEntryKind Kind { get; }
        public GrantOperationState State { get; }
        public string? DeliveryOperationId { get; }
        public string? LedgerTransactionId { get; }
        public string? ErrorCode { get; }
        public DateTimeOffset UpdatedAtUtc { get; }
        public long RowVersion { get; }
    }

    public sealed class GrantOperationSnapshot
    {
        public GrantOperationSnapshot(
            string operationId,
            string packageId,
            string crossplatformId,
            int expectedEntityId,
            string expectedWorldId,
            GrantOperationState state,
            string idempotencyKey,
            string? eligibilityKey,
            string? sourceKind,
            string? sourceId,
            string actorKind,
            string actorId,
            string? reservationId,
            string? compensatesOperationId,
            string? correlationId,
            string? errorCode,
            DateTimeOffset createdAtUtc,
            DateTimeOffset updatedAtUtc,
            DateTimeOffset? completedAtUtc,
            DateTimeOffset? reconciledAtUtc,
            string? reconciledBy,
            long rowVersion,
            IEnumerable<GrantOperationEntrySnapshot> entries)
        {
            OperationId = RewardValidation.RequireText(operationId, nameof(operationId));
            PackageId = RewardValidation.RequireText(packageId, nameof(packageId));
            CrossplatformId = RewardValidation.RequireText(crossplatformId, nameof(crossplatformId));
            if (expectedEntityId < 0) throw new ArgumentOutOfRangeException(nameof(expectedEntityId));
            ExpectedEntityId = expectedEntityId;
            ExpectedWorldId = RewardValidation.RequireText(expectedWorldId, nameof(expectedWorldId));
            RewardValidation.RequireDefined(state, nameof(state));
            State = state;
            IdempotencyKey = RewardValidation.RequireText(idempotencyKey, nameof(idempotencyKey));
            EligibilityKey = RewardValidation.OptionalText(eligibilityKey);
            SourceKind = RewardValidation.OptionalText(sourceKind);
            SourceId = RewardValidation.OptionalText(sourceId);
            ActorKind = RewardValidation.RequireText(actorKind, nameof(actorKind));
            ActorId = RewardValidation.RequireText(actorId, nameof(actorId));
            ReservationId = RewardValidation.OptionalText(reservationId);
            CompensatesOperationId = RewardValidation.OptionalText(compensatesOperationId);
            CorrelationId = RewardValidation.OptionalText(correlationId);
            ErrorCode = RewardValidation.OptionalText(errorCode);
            RewardValidation.RequireUtc(createdAtUtc, nameof(createdAtUtc));
            RewardValidation.RequireUtc(updatedAtUtc, nameof(updatedAtUtc));
            if (completedAtUtc.HasValue) RewardValidation.RequireUtc(completedAtUtc.Value, nameof(completedAtUtc));
            if (reconciledAtUtc.HasValue) RewardValidation.RequireUtc(reconciledAtUtc.Value, nameof(reconciledAtUtc));
            ReconciledBy = RewardValidation.OptionalText(reconciledBy);
            if (rowVersion < 0) throw new ArgumentOutOfRangeException(nameof(rowVersion));
            CreatedAtUtc = createdAtUtc;
            UpdatedAtUtc = updatedAtUtc;
            CompletedAtUtc = completedAtUtc;
            ReconciledAtUtc = reconciledAtUtc;
            RowVersion = rowVersion;
            Entries = entries?.OrderBy(entry => entry.Ordinal).ToArray() ??
                throw new ArgumentNullException(nameof(entries));
        }

        public string OperationId { get; }
        public string PackageId { get; }
        public string CrossplatformId { get; }
        public int ExpectedEntityId { get; }
        public string ExpectedWorldId { get; }
        public GrantOperationState State { get; }
        public string IdempotencyKey { get; }
        public string? EligibilityKey { get; }
        public string? SourceKind { get; }
        public string? SourceId { get; }
        public string ActorKind { get; }
        public string ActorId { get; }
        public string? ReservationId { get; }
        public string? CompensatesOperationId { get; }
        public string? CorrelationId { get; }
        public string? ErrorCode { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset UpdatedAtUtc { get; }
        public DateTimeOffset? CompletedAtUtc { get; }
        public DateTimeOffset? ReconciledAtUtc { get; }
        public string? ReconciledBy { get; }
        public long RowVersion { get; }
        public IReadOnlyList<GrantOperationEntrySnapshot> Entries { get; }
    }

    public sealed class GrantCreationResult
    {
        public GrantCreationResult(GrantOperationSnapshot operation, bool created)
        {
            Operation = operation ?? throw new ArgumentNullException(nameof(operation));
            Created = created;
        }

        public GrantOperationSnapshot Operation { get; }
        public bool Created { get; }
    }

    public sealed class GrantEntryResolution
    {
        public GrantEntryResolution(
            string operationEntryId,
            GrantOperationState state,
            string? deliveryOperationId,
            string? ledgerTransactionId,
            string? errorCode)
        {
            OperationEntryId = RewardValidation.RequireText(operationEntryId, nameof(operationEntryId));
            RewardValidation.RequireDefined(state, nameof(state));
            State = state;
            DeliveryOperationId = RewardValidation.OptionalText(deliveryOperationId);
            LedgerTransactionId = RewardValidation.OptionalText(ledgerTransactionId);
            ErrorCode = RewardValidation.OptionalText(errorCode);
        }

        public string OperationEntryId { get; }
        public GrantOperationState State { get; }
        public string? DeliveryOperationId { get; }
        public string? LedgerTransactionId { get; }
        public string? ErrorCode { get; }
    }

    public sealed class GrantDispatchResolution
    {
        public GrantDispatchResolution(
            string operationId,
            long expectedRowVersion,
            GrantOperationState state,
            IEnumerable<GrantEntryResolution> entries,
            string? errorCode,
            DateTimeOffset occurredAtUtc)
        {
            if (expectedRowVersion < 0) throw new ArgumentOutOfRangeException(nameof(expectedRowVersion));
            OperationId = RewardValidation.RequireText(operationId, nameof(operationId));
            RewardValidation.RequireDefined(state, nameof(state));
            if (state != GrantOperationState.Completed &&
                state != GrantOperationState.Failed &&
                state != GrantOperationState.PendingReconciliation)
            {
                throw new ArgumentException("A dispatch resolution requires a dispatch terminal or reconciliation state.");
            }
            Entries = entries?.ToArray() ?? throw new ArgumentNullException(nameof(entries));
            if (Entries.Select(entry => entry.OperationEntryId).Distinct(StringComparer.Ordinal).Count() != Entries.Count)
                throw new ArgumentException("Entry resolutions must be unique.", nameof(entries));
            ErrorCode = RewardValidation.OptionalText(errorCode);
            RewardValidation.RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
            ExpectedRowVersion = expectedRowVersion;
            State = state;
            OccurredAtUtc = occurredAtUtc;
        }

        public string OperationId { get; }
        public long ExpectedRowVersion { get; }
        public GrantOperationState State { get; }
        public IReadOnlyList<GrantEntryResolution> Entries { get; }
        public string? ErrorCode { get; }
        public DateTimeOffset OccurredAtUtc { get; }
    }

    public sealed class ResolvedRewardEntry
    {
        internal ResolvedRewardEntry(
            string operationEntryId,
            string packageEntryId,
            int ordinal,
            RewardEntryKind kind,
            string? resourceId,
            string? itemInternalName,
            GameResourceKind? itemKind,
            int? quantity,
            int? quality,
            string? catalogVersion,
            bool hiddenItemConfirmed,
            long? currencyAmount,
            string? registeredAction)
        {
            OperationEntryId = RewardValidation.RequireText(operationEntryId, nameof(operationEntryId));
            PackageEntryId = RewardValidation.RequireText(packageEntryId, nameof(packageEntryId));
            if (ordinal < 0) throw new ArgumentOutOfRangeException(nameof(ordinal));
            RewardValidation.RequireDefined(kind, nameof(kind));
            Ordinal = ordinal;
            Kind = kind;
            ResourceId = RewardValidation.OptionalText(resourceId);
            ItemInternalName = RewardValidation.OptionalText(itemInternalName);
            ItemKind = itemKind;
            Quantity = quantity;
            Quality = quality;
            CatalogVersion = RewardValidation.OptionalText(catalogVersion);
            HiddenItemConfirmed = hiddenItemConfirmed;
            CurrencyAmount = currencyAmount;
            RegisteredAction = RewardValidation.OptionalText(registeredAction);
        }

        public string OperationEntryId { get; }
        public string PackageEntryId { get; }
        public int Ordinal { get; }
        public RewardEntryKind Kind { get; }
        public string? ResourceId { get; }
        public string? ItemInternalName { get; }
        public GameResourceKind? ItemKind { get; }
        public int? Quantity { get; }
        public int? Quality { get; }
        public string? CatalogVersion { get; }
        public bool HiddenItemConfirmed { get; }
        public long? CurrencyAmount { get; }
        public string? RegisteredAction { get; }
    }

    public sealed class RewardDeliveryCommand
    {
        public RewardDeliveryCommand(
            string grantOperationId,
            string crossplatformId,
            int expectedEntityId,
            string expectedWorldId,
            IEnumerable<ResolvedRewardEntry> entries)
        {
            if (expectedEntityId < 0) throw new ArgumentOutOfRangeException(nameof(expectedEntityId));
            GrantOperationId = RewardValidation.RequireText(grantOperationId, nameof(grantOperationId));
            CrossplatformId = RewardValidation.RequireText(crossplatformId, nameof(crossplatformId));
            ExpectedEntityId = expectedEntityId;
            ExpectedWorldId = RewardValidation.RequireText(expectedWorldId, nameof(expectedWorldId));
            Entries = entries?.OrderBy(entry => entry.Ordinal).ToArray() ??
                throw new ArgumentNullException(nameof(entries));
        }

        public string GrantOperationId { get; }
        public string CrossplatformId { get; }
        public int ExpectedEntityId { get; }
        public string ExpectedWorldId { get; }
        public IReadOnlyList<ResolvedRewardEntry> Entries { get; }
    }

    public sealed class RewardDeliveryEntryResult
    {
        private RewardDeliveryEntryResult(
            string operationEntryId,
            RewardDeliveryStatus status,
            string? deliveryOperationId,
            string? errorCode)
        {
            OperationEntryId = RewardValidation.RequireText(operationEntryId, nameof(operationEntryId));
            RewardValidation.RequireDefined(status, nameof(status));
            Status = status;
            DeliveryOperationId = RewardValidation.OptionalText(deliveryOperationId);
            ErrorCode = RewardValidation.OptionalText(errorCode);
        }

        public string OperationEntryId { get; }
        public RewardDeliveryStatus Status { get; }
        public string? DeliveryOperationId { get; }
        public string? ErrorCode { get; }

        public static RewardDeliveryEntryResult Succeeded(string entryId, string operationId) =>
            new RewardDeliveryEntryResult(entryId, RewardDeliveryStatus.Succeeded, operationId, null);

        public static RewardDeliveryEntryResult Failed(
            string entryId,
            string? operationId,
            string errorCode) => new RewardDeliveryEntryResult(
                entryId,
                RewardDeliveryStatus.Failed,
                operationId,
                RewardValidation.RequireText(errorCode, nameof(errorCode)));

        public static RewardDeliveryEntryResult ResultUnknown(
            string entryId,
            string? operationId,
            string errorCode) => new RewardDeliveryEntryResult(
                entryId,
                RewardDeliveryStatus.ResultUnknown,
                operationId,
                RewardValidation.RequireText(errorCode, nameof(errorCode)));
    }

    public sealed class RewardDeliveryResult
    {
        private RewardDeliveryResult(
            RewardDeliveryStatus status,
            IEnumerable<RewardDeliveryEntryResult> entries,
            string? errorCode)
        {
            RewardValidation.RequireDefined(status, nameof(status));
            Status = status;
            Entries = entries?.ToArray() ?? throw new ArgumentNullException(nameof(entries));
            ErrorCode = RewardValidation.OptionalText(errorCode);
        }

        public RewardDeliveryStatus Status { get; }
        public IReadOnlyList<RewardDeliveryEntryResult> Entries { get; }
        public string? ErrorCode { get; }

        public static RewardDeliveryResult Succeeded(IEnumerable<RewardDeliveryEntryResult> entries) =>
            new RewardDeliveryResult(RewardDeliveryStatus.Succeeded, entries, null);

        public static RewardDeliveryResult Failed(
            IEnumerable<RewardDeliveryEntryResult> entries,
            string errorCode) => new RewardDeliveryResult(
                RewardDeliveryStatus.Failed,
                entries,
                RewardValidation.RequireText(errorCode, nameof(errorCode)));

        public static RewardDeliveryResult ResultUnknown(
            IEnumerable<RewardDeliveryEntryResult> entries,
            string? errorCode = "ResultUnknown") => new RewardDeliveryResult(
                RewardDeliveryStatus.ResultUnknown,
                entries,
                errorCode);
    }

    public sealed class GrantRewardCommand
    {
        public GrantRewardCommand(
            string packageId,
            string crossplatformId,
            int expectedEntityId,
            string expectedWorldId,
            string idempotencyKey,
            string? eligibilityKey,
            string? sourceKind,
            string? sourceId,
            string actorKind,
            string actorId,
            string? correlationId,
            string? reservationId = null,
            string? compensatesOperationId = null)
        {
            if (expectedEntityId < 0) throw new ArgumentOutOfRangeException(nameof(expectedEntityId));
            PackageId = RewardValidation.RequireText(packageId, nameof(packageId));
            CrossplatformId = RewardValidation.RequireText(crossplatformId, nameof(crossplatformId));
            ExpectedEntityId = expectedEntityId;
            ExpectedWorldId = RewardValidation.RequireText(expectedWorldId, nameof(expectedWorldId));
            IdempotencyKey = RewardValidation.RequireText(idempotencyKey, nameof(idempotencyKey));
            EligibilityKey = RewardValidation.OptionalText(eligibilityKey);
            SourceKind = RewardValidation.OptionalText(sourceKind);
            SourceId = RewardValidation.OptionalText(sourceId);
            if ((SourceKind == null) != (SourceId == null))
                throw new ArgumentException("Source kind and id must be supplied together.");
            if (EligibilityKey != null && SourceKind == null)
                throw new ArgumentException("An eligibility key requires a source.");
            ActorKind = RewardValidation.RequireText(actorKind, nameof(actorKind));
            ActorId = RewardValidation.RequireText(actorId, nameof(actorId));
            CorrelationId = RewardValidation.OptionalText(correlationId);
            ReservationId = RewardValidation.OptionalText(reservationId);
            CompensatesOperationId = RewardValidation.OptionalText(compensatesOperationId);
        }

        public string PackageId { get; }
        public string CrossplatformId { get; }
        public int ExpectedEntityId { get; }
        public string ExpectedWorldId { get; }
        public string IdempotencyKey { get; }
        public string? EligibilityKey { get; }
        public string? SourceKind { get; }
        public string? SourceId { get; }
        public string ActorKind { get; }
        public string ActorId { get; }
        public string? CorrelationId { get; }
        public string? ReservationId { get; }
        public string? CompensatesOperationId { get; }
    }

    public sealed class GrantRewardResult
    {
        public GrantRewardResult(GrantOperationSnapshot operation, bool reused)
        {
            Operation = operation ?? throw new ArgumentNullException(nameof(operation));
            Reused = reused;
        }

        public GrantOperationSnapshot Operation { get; }
        public bool Reused { get; }
    }

    public sealed class ConfirmRewardGrantCommand
    {
        public ConfirmRewardGrantCommand(
            string operationId,
            string actorId,
            string correlationId,
            DateTimeOffset occurredAtUtc)
        {
            OperationId = RewardValidation.RequireText(operationId, nameof(operationId));
            ActorId = RewardValidation.RequireText(actorId, nameof(actorId));
            CorrelationId = RewardValidation.RequireText(correlationId, nameof(correlationId));
            RewardValidation.RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
            OccurredAtUtc = occurredAtUtc;
        }

        public string OperationId { get; }
        public string ActorId { get; }
        public string CorrelationId { get; }
        public DateTimeOffset OccurredAtUtc { get; }
    }

    public sealed class RefundRewardGrantCommand
    {
        public RefundRewardGrantCommand(
            string operationId,
            string idempotencyKey,
            string actorKind,
            string actorId,
            string correlationId,
            DateTimeOffset occurredAtUtc)
        {
            OperationId = RewardValidation.RequireText(operationId, nameof(operationId));
            IdempotencyKey = RewardValidation.RequireText(idempotencyKey, nameof(idempotencyKey));
            ActorKind = RewardValidation.RequireText(actorKind, nameof(actorKind));
            ActorId = RewardValidation.RequireText(actorId, nameof(actorId));
            CorrelationId = RewardValidation.RequireText(correlationId, nameof(correlationId));
            RewardValidation.RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
            OccurredAtUtc = occurredAtUtc;
        }

        public string OperationId { get; }
        public string IdempotencyKey { get; }
        public string ActorKind { get; }
        public string ActorId { get; }
        public string CorrelationId { get; }
        public DateTimeOffset OccurredAtUtc { get; }
    }

    public sealed class CompensateRewardGrantCommand
    {
        public CompensateRewardGrantCommand(
            string operationId,
            string idempotencyKey,
            string actorKind,
            string actorId,
            string correlationId)
        {
            OperationId = RewardValidation.RequireText(operationId, nameof(operationId));
            IdempotencyKey = RewardValidation.RequireText(idempotencyKey, nameof(idempotencyKey));
            ActorKind = RewardValidation.RequireText(actorKind, nameof(actorKind));
            ActorId = RewardValidation.RequireText(actorId, nameof(actorId));
            CorrelationId = RewardValidation.RequireText(correlationId, nameof(correlationId));
        }

        public string OperationId { get; }
        public string IdempotencyKey { get; }
        public string ActorKind { get; }
        public string ActorId { get; }
        public string CorrelationId { get; }
    }

    public class RewardException : InvalidOperationException
    {
        protected RewardException(string code) : base(code) { }
    }

    public sealed class RewardPackageNotFoundException : RewardException
    {
        public RewardPackageNotFoundException() : base("reward_package_not_found") { }
    }

    public sealed class RewardGrantNotFoundException : RewardException
    {
        public RewardGrantNotFoundException() : base("reward_grant_not_found") { }
    }

    public sealed class RewardIdempotencyConflictException : RewardException
    {
        public RewardIdempotencyConflictException() : base("reward_idempotency_conflict") { }
    }

    public sealed class RewardCatalogValidationException : RewardException
    {
        public RewardCatalogValidationException(string code) : base(code) { }
    }

    public sealed class RewardConcurrencyException : RewardException
    {
        public RewardConcurrencyException() : base("reward_concurrency_conflict") { }
    }

    internal static class RewardValidation
    {
        internal static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty value is required.", parameterName);
            return value.Trim();
        }

        internal static string? OptionalText(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

        internal static void RequireUtc(DateTimeOffset value, string parameterName)
        {
            if (value.Offset != TimeSpan.Zero)
                throw new ArgumentException("A UTC timestamp is required.", parameterName);
        }

        internal static void RequireDefined<T>(T value, string parameterName) where T : struct, Enum
        {
            if (!Enum.IsDefined(typeof(T), value))
                throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
