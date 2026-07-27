using System;
using System.Collections.Generic;
using System.Linq;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Application.Rewards;
using LSTY.SevenDPanel.Domain.Rewards;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    public sealed class RewardPackageEntryHttpRequest
    {
        public string? EntryId { get; set; }
        public RewardEntryKind Kind { get; set; }
        public string? ItemInternalName { get; set; }
        public GameResourceKind? ItemKind { get; set; }
        public int? Quantity { get; set; }
        public int? MinQuality { get; set; }
        public int? MaxQuality { get; set; }
        public string? CatalogVersion { get; set; }
        public long? CurrencyAmount { get; set; }
        public string? RegisteredAction { get; set; }

        internal RewardPackageEntryDraft ToDraft()
        {
            var entryId = CommerceRewardHttpSupport.RequireText(EntryId);
            switch (Kind)
            {
                case RewardEntryKind.Item:
                    if (!ItemKind.HasValue || !Quantity.HasValue)
                        throw new ArgumentException("Typed item values are required.");
                    return RewardPackageEntryDraft.Item(
                        entryId,
                        CommerceRewardHttpSupport.RequireText(ItemInternalName),
                        ItemKind.Value,
                        Quantity.Value,
                        MinQuality,
                        MaxQuality,
                        CommerceRewardHttpSupport.RequireText(CatalogVersion));
                case RewardEntryKind.Currency:
                    if (!CurrencyAmount.HasValue)
                        throw new ArgumentException("A currency amount is required.");
                    return RewardPackageEntryDraft.Currency(entryId, CurrencyAmount.Value);
                case RewardEntryKind.RegisteredAction:
                    return RewardPackageEntryDraft.RegisteredActionEntry(
                        entryId,
                        CommerceRewardHttpSupport.RequireText(RegisteredAction));
                default:
                    throw new ArgumentOutOfRangeException(nameof(Kind));
            }
        }
    }

    public sealed class RewardPackageUpsertHttpRequest
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public bool Enabled { get; set; }
        public int SortOrder { get; set; }
        public IReadOnlyList<RewardPackageEntryHttpRequest>? Entries { get; set; }
    }

    public sealed class DailyRewardPolicyUpsertHttpRequest
    {
        public string? RewardPackageId { get; set; }
        public bool Enabled { get; set; }
        public long? ExpectedRowVersion { get; set; }
    }

    public sealed class DailyRewardPolicyHttpResponse
    {
        public DailyRewardPolicyHttpResponse(DailyRewardPolicySnapshot policy)
        {
            RuleId = policy.RuleId;
            RewardPackageId = policy.RewardPackageId;
            Enabled = policy.Enabled;
            CreatedAtUtc = policy.CreatedAtUtc;
            UpdatedAtUtc = policy.UpdatedAtUtc;
            RowVersion = policy.RowVersion;
        }

        public string RuleId { get; }
        public string RewardPackageId { get; }
        public bool Enabled { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset UpdatedAtUtc { get; }
        public long RowVersion { get; }
    }

    public sealed class RewardPackageEntryHttpResponse
    {
        public RewardPackageEntryHttpResponse(RewardPackageEntrySnapshot entry)
        {
            EntryId = entry.EntryId;
            Ordinal = entry.Ordinal;
            Kind = entry.Kind;
            ItemInternalName = entry.ItemInternalName;
            ItemKind = entry.ItemKind;
            Quantity = entry.Quantity;
            MinQuality = entry.MinQuality;
            MaxQuality = entry.MaxQuality;
            CatalogVersion = entry.CatalogVersion;
            CurrencyAmount = entry.CurrencyAmount;
            RegisteredAction = entry.RegisteredAction;
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

    public sealed class RewardPackageHttpResponse
    {
        public RewardPackageHttpResponse(RewardPackageSnapshot package)
        {
            PackageId = package.PackageId;
            Name = package.Name;
            Description = package.Description;
            Enabled = package.Enabled;
            SortOrder = package.SortOrder;
            CreatedAtUtc = package.CreatedAtUtc;
            UpdatedAtUtc = package.UpdatedAtUtc;
            RowVersion = package.RowVersion;
            Entries = package.Entries.Select(entry => new RewardPackageEntryHttpResponse(entry)).ToArray();
        }

        public string PackageId { get; }
        public string Name { get; }
        public string Description { get; }
        public bool Enabled { get; }
        public int SortOrder { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset UpdatedAtUtc { get; }
        public long RowVersion { get; }
        public IReadOnlyList<RewardPackageEntryHttpResponse> Entries { get; }
    }

    public sealed class GrantRewardHttpRequest
    {
        public string? PackageId { get; set; }
        public string? CrossplatformId { get; set; }
        public int ExpectedEntityId { get; set; }
        public string? ExpectedWorldId { get; set; }
        public string? ClientRequestKey { get; set; }
    }

    public sealed class RefundRewardGrantHttpRequest
    {
        public string? ClientRequestKey { get; set; }
    }

    public sealed class CompensateRewardGrantHttpRequest
    {
        public string? ClientRequestKey { get; set; }
    }

    public sealed class GrantOperationEntryHttpResponse
    {
        public GrantOperationEntryHttpResponse(GrantOperationEntrySnapshot entry)
        {
            OperationEntryId = entry.OperationEntryId;
            PackageEntryId = entry.PackageEntryId;
            Ordinal = entry.Ordinal;
            Kind = entry.Kind;
            State = entry.State;
            DeliveryOperationId = entry.DeliveryOperationId;
            LedgerTransactionId = entry.LedgerTransactionId;
            ErrorCode = entry.ErrorCode;
            UpdatedAtUtc = entry.UpdatedAtUtc;
            RowVersion = entry.RowVersion;
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

    public sealed class GrantOperationHttpResponse
    {
        public GrantOperationHttpResponse(GrantOperationSnapshot operation, bool? reused = null)
        {
            OperationId = operation.OperationId;
            PackageId = operation.PackageId;
            CrossplatformId = operation.CrossplatformId;
            ExpectedEntityId = operation.ExpectedEntityId;
            ExpectedWorldId = operation.ExpectedWorldId;
            State = operation.State;
            SourceKind = operation.SourceKind;
            SourceId = operation.SourceId;
            ActorKind = operation.ActorKind;
            ActorId = operation.ActorId;
            ReservationId = operation.ReservationId;
            CompensatesOperationId = operation.CompensatesOperationId;
            CorrelationId = operation.CorrelationId;
            ErrorCode = operation.ErrorCode;
            CreatedAtUtc = operation.CreatedAtUtc;
            UpdatedAtUtc = operation.UpdatedAtUtc;
            CompletedAtUtc = operation.CompletedAtUtc;
            ReconciledAtUtc = operation.ReconciledAtUtc;
            ReconciledBy = operation.ReconciledBy;
            RowVersion = operation.RowVersion;
            Reused = reused;
            Entries = operation.Entries.Select(entry => new GrantOperationEntryHttpResponse(entry)).ToArray();
        }

        public string OperationId { get; }
        public string PackageId { get; }
        public string CrossplatformId { get; }
        public int ExpectedEntityId { get; }
        public string ExpectedWorldId { get; }
        public GrantOperationState State { get; }
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
        public bool? Reused { get; }
        public IReadOnlyList<GrantOperationEntryHttpResponse> Entries { get; }
    }

    public sealed class GrantOperationsHttpResponse
    {
        public GrantOperationsHttpResponse(IEnumerable<GrantOperationSnapshot> operations) =>
            Operations = operations.Select(operation => new GrantOperationHttpResponse(operation)).ToArray();

        public IReadOnlyList<GrantOperationHttpResponse> Operations { get; }
    }
}
