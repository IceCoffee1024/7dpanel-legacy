using System;
using System.Collections.Generic;
using System.Linq;
using LSTY.SevenDPanel.Domain.Economy;

namespace LSTY.SevenDPanel.Application.Economy
{
    public enum EconomyAccountKind
    {
        Player,
        System
    }

    public enum FundsReservationStatus
    {
        Reserved,
        InsufficientFunds,
        AccountFrozen,
        AccountDisabled,
        Released,
        Captured
    }

    public static class EconomyActorKinds
    {
        public const string Player = "Player";
        public const string Owner = "Owner";
        public const string System = "System";
    }

    public static class SystemAccountIds
    {
        public const string Issuance = "system:issuance";
        public const string Recovery = "system:recovery";
        public const string Shop = "system:shop";
        public const string Rewards = "system:rewards";

        public static bool IsApproved(string accountId) =>
            string.Equals(accountId, Issuance, StringComparison.Ordinal) ||
            string.Equals(accountId, Recovery, StringComparison.Ordinal) ||
            string.Equals(accountId, Shop, StringComparison.Ordinal) ||
            string.Equals(accountId, Rewards, StringComparison.Ordinal);
    }

    public sealed class AccountSnapshot
    {
        public AccountSnapshot(
            string accountId,
            EconomyAccountKind kind,
            string? crossplatformId,
            bool enabled,
            bool isFrozen,
            long postedBalance,
            long reservedDebit,
            DateTimeOffset createdAtUtc,
            DateTimeOffset updatedAtUtc,
            long rowVersion)
        {
            AccountId = EconomyModelValidation.RequireText(accountId, nameof(accountId));
            EconomyModelValidation.RequireDefined(kind, nameof(kind));
            CrossplatformId = EconomyModelValidation.Normalize(crossplatformId);
            if (kind == EconomyAccountKind.Player && CrossplatformId == null)
                throw new ArgumentException("A player account requires a cross-platform id.", nameof(crossplatformId));
            if (kind == EconomyAccountKind.System && CrossplatformId != null)
                throw new ArgumentException("A system account cannot have a cross-platform id.", nameof(crossplatformId));
            if (reservedDebit < 0) throw new ArgumentOutOfRangeException(nameof(reservedDebit));
            if (kind == EconomyAccountKind.Player && postedBalance < reservedDebit)
                throw new ArgumentOutOfRangeException(nameof(postedBalance));
            EconomyModelValidation.RequireUtc(createdAtUtc, nameof(createdAtUtc));
            EconomyModelValidation.RequireUtc(updatedAtUtc, nameof(updatedAtUtc));
            if (updatedAtUtc < createdAtUtc) throw new ArgumentOutOfRangeException(nameof(updatedAtUtc));
            if (rowVersion < 0) throw new ArgumentOutOfRangeException(nameof(rowVersion));

            Kind = kind;
            Enabled = enabled;
            IsFrozen = isFrozen;
            PostedBalance = postedBalance;
            ReservedDebit = reservedDebit;
            CreatedAtUtc = createdAtUtc;
            UpdatedAtUtc = updatedAtUtc;
            RowVersion = rowVersion;
        }

        public string AccountId { get; }
        public EconomyAccountKind Kind { get; }
        public string? CrossplatformId { get; }
        public bool Enabled { get; }
        public bool IsFrozen { get; }
        public long PostedBalance { get; }
        public long ReservedDebit { get; }
        public long AvailableBalance => checked(PostedBalance - ReservedDebit);
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset UpdatedAtUtc { get; }
        public long RowVersion { get; }
    }

    public sealed class LedgerEntryDraft
    {
        public LedgerEntryDraft(string accountId, LedgerSide side, long amount)
        {
            AccountId = EconomyModelValidation.RequireText(accountId, nameof(accountId));
            EconomyModelValidation.RequireDefined(side, nameof(side));
            LedgerRules.ValidateAmount(amount);
            Side = side;
            Amount = amount;
        }

        public string AccountId { get; }
        public LedgerSide Side { get; }
        public long Amount { get; }
    }

    public sealed class LedgerEntrySnapshot
    {
        public LedgerEntrySnapshot(
            string entryId,
            string accountId,
            LedgerSide side,
            long amount,
            long balanceAfter)
        {
            EntryId = EconomyModelValidation.RequireText(entryId, nameof(entryId));
            AccountId = EconomyModelValidation.RequireText(accountId, nameof(accountId));
            EconomyModelValidation.RequireDefined(side, nameof(side));
            LedgerRules.ValidateAmount(amount);
            Side = side;
            Amount = amount;
            BalanceAfter = balanceAfter;
        }

        public string EntryId { get; }
        public string AccountId { get; }
        public LedgerSide Side { get; }
        public long Amount { get; }
        public long BalanceAfter { get; }
    }

    public sealed class LedgerTransactionDraft
    {
        public LedgerTransactionDraft(
            string transactionId,
            string type,
            string idempotencyKey,
            DateTimeOffset occurredAtUtc,
            string actorKind,
            string actorId,
            string? relatedCrossplatformId,
            string? businessKind,
            string? businessId,
            string? correlationId,
            string? reason,
            IEnumerable<LedgerEntryDraft> entries)
        {
            TransactionId = EconomyModelValidation.RequireText(transactionId, nameof(transactionId));
            Type = EconomyModelValidation.RequireText(type, nameof(type));
            IdempotencyKey = EconomyModelValidation.RequireText(idempotencyKey, nameof(idempotencyKey));
            EconomyModelValidation.RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
            ActorKind = EconomyModelValidation.RequireText(actorKind, nameof(actorKind));
            ActorId = EconomyModelValidation.RequireText(actorId, nameof(actorId));
            RelatedCrossplatformId = EconomyModelValidation.Normalize(relatedCrossplatformId);
            BusinessKind = EconomyModelValidation.Normalize(businessKind);
            BusinessId = EconomyModelValidation.Normalize(businessId);
            if ((BusinessKind == null) != (BusinessId == null))
                throw new ArgumentException("Business kind and id must be supplied together.");
            CorrelationId = EconomyModelValidation.Normalize(correlationId);
            Reason = EconomyModelValidation.Normalize(reason);
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            Entries = entries.ToArray();
            if (Entries.Count == 0)
                throw new ArgumentException("A ledger transaction requires entries.", nameof(entries));
            if (Entries.Any(entry => entry == null))
                throw new ArgumentException("Ledger entries cannot contain null.", nameof(entries));
            OccurredAtUtc = occurredAtUtc;
        }

        public string TransactionId { get; }
        public string Type { get; }
        public string IdempotencyKey { get; }
        public DateTimeOffset OccurredAtUtc { get; }
        public string ActorKind { get; }
        public string ActorId { get; }
        public string? RelatedCrossplatformId { get; }
        public string? BusinessKind { get; }
        public string? BusinessId { get; }
        public string? CorrelationId { get; }
        public string? Reason { get; }
        public IReadOnlyList<LedgerEntryDraft> Entries { get; }
    }

    public sealed class LedgerTransactionSnapshot
    {
        public LedgerTransactionSnapshot(
            string transactionId,
            string type,
            string idempotencyKey,
            DateTimeOffset occurredAtUtc,
            string actorKind,
            string actorId,
            string? relatedCrossplatformId,
            string? businessKind,
            string? businessId,
            string? correlationId,
            string? reason,
            string status,
            IEnumerable<LedgerEntrySnapshot> entries)
        {
            TransactionId = EconomyModelValidation.RequireText(transactionId, nameof(transactionId));
            Type = EconomyModelValidation.RequireText(type, nameof(type));
            IdempotencyKey = EconomyModelValidation.RequireText(idempotencyKey, nameof(idempotencyKey));
            EconomyModelValidation.RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
            ActorKind = EconomyModelValidation.RequireText(actorKind, nameof(actorKind));
            ActorId = EconomyModelValidation.RequireText(actorId, nameof(actorId));
            RelatedCrossplatformId = EconomyModelValidation.Normalize(relatedCrossplatformId);
            BusinessKind = EconomyModelValidation.Normalize(businessKind);
            BusinessId = EconomyModelValidation.Normalize(businessId);
            CorrelationId = EconomyModelValidation.Normalize(correlationId);
            Reason = EconomyModelValidation.Normalize(reason);
            Status = EconomyModelValidation.RequireText(status, nameof(status));
            Entries = entries?.ToArray() ?? throw new ArgumentNullException(nameof(entries));
            OccurredAtUtc = occurredAtUtc;
        }

        public string TransactionId { get; }
        public string Type { get; }
        public string IdempotencyKey { get; }
        public DateTimeOffset OccurredAtUtc { get; }
        public string ActorKind { get; }
        public string ActorId { get; }
        public string? RelatedCrossplatformId { get; }
        public string? BusinessKind { get; }
        public string? BusinessId { get; }
        public string? CorrelationId { get; }
        public string? Reason { get; }
        public string Status { get; }
        public IReadOnlyList<LedgerEntrySnapshot> Entries { get; }
    }

    public sealed class LedgerWriteResult
    {
        public LedgerWriteResult(
            LedgerTransactionSnapshot transaction,
            IEnumerable<AccountSnapshot> accounts)
        {
            Transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
            Accounts = accounts?.ToArray() ?? throw new ArgumentNullException(nameof(accounts));
        }

        public LedgerTransactionSnapshot Transaction { get; }
        public IReadOnlyList<AccountSnapshot> Accounts { get; }
    }

    public sealed class FundsReservationDraft
    {
        public FundsReservationDraft(
            string reservationId,
            string accountId,
            long amount,
            string idempotencyKey,
            string businessKind,
            string businessId,
            DateTimeOffset occurredAtUtc,
            DateTimeOffset? expiresAtUtc)
        {
            ReservationId = EconomyModelValidation.RequireText(reservationId, nameof(reservationId));
            AccountId = EconomyModelValidation.RequireText(accountId, nameof(accountId));
            LedgerRules.ValidateAmount(amount);
            IdempotencyKey = EconomyModelValidation.RequireText(idempotencyKey, nameof(idempotencyKey));
            BusinessKind = EconomyModelValidation.RequireText(businessKind, nameof(businessKind));
            BusinessId = EconomyModelValidation.RequireText(businessId, nameof(businessId));
            EconomyModelValidation.RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
            if (expiresAtUtc.HasValue)
            {
                EconomyModelValidation.RequireUtc(expiresAtUtc.Value, nameof(expiresAtUtc));
                if (expiresAtUtc.Value <= occurredAtUtc)
                    throw new ArgumentOutOfRangeException(nameof(expiresAtUtc));
            }

            Amount = amount;
            OccurredAtUtc = occurredAtUtc;
            ExpiresAtUtc = expiresAtUtc;
        }

        public string ReservationId { get; }
        public string AccountId { get; }
        public long Amount { get; }
        public string IdempotencyKey { get; }
        public string BusinessKind { get; }
        public string BusinessId { get; }
        public DateTimeOffset OccurredAtUtc { get; }
        public DateTimeOffset? ExpiresAtUtc { get; }
    }

    public sealed class FundsReservationResult
    {
        public FundsReservationResult(
            string reservationId,
            FundsReservationStatus status,
            long amount,
            AccountSnapshot account,
            string? capturedTransactionId)
        {
            ReservationId = EconomyModelValidation.RequireText(reservationId, nameof(reservationId));
            EconomyModelValidation.RequireDefined(status, nameof(status));
            LedgerRules.ValidateAmount(amount);
            Account = account ?? throw new ArgumentNullException(nameof(account));
            CapturedTransactionId = EconomyModelValidation.Normalize(capturedTransactionId);
            Status = status;
            Amount = amount;
        }

        public string ReservationId { get; }
        public FundsReservationStatus Status { get; }
        public long Amount { get; }
        public AccountSnapshot Account { get; }
        public string? CapturedTransactionId { get; }
    }

    public sealed class AccountKeyset
    {
        public AccountKeyset(long postedBalance, string accountId)
        {
            PostedBalance = postedBalance;
            AccountId = EconomyModelValidation.RequireText(accountId, nameof(accountId));
        }

        public long PostedBalance { get; }
        public string AccountId { get; }
    }

    public sealed class AccountKeysetQuery
    {
        public const int MaximumPageSize = 200;

        public AccountKeysetQuery(
            int pageSize,
            bool includeSystem = true,
            string? search = null,
            bool? enabled = null,
            bool? frozen = null,
            AccountKeyset? keyset = null)
        {
            if (pageSize < 1 || pageSize > MaximumPageSize)
                throw new ArgumentOutOfRangeException(nameof(pageSize));
            PageSize = pageSize;
            IncludeSystem = includeSystem;
            Search = EconomyModelValidation.Normalize(search);
            Enabled = enabled;
            Frozen = frozen;
            Keyset = keyset;
        }

        public int PageSize { get; }
        public bool IncludeSystem { get; }
        public string? Search { get; }
        public bool? Enabled { get; }
        public bool? Frozen { get; }
        public AccountKeyset? Keyset { get; }
    }

    public sealed class AccountPage
    {
        public AccountPage(IEnumerable<AccountSnapshot> accounts, AccountKeyset? nextKeyset)
        {
            Accounts = accounts?.ToArray() ?? throw new ArgumentNullException(nameof(accounts));
            NextKeyset = nextKeyset;
        }

        public IReadOnlyList<AccountSnapshot> Accounts { get; }
        public AccountKeyset? NextKeyset { get; }
    }

    public sealed class TransactionKeyset
    {
        public TransactionKeyset(DateTimeOffset occurredAtUtc, string transactionId)
        {
            EconomyModelValidation.RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
            OccurredAtUtc = occurredAtUtc;
            TransactionId = EconomyModelValidation.RequireText(transactionId, nameof(transactionId));
        }

        public DateTimeOffset OccurredAtUtc { get; }
        public string TransactionId { get; }
    }

    public sealed class TransactionKeysetQuery
    {
        public const int MaximumPageSize = 200;

        public TransactionKeysetQuery(
            int pageSize,
            string? relatedCrossplatformId = null,
            string? accountId = null,
            string? type = null,
            string? businessKind = null,
            TransactionKeyset? keyset = null)
        {
            if (pageSize < 1 || pageSize > MaximumPageSize)
                throw new ArgumentOutOfRangeException(nameof(pageSize));
            PageSize = pageSize;
            RelatedCrossplatformId = EconomyModelValidation.Normalize(relatedCrossplatformId);
            AccountId = EconomyModelValidation.Normalize(accountId);
            Type = EconomyModelValidation.Normalize(type);
            BusinessKind = EconomyModelValidation.Normalize(businessKind);
            Keyset = keyset;
        }

        public int PageSize { get; }
        public string? RelatedCrossplatformId { get; }
        public string? AccountId { get; }
        public string? Type { get; }
        public string? BusinessKind { get; }
        public TransactionKeyset? Keyset { get; }
    }

    public sealed class TransactionPage
    {
        public TransactionPage(
            IEnumerable<LedgerTransactionSnapshot> transactions,
            TransactionKeyset? nextKeyset)
        {
            Transactions = transactions?.ToArray() ?? throw new ArgumentNullException(nameof(transactions));
            NextKeyset = nextKeyset;
        }

        public IReadOnlyList<LedgerTransactionSnapshot> Transactions { get; }
        public TransactionKeyset? NextKeyset { get; }
    }

    public sealed class OpenPlayerAccountCommand
    {
        public OpenPlayerAccountCommand(
            string crossplatformId,
            string idempotencyKey,
            long openingAmount,
            DateTimeOffset occurredAtUtc)
        {
            CrossplatformId = EconomyModelValidation.RequireText(crossplatformId, nameof(crossplatformId));
            IdempotencyKey = EconomyModelValidation.RequireText(idempotencyKey, nameof(idempotencyKey));
            LedgerRules.ValidateAmount(openingAmount);
            EconomyModelValidation.RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
            OpeningAmount = openingAmount;
            OccurredAtUtc = occurredAtUtc;
        }

        public string CrossplatformId { get; }
        public string IdempotencyKey { get; }
        public long OpeningAmount { get; }
        public DateTimeOffset OccurredAtUtc { get; }
    }

    public sealed class SetAccountFrozenCommand
    {
        public SetAccountFrozenCommand(
            string accountId,
            bool isFrozen,
            long expectedRowVersion,
            DateTimeOffset occurredAtUtc)
        {
            AccountId = EconomyModelValidation.RequireText(accountId, nameof(accountId));
            if (expectedRowVersion < 0) throw new ArgumentOutOfRangeException(nameof(expectedRowVersion));
            EconomyModelValidation.RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
            IsFrozen = isFrozen;
            ExpectedRowVersion = expectedRowVersion;
            OccurredAtUtc = occurredAtUtc;
        }

        public string AccountId { get; }
        public bool IsFrozen { get; }
        public long ExpectedRowVersion { get; }
        public DateTimeOffset OccurredAtUtc { get; }
    }

    public sealed class AdjustPlayerBalanceCommand
    {
        public AdjustPlayerBalanceCommand(
            string transactionId,
            string idempotencyKey,
            string crossplatformId,
            LedgerSide playerSide,
            long amount,
            string actorId,
            DateTimeOffset occurredAtUtc,
            string? correlationId,
            string reason)
        {
            TransactionId = EconomyModelValidation.RequireText(transactionId, nameof(transactionId));
            IdempotencyKey = EconomyModelValidation.RequireText(idempotencyKey, nameof(idempotencyKey));
            CrossplatformId = EconomyModelValidation.RequireText(crossplatformId, nameof(crossplatformId));
            EconomyModelValidation.RequireDefined(playerSide, nameof(playerSide));
            LedgerRules.ValidateAmount(amount);
            ActorId = EconomyModelValidation.RequireText(actorId, nameof(actorId));
            EconomyModelValidation.RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
            CorrelationId = EconomyModelValidation.Normalize(correlationId);
            Reason = EconomyModelValidation.RequireText(reason, nameof(reason));
            PlayerSide = playerSide;
            Amount = amount;
            OccurredAtUtc = occurredAtUtc;
        }

        public string TransactionId { get; }
        public string IdempotencyKey { get; }
        public string CrossplatformId { get; }
        public LedgerSide PlayerSide { get; }
        public long Amount { get; }
        public string ActorId { get; }
        public DateTimeOffset OccurredAtUtc { get; }
        public string? CorrelationId { get; }
        public string Reason { get; }
    }

    public sealed class TransferBalanceCommand
    {
        public TransferBalanceCommand(
            string transactionId,
            string idempotencyKey,
            string sourceCrossplatformId,
            string targetCrossplatformId,
            long amount,
            DateTimeOffset occurredAtUtc,
            string? correlationId)
        {
            TransactionId = EconomyModelValidation.RequireText(transactionId, nameof(transactionId));
            IdempotencyKey = EconomyModelValidation.RequireText(idempotencyKey, nameof(idempotencyKey));
            SourceCrossplatformId = EconomyModelValidation.RequireText(
                sourceCrossplatformId, nameof(sourceCrossplatformId));
            TargetCrossplatformId = EconomyModelValidation.RequireText(
                targetCrossplatformId, nameof(targetCrossplatformId));
            if (string.Equals(SourceCrossplatformId, TargetCrossplatformId, StringComparison.Ordinal))
                throw new ArgumentException("Source and target players must differ.", nameof(targetCrossplatformId));
            LedgerRules.ValidateAmount(amount);
            EconomyModelValidation.RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
            CorrelationId = EconomyModelValidation.Normalize(correlationId);
            Amount = amount;
            OccurredAtUtc = occurredAtUtc;
        }

        public string TransactionId { get; }
        public string IdempotencyKey { get; }
        public string SourceCrossplatformId { get; }
        public string TargetCrossplatformId { get; }
        public long Amount { get; }
        public DateTimeOffset OccurredAtUtc { get; }
        public string? CorrelationId { get; }
    }

    public class EconomyException : InvalidOperationException
    {
        public EconomyException(string code) : base(code) { }
    }

    public sealed class EconomyIdempotencyConflictException : EconomyException
    {
        public EconomyIdempotencyConflictException() : base("economy_idempotency_conflict") { }
    }

    public sealed class EconomyUnbalancedTransactionException : EconomyException
    {
        public EconomyUnbalancedTransactionException() : base("economy_transaction_unbalanced") { }
    }

    public sealed class EconomyAccountNotFoundException : EconomyException
    {
        public EconomyAccountNotFoundException() : base("economy_account_not_found") { }
    }

    public sealed class EconomyAccountFrozenException : EconomyException
    {
        public EconomyAccountFrozenException() : base("economy_account_frozen") { }
    }

    public sealed class EconomyAccountDisabledException : EconomyException
    {
        public EconomyAccountDisabledException() : base("economy_account_disabled") { }
    }

    public sealed class EconomyInsufficientFundsException : EconomyException
    {
        public EconomyInsufficientFundsException() : base("economy_insufficient_funds") { }
    }

    public sealed class EconomyConcurrencyException : EconomyException
    {
        public EconomyConcurrencyException() : base("economy_concurrency_conflict") { }
    }

    internal static class EconomyModelValidation
    {
        public static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty value is required.", parameterName);
            return value.Trim();
        }

        public static string? Normalize(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

        public static void RequireUtc(DateTimeOffset value, string parameterName)
        {
            if (value.Offset != TimeSpan.Zero)
                throw new ArgumentException("A UTC timestamp is required.", parameterName);
        }

        public static void RequireDefined<T>(T value, string parameterName) where T : struct, Enum
        {
            if (!Enum.IsDefined(typeof(T), value))
                throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
