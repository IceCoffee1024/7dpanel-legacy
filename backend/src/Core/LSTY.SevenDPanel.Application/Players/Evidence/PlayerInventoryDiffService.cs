using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LSTY.SevenDPanel.Application
{
    public sealed class PlayerInventoryDiffService
    {
        public PlayerInventoryDiff Compare(
            PlayerInventorySnapshot? previous,
            PlayerInventorySnapshot current,
            IEnumerable<PlayerEvidenceGap> gaps,
            IEnumerable<PlayerActionOperation> confirmedOperations)
        {
            if (current == null) throw new ArgumentNullException(nameof(current));
            if (gaps == null) throw new ArgumentNullException(nameof(gaps));
            if (confirmedOperations == null) throw new ArgumentNullException(nameof(confirmedOperations));

            var gapSnapshot = gaps.ToArray();
            var operationSnapshot = confirmedOperations.ToArray();
            if (gapSnapshot.Any(gap => gap == null))
                throw new ArgumentException("Gaps cannot contain null.", nameof(gaps));
            if (operationSnapshot.Any(operation => operation == null))
                throw new ArgumentException("Operations cannot contain null.", nameof(confirmedOperations));

            if (!CanCompare(previous, current, gapSnapshot))
                return Uncomparable(previous, current);

            var sourceOperationIds = operationSnapshot
                .Where(operation =>
                    operation.Status == PlayerActionStatus.Succeeded &&
                    operation.BeforeInventorySnapshotId == previous!.SnapshotId &&
                    operation.AfterInventorySnapshotId == current.SnapshotId &&
                    string.Equals(
                        operation.Target.CrossplatformId,
                        current.CrossplatformId,
                        StringComparison.Ordinal))
                .Select(operation => operation.OperationId)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(operationId => operationId, StringComparer.Ordinal)
                .ToArray();
            var evidenceLevel = sourceOperationIds.Length == 0
                ? EvidenceLevel.ObservedChange
                : EvidenceLevel.Confirmed;

            var changes = CompareItems(
                previous!.Items,
                current.Items,
                evidenceLevel,
                sourceOperationIds);

            return new PlayerInventoryDiff(
                previous.SnapshotId,
                current.SnapshotId,
                previous.ObservedAtUtc,
                current.ObservedAtUtc,
                true,
                changes);
        }

        private static bool CanCompare(
            PlayerInventorySnapshot? previous,
            PlayerInventorySnapshot current,
            IReadOnlyList<PlayerEvidenceGap> gaps)
        {
            if (previous == null) return false;
            if (previous.SnapshotId == current.SnapshotId) return false;
            if (!string.Equals(previous.CrossplatformId, current.CrossplatformId, StringComparison.Ordinal))
                return false;
            if (current.ObservedAtUtc < previous.ObservedAtUtc) return false;
            if (previous.CatalogResolution != CatalogResolutionState.Resolved ||
                current.CatalogResolution != CatalogResolutionState.Resolved)
                return false;

            return !gaps.Any(gap =>
                string.Equals(gap.CrossplatformId, current.CrossplatformId, StringComparison.Ordinal) &&
                gap.StartedAtUtc <= current.ObservedAtUtc &&
                gap.EndedAtUtc >= previous.ObservedAtUtc);
        }

        private static PlayerInventoryDiff Uncomparable(
            PlayerInventorySnapshot? previous,
            PlayerInventorySnapshot current) =>
            new PlayerInventoryDiff(
                previous?.SnapshotId,
                current.SnapshotId,
                previous?.ObservedAtUtc,
                current.ObservedAtUtc,
                false,
                new[]
                {
                    new PlayerInventoryDiffEntry(
                        InventoryDiffKind.Uncomparable,
                        null,
                        null,
                        EvidenceLevel.ObservedChange,
                        Array.Empty<string>())
                });

        private static IReadOnlyList<PlayerInventoryDiffEntry> CompareItems(
            IReadOnlyList<InventoryItemScalar> previousItems,
            IReadOnlyList<InventoryItemScalar> currentItems,
            EvidenceLevel evidenceLevel,
            IReadOnlyList<string> sourceOperationIds)
        {
            var changes = new List<PlayerInventoryDiffEntry>();
            var previousMatched = new bool[previousItems.Count];
            var currentMatched = new bool[currentItems.Count];
            var currentByLocation = Enumerable.Range(0, currentItems.Count)
                .ToDictionary(index => Location(currentItems[index]), index => index, StringComparer.OrdinalIgnoreCase);
            var previousOrder = OrderedIndexes(previousItems).ToArray();
            var currentOrder = OrderedIndexes(currentItems).ToArray();

            foreach (var previousIndex in previousOrder)
            {
                var previousItem = previousItems[previousIndex];
                if (!currentByLocation.TryGetValue(Location(previousItem), out var currentIndex))
                    continue;

                var currentItem = currentItems[currentIndex];
                if (ItemValuesEqual(previousItem, currentItem))
                {
                    previousMatched[previousIndex] = true;
                    currentMatched[currentIndex] = true;
                    continue;
                }

                if (!string.Equals(previousItem.InternalName, currentItem.InternalName, StringComparison.Ordinal))
                    continue;

                var kind = AttributesEqual(previousItem, currentItem)
                    ? InventoryDiffKind.QuantityChanged
                    : InventoryDiffKind.AttributesChanged;
                changes.Add(Change(
                    kind,
                    previousItem,
                    currentItem,
                    evidenceLevel,
                    sourceOperationIds));
                previousMatched[previousIndex] = true;
                currentMatched[currentIndex] = true;
            }

            foreach (var previousIndex in previousOrder.Where(index => !previousMatched[index]))
            {
                var previousItem = previousItems[previousIndex];
                var currentIndex = currentOrder
                    .Where(index =>
                        !currentMatched[index] && ItemValuesEqual(previousItem, currentItems[index]))
                    .Select(index => (int?)index)
                    .FirstOrDefault();
                if (!currentIndex.HasValue)
                    continue;

                changes.Add(Change(
                    InventoryDiffKind.Moved,
                    previousItem,
                    currentItems[currentIndex.Value],
                    evidenceLevel,
                    sourceOperationIds));
                previousMatched[previousIndex] = true;
                currentMatched[currentIndex.Value] = true;
            }

            foreach (var previousIndex in previousOrder.Where(index => !previousMatched[index]))
                changes.Add(Change(
                    InventoryDiffKind.Removed,
                    previousItems[previousIndex],
                    null,
                    evidenceLevel,
                    sourceOperationIds));

            foreach (var currentIndex in currentOrder.Where(index => !currentMatched[index]))
                changes.Add(Change(
                    InventoryDiffKind.Added,
                    null,
                    currentItems[currentIndex],
                    evidenceLevel,
                    sourceOperationIds));

            return changes;
        }

        private static IEnumerable<int> OrderedIndexes(IReadOnlyList<InventoryItemScalar> items) =>
            Enumerable.Range(0, items.Count)
                .OrderBy(index => items[index].Container, StringComparer.OrdinalIgnoreCase)
                .ThenBy(index => items[index].Slot)
                .ThenBy(index => items[index].InternalName, StringComparer.Ordinal);

        private static PlayerInventoryDiffEntry Change(
            InventoryDiffKind kind,
            InventoryItemScalar? previousItem,
            InventoryItemScalar? currentItem,
            EvidenceLevel evidenceLevel,
            IReadOnlyList<string> sourceOperationIds) =>
            new PlayerInventoryDiffEntry(
                kind,
                previousItem,
                currentItem,
                evidenceLevel,
                sourceOperationIds);

        private static string Location(InventoryItemScalar item) =>
            item.Container + "\u001f" + item.Slot.ToString(CultureInfo.InvariantCulture);

        private static bool ItemValuesEqual(InventoryItemScalar left, InventoryItemScalar right) =>
            string.Equals(left.InternalName, right.InternalName, StringComparison.Ordinal) &&
            left.Count == right.Count &&
            AttributesEqual(left, right);

        private static bool AttributesEqual(InventoryItemScalar left, InventoryItemScalar right) =>
            left.Quality == right.Quality &&
            left.UseAmount == right.UseAmount &&
            left.ModInternalNames.SequenceEqual(right.ModInternalNames, StringComparer.Ordinal);
    }
}
