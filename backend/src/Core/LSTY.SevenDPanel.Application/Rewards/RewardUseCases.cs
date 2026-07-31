using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application.Economy;
using LSTY.SevenDPanel.Domain.Economy;
using LSTY.SevenDPanel.Domain.Rewards;

namespace LSTY.SevenDPanel.Application.Rewards
{
    public sealed class SaveRewardPackageUseCase
    {
        private readonly IRewardStore store;
        private readonly IGameResourceCatalog catalog;
        private readonly Func<DateTimeOffset> utcClock;

        public SaveRewardPackageUseCase(IRewardStore store, IGameResourceCatalog catalog)
            : this(store, catalog, () => DateTimeOffset.UtcNow) { }

        internal SaveRewardPackageUseCase(
            IRewardStore store,
            IGameResourceCatalog catalog,
            Func<DateTimeOffset> utcClock)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
        }

        public RewardPackageSnapshot Execute(RewardPackageDraft package)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));
            RewardCatalogResolver.ValidatePackage(package, catalog);
            return store.SavePackage(package, UtcNow());
        }

        private DateTimeOffset UtcNow()
        {
            var value = utcClock();
            RewardValidation.RequireUtc(value, nameof(utcClock));
            return value;
        }
    }

    public sealed class GrantRewardUseCase
    {
        private readonly IRewardStore store;
        private readonly IRewardDeliveryPort delivery;
        private readonly IEconomyLedgerStore ledger;
        private readonly IGameResourceCatalog catalog;
        private readonly Func<string> operationIdFactory;
        private readonly Func<string> operationEntryIdFactory;
        private readonly Func<DateTimeOffset> utcClock;

        public GrantRewardUseCase(
            IRewardStore store,
            IRewardDeliveryPort delivery,
            IEconomyLedgerStore ledger,
            IGameResourceCatalog catalog)
            : this(
                store,
                delivery,
                ledger,
                catalog,
                () => "grant-" + Guid.NewGuid().ToString("N"),
                () => "grant-entry-" + Guid.NewGuid().ToString("N"),
                () => DateTimeOffset.UtcNow)
        {
        }

        internal GrantRewardUseCase(
            IRewardStore store,
            IRewardDeliveryPort delivery,
            IEconomyLedgerStore ledger,
            IGameResourceCatalog catalog,
            Func<string> operationIdFactory,
            Func<string> operationEntryIdFactory,
            Func<DateTimeOffset> utcClock)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.delivery = delivery ?? throw new ArgumentNullException(nameof(delivery));
            this.ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.operationIdFactory = operationIdFactory ??
                throw new ArgumentNullException(nameof(operationIdFactory));
            this.operationEntryIdFactory = operationEntryIdFactory ??
                throw new ArgumentNullException(nameof(operationEntryIdFactory));
            this.utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
        }

        public async Task<GrantRewardResult> ExecuteAsync(
            GrantRewardCommand command,
            CancellationToken cancellationToken)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            var package = store.GetPackage(command.PackageId);
            if (!package.Enabled) throw new InvalidOperationException("reward_package_disabled");
            var resolvedPackage = RewardCatalogResolver.ResolvePackage(package, catalog);
            var createdAtUtc = UtcNow();
            var operationId = RewardValidation.RequireText(operationIdFactory(), nameof(operationIdFactory));
            var operationEntries = package.Entries.Select(entry => new GrantOperationEntryDraft(
                RewardValidation.RequireText(operationEntryIdFactory(), nameof(operationEntryIdFactory)),
                entry.EntryId,
                entry.Ordinal,
                entry.Kind)).ToArray();
            var creation = store.GetOrCreateGrant(new GrantOperationDraft(
                operationId,
                command.PackageId,
                command.CrossplatformId,
                command.ExpectedEntityId,
                command.ExpectedWorldId,
                command.IdempotencyKey,
                command.EligibilityKey,
                command.SourceKind,
                command.SourceId,
                command.ActorKind,
                command.ActorId,
                command.ReservationId,
                command.CompensatesOperationId,
                command.CorrelationId,
                createdAtUtc,
                operationEntries));
            if (!creation.Created)
                return new GrantRewardResult(creation.Operation, true);

            if (!store.TryStartDispatch(
                    creation.Operation.OperationId,
                    creation.Operation.RowVersion,
                    UtcNow()))
            {
                return new GrantRewardResult(store.GetGrant(creation.Operation.OperationId), true);
            }

            var dispatching = store.GetGrant(creation.Operation.OperationId);
            var resolvedEntries = RewardCatalogResolver.BindOperationEntries(
                resolvedPackage,
                dispatching.Entries,
                package);
            RewardDeliveryResult deliveryResult;
            try
            {
                deliveryResult = await delivery.DeliverAsync(
                        new RewardDeliveryCommand(
                            dispatching.OperationId,
                            dispatching.CrossplatformId,
                            dispatching.ExpectedEntityId,
                            dispatching.ExpectedWorldId,
                            resolvedEntries),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (deliveryResult == null)
                    deliveryResult = RewardDeliveryResult.ResultUnknown(
                        Array.Empty<RewardDeliveryEntryResult>(),
                        "reward_delivery_returned_null");
            }
            catch
            {
                deliveryResult = RewardDeliveryResult.ResultUnknown(
                    Array.Empty<RewardDeliveryEntryResult>(),
                    "reward_delivery_result_unknown");
            }

            deliveryResult = NormalizeDeliveryResult(deliveryResult, resolvedEntries);
            string? ledgerTransactionId = null;
            var finalState = deliveryResult.Status switch
            {
                RewardDeliveryStatus.Succeeded => GrantOperationState.Completed,
                RewardDeliveryStatus.Failed => GrantOperationState.Failed,
                RewardDeliveryStatus.ResultUnknown => GrantOperationState.PendingReconciliation,
                _ => throw new ArgumentOutOfRangeException()
            };
            var finalError = deliveryResult.ErrorCode;

            if (finalState == GrantOperationState.Completed)
            {
                try
                {
                    ledgerTransactionId = RewardLedgerWriter.Issue(
                        ledger,
                        dispatching,
                        resolvedEntries,
                        UtcNow());
                }
                catch
                {
                    finalState = GrantOperationState.PendingReconciliation;
                    finalError = "reward_currency_result_unknown";
                }
            }

            var resolution = BuildResolution(
                dispatching,
                resolvedEntries,
                deliveryResult,
                finalState,
                ledgerTransactionId,
                finalError,
                UtcNow());
            var persisted = false;
            try { persisted = store.TryResolveDispatch(resolution); }
            catch { }
            if (!persisted)
            {
                try
                {
                    store.TryMarkPendingReconciliation(
                        dispatching.OperationId,
                        finalError ?? "reward_terminal_persistence_failed",
                        UtcNow());
                }
                catch { }
            }

            return new GrantRewardResult(store.GetGrant(dispatching.OperationId), false);
        }

        private static RewardDeliveryResult NormalizeDeliveryResult(
            RewardDeliveryResult result,
            IReadOnlyList<ResolvedRewardEntry> entries)
        {
            var gameEntries = entries
                .Where(entry => entry.Kind != RewardEntryKind.Currency)
                .Select(entry => entry.OperationEntryId)
                .ToArray();
            var supplied = result.Entries.Select(entry => entry.OperationEntryId).ToArray();
            if (supplied.Distinct(StringComparer.Ordinal).Count() != supplied.Length ||
                supplied.Any(id => !gameEntries.Contains(id, StringComparer.Ordinal)))
            {
                return RewardDeliveryResult.ResultUnknown(
                    result.Entries,
                    "reward_delivery_contract_invalid");
            }
            if (result.Status == RewardDeliveryStatus.Succeeded &&
                (supplied.Length != gameEntries.Length ||
                 result.Entries.Any(entry => entry.Status != RewardDeliveryStatus.Succeeded)))
            {
                return RewardDeliveryResult.ResultUnknown(
                    result.Entries,
                    "reward_delivery_incomplete");
            }
            return result;
        }

        private static GrantDispatchResolution BuildResolution(
            GrantOperationSnapshot operation,
            IReadOnlyList<ResolvedRewardEntry> entries,
            RewardDeliveryResult deliveryResult,
            GrantOperationState finalState,
            string? ledgerTransactionId,
            string? errorCode,
            DateTimeOffset occurredAtUtc)
        {
            var results = deliveryResult.Entries.ToDictionary(
                entry => entry.OperationEntryId,
                StringComparer.Ordinal);
            var resolutions = new List<GrantEntryResolution>(entries.Count);
            foreach (var entry in entries)
            {
                if (entry.Kind == RewardEntryKind.Currency)
                {
                    resolutions.Add(new GrantEntryResolution(
                        entry.OperationEntryId,
                        finalState,
                        null,
                        finalState == GrantOperationState.Completed ? ledgerTransactionId : null,
                        finalState == GrantOperationState.Completed ? null : errorCode));
                    continue;
                }

                if (results.TryGetValue(entry.OperationEntryId, out var result))
                {
                    var state = result.Status switch
                    {
                        RewardDeliveryStatus.Succeeded => GrantOperationState.Completed,
                        RewardDeliveryStatus.Failed => GrantOperationState.Failed,
                        RewardDeliveryStatus.ResultUnknown => GrantOperationState.PendingReconciliation,
                        _ => throw new ArgumentOutOfRangeException()
                    };
                    resolutions.Add(new GrantEntryResolution(
                        entry.OperationEntryId,
                        state,
                        result.DeliveryOperationId,
                        null,
                        result.ErrorCode));
                    continue;
                }

                resolutions.Add(new GrantEntryResolution(
                    entry.OperationEntryId,
                    finalState == GrantOperationState.Failed
                        ? GrantOperationState.Failed
                        : GrantOperationState.PendingReconciliation,
                    null,
                    null,
                    errorCode));
            }

            return new GrantDispatchResolution(
                operation.OperationId,
                operation.RowVersion,
                finalState,
                resolutions,
                errorCode,
                occurredAtUtc);
        }

        private DateTimeOffset UtcNow()
        {
            var value = utcClock();
            RewardValidation.RequireUtc(value, nameof(utcClock));
            return value;
        }
    }

    public sealed class PendingRewardReconciliationUseCase
    {
        private readonly IRewardStore store;

        public PendingRewardReconciliationUseCase(IRewardStore store) =>
            this.store = store ?? throw new ArgumentNullException(nameof(store));

        public IReadOnlyList<GrantOperationSnapshot> Execute(int take) =>
            store.ListPendingReconciliation(take);
    }

    public sealed class ConfirmRewardGrantUseCase
    {
        private readonly IRewardStore store;
        private readonly IEconomyLedgerStore ledger;

        public ConfirmRewardGrantUseCase(IRewardStore store, IEconomyLedgerStore ledger)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        }

        public GrantOperationSnapshot Execute(ConfirmRewardGrantCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            var operation = store.GetGrant(command.OperationId);
            if (operation.State != GrantOperationState.PendingReconciliation)
                throw new InvalidOperationException("reward_grant_not_pending_reconciliation");
            var package = store.GetPackage(operation.PackageId);
            var entries = RewardCatalogResolver.BindOperationEntriesWithoutCatalog(
                package,
                operation.Entries);
            var ledgerTransactionId = RewardLedgerWriter.Issue(
                ledger,
                operation,
                entries,
                command.OccurredAtUtc);
            if (!store.TryConfirmReconciled(
                    operation.OperationId,
                    operation.RowVersion,
                    command.ActorId,
                    command.CorrelationId,
                    ledgerTransactionId,
                    command.OccurredAtUtc))
            {
                throw new RewardConcurrencyException();
            }
            return store.GetGrant(operation.OperationId);
        }
    }

    public sealed class RefundRewardGrantUseCase
    {
        private readonly IRewardStore store;
        private readonly IEconomyLedgerStore ledger;

        public RefundRewardGrantUseCase(IRewardStore store, IEconomyLedgerStore ledger)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        }

        public GrantOperationSnapshot Execute(RefundRewardGrantCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            var operation = store.GetGrant(command.OperationId);
            if (operation.State != GrantOperationState.Completed)
                throw new InvalidOperationException("reward_grant_not_refundable");
            var package = store.GetPackage(operation.PackageId);
            var amount = RewardLedgerWriter.CurrencyAmount(package.Entries);
            var account = ledger.GetOrCreatePlayerAccount(
                operation.CrossplatformId,
                "reward-account:" + operation.CrossplatformId,
                0,
                command.OccurredAtUtc);
            var transactionId = "reward-refund:" + operation.OperationId;
            LedgerWriteResult write;
            try
            {
                write = ledger.Commit(new LedgerTransactionDraft(
                    transactionId,
                    "RewardRefund",
                    transactionId,
                    command.OccurredAtUtc,
                    command.ActorKind,
                    command.ActorId,
                    operation.CrossplatformId,
                    "GrantRefund",
                    operation.OperationId,
                    command.CorrelationId,
                    null,
                    new[]
                    {
                        new LedgerEntryDraft(account.AccountId, LedgerSide.Debit, amount),
                        new LedgerEntryDraft(SystemAccountIds.Rewards, LedgerSide.Credit, amount)
                    }));
            }
            catch (EconomyIdempotencyConflictException)
            {
                throw new RewardConcurrencyException();
            }
            if (!store.TryMarkRefunded(
                    operation.OperationId,
                    operation.RowVersion,
                    write.Transaction.TransactionId,
                    command.CorrelationId,
                    command.OccurredAtUtc))
            {
                throw new RewardConcurrencyException();
            }
            return store.GetGrant(operation.OperationId);
        }
    }

    public sealed class CompensateRewardGrantUseCase
    {
        private readonly IRewardStore store;
        private readonly GrantRewardUseCase grant;

        public CompensateRewardGrantUseCase(IRewardStore store, GrantRewardUseCase grant)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.grant = grant ?? throw new ArgumentNullException(nameof(grant));
        }

        public Task<GrantRewardResult> ExecuteAsync(
            CompensateRewardGrantCommand command,
            CancellationToken cancellationToken)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            var source = store.GetGrant(command.OperationId);
            if (source.State != GrantOperationState.Completed &&
                source.State != GrantOperationState.PendingReconciliation)
            {
                throw new InvalidOperationException("reward_grant_not_compensatable");
            }
            return grant.ExecuteAsync(new GrantRewardCommand(
                source.PackageId,
                source.CrossplatformId,
                source.ExpectedEntityId,
                source.ExpectedWorldId,
                command.IdempotencyKey,
                null,
                "Compensation",
                source.OperationId,
                command.ActorKind,
                command.ActorId,
                command.CorrelationId,
                null,
                source.OperationId), cancellationToken);
        }
    }

    internal static class RewardCatalogResolver
    {
        internal static void ValidatePackage(RewardPackageDraft package, IGameResourceCatalog catalog)
        {
            var itemEntries = package.Entries.Where(entry => entry.Kind == RewardEntryKind.Item).ToArray();
            if (itemEntries.Length == 0) return;
            var snapshot = ReadAvailable(catalog);
            foreach (var entry in itemEntries)
            {
                ValidateItem(
                    snapshot,
                    entry.ItemInternalName!,
                    entry.ItemKind!.Value,
                    entry.CatalogVersion!);
            }
        }

        internal static IReadOnlyDictionary<string, GameResourceCatalogEntry> ResolvePackage(
            RewardPackageSnapshot package,
            IGameResourceCatalog catalog)
        {
            var items = package.Entries.Where(entry => entry.Kind == RewardEntryKind.Item).ToArray();
            if (items.Length == 0)
                return new Dictionary<string, GameResourceCatalogEntry>(StringComparer.Ordinal);
            var snapshot = ReadAvailable(catalog);
            return items.ToDictionary(
                entry => entry.EntryId,
                entry => ValidateItem(
                    snapshot,
                    entry.ItemInternalName!,
                    entry.ItemKind!.Value,
                    entry.CatalogVersion!),
                StringComparer.Ordinal);
        }

        internal static IReadOnlyList<ResolvedRewardEntry> BindOperationEntries(
            IReadOnlyDictionary<string, GameResourceCatalogEntry> resources,
            IReadOnlyList<GrantOperationEntrySnapshot> operationEntries,
            RewardPackageSnapshot package) => Bind(resources, package, operationEntries);

        internal static IReadOnlyList<ResolvedRewardEntry> BindOperationEntriesWithoutCatalog(
            RewardPackageSnapshot package,
            IReadOnlyList<GrantOperationEntrySnapshot> operationEntries) =>
            Bind(
                new Dictionary<string, GameResourceCatalogEntry>(StringComparer.Ordinal),
                package,
                operationEntries,
                requireResources: false);

        private static IReadOnlyList<ResolvedRewardEntry> Bind(
            IReadOnlyDictionary<string, GameResourceCatalogEntry> resources,
            RewardPackageSnapshot package,
            IReadOnlyList<GrantOperationEntrySnapshot> operationEntries,
            bool requireResources = true)
        {
            var packageById = package.Entries.ToDictionary(entry => entry.EntryId, StringComparer.Ordinal);
            return operationEntries.OrderBy(entry => entry.Ordinal).Select(operationEntry =>
            {
                if (!packageById.TryGetValue(operationEntry.PackageEntryId, out var packageEntry))
                    throw new InvalidOperationException("reward_package_entry_missing");
                GameResourceCatalogEntry? resource = null;
                if (packageEntry.Kind == RewardEntryKind.Item &&
                    !resources.TryGetValue(packageEntry.EntryId, out resource) && requireResources)
                {
                    throw new RewardCatalogValidationException("reward_catalog_entry_missing");
                }
                return new ResolvedRewardEntry(
                    operationEntry.OperationEntryId,
                    packageEntry.EntryId,
                    packageEntry.Ordinal,
                    packageEntry.Kind,
                    resource?.ResourceId,
                    packageEntry.ItemInternalName,
                    packageEntry.ItemKind,
                    packageEntry.Quantity,
                    packageEntry.MinQuality,
                    packageEntry.CatalogVersion,
                    resource?.Visibility == GameResourceVisibility.Hidden,
                    packageEntry.CurrencyAmount,
                    packageEntry.RegisteredAction);
            }).ToArray();
        }

        private static GameResourceCatalogSnapshot ReadAvailable(IGameResourceCatalog catalog)
        {
            GameResourceCatalogReadResult result;
            try { result = catalog.Read(); }
            catch { throw new RewardCatalogValidationException("reward_catalog_unavailable"); }
            if (result.Status != GameResourceCatalogReadStatus.Available || result.Snapshot == null)
                throw new RewardCatalogValidationException("reward_catalog_unavailable");
            return result.Snapshot;
        }

        private static GameResourceCatalogEntry ValidateItem(
            GameResourceCatalogSnapshot snapshot,
            string internalName,
            GameResourceKind kind,
            string catalogVersion)
        {
            if (!string.Equals(snapshot.CatalogVersion, catalogVersion, StringComparison.Ordinal))
                throw new RewardCatalogValidationException("reward_catalog_changed");
            var matches = snapshot.Resources.Where(resource =>
                    string.Equals(resource.InternalName, internalName, StringComparison.Ordinal) &&
                    resource.Kind == kind)
                .Take(2)
                .ToArray();
            if (matches.Length != 1)
                throw new RewardCatalogValidationException("reward_catalog_entry_invalid");
            return matches[0];
        }
    }

    internal static class RewardLedgerWriter
    {
        internal static string? Issue(
            IEconomyLedgerStore ledger,
            GrantOperationSnapshot operation,
            IReadOnlyList<ResolvedRewardEntry> entries,
            DateTimeOffset occurredAtUtc)
        {
            var currency = entries.Where(entry => entry.Kind == RewardEntryKind.Currency).ToArray();
            if (currency.Length == 0) return null;
            var amount = CurrencyAmount(currency.Select(entry => entry.CurrencyAmount!.Value));
            var account = ledger.GetOrCreatePlayerAccount(
                operation.CrossplatformId,
                "reward-account:" + operation.CrossplatformId,
                0,
                occurredAtUtc);
            var transactionId = "reward-grant:" + operation.OperationId;
            var write = ledger.Commit(new LedgerTransactionDraft(
                transactionId,
                "RewardGrant",
                transactionId,
                occurredAtUtc,
                EconomyActorKinds.System,
                SystemAccountIds.Rewards,
                operation.CrossplatformId,
                "Grant",
                operation.OperationId,
                operation.CorrelationId,
                null,
                new[]
                {
                    new LedgerEntryDraft(SystemAccountIds.Rewards, LedgerSide.Debit, amount),
                    new LedgerEntryDraft(account.AccountId, LedgerSide.Credit, amount)
                }));
            return write.Transaction.TransactionId;
        }

        internal static long CurrencyAmount(IEnumerable<RewardPackageEntrySnapshot> entries) =>
            CurrencyAmount(entries
                .Where(entry => entry.Kind == RewardEntryKind.Currency)
                .Select(entry => entry.CurrencyAmount!.Value));

        private static long CurrencyAmount(IEnumerable<long> values)
        {
            long total = 0;
            checked
            {
                foreach (var value in values) total += value;
            }
            return total;
        }
    }
}
