using System;
using System.Collections.Generic;
using System.Linq;

namespace LSTY.SevenDPanel.Application
{
    public sealed class GetInventoryDiffsUseCase
    {
        private readonly IPlayerEvidenceStore store;
        private readonly IPlayerActionOperationQuery operations;
        private readonly PlayerInventoryDiffService diffService;

        public GetInventoryDiffsUseCase(
            IPlayerEvidenceStore store,
            IPlayerActionOperationQuery operations,
            PlayerInventoryDiffService diffService)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.operations = operations ?? throw new ArgumentNullException(nameof(operations));
            this.diffService = diffService ?? throw new ArgumentNullException(nameof(diffService));
        }

        public PlayerProfileSection<PlayerInventoryDiffsPage> Execute(
            PlayerInventoryDiffsQuery query,
            PlayerEvidenceAccess access,
            IEnumerable<string> candidateOperationIds)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (candidateOperationIds == null)
                throw new ArgumentNullException(nameof(candidateOperationIds));
            PlayerEvidenceUseCaseSupport.RequireAccess(access);
            if (access != PlayerEvidenceAccess.Owner)
                return PlayerEvidenceUseCaseSupport.Forbidden<PlayerInventoryDiffsPage>();

            var operationIds = candidateOperationIds
                .Select(operationId => PlayerEvidenceValidation.RequireText(
                    operationId,
                    nameof(candidateOperationIds)))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            SnapshotRead read;
            try
            {
                read = ReadSnapshots(query);
            }
            catch (Exception)
            {
                return PlayerEvidenceUseCaseSupport.Unavailable<PlayerInventoryDiffsPage>();
            }

            var operationSourcePartial = false;
            var operationSnapshot = new List<PlayerActionOperation>();
            foreach (var operationId in operationIds)
            {
                try
                {
                    var operation = operations.Get(operationId);
                    if (operation != null) operationSnapshot.Add(operation);
                }
                catch (Exception)
                {
                    operationSourcePartial = true;
                }
            }

            var currentSnapshots = read.Snapshots.Take(query.PageSize).ToArray();
            var diffs = new List<PlayerInventoryDiff>(currentSnapshots.Length);
            for (var index = 0; index < currentSnapshots.Length; index++)
            {
                var previous = index + 1 < read.Snapshots.Count
                    ? read.Snapshots[index + 1]
                    : null;
                diffs.Add(diffService.Compare(
                    previous,
                    currentSnapshots[index],
                    read.Gaps,
                    operationSnapshot));
            }

            var nextCursor = read.Snapshots.Count > query.PageSize && currentSnapshots.Length > 0
                ? Cursor(currentSnapshots[currentSnapshots.Length - 1])
                : null;
            var page = new PlayerInventoryDiffsPage(diffs, nextCursor);
            var state = operationSourcePartial ||
                        read.Gaps.Count > 0 ||
                        diffs.Any(diff => !diff.IsComplete)
                ? PlayerProfileSectionState.Partial
                : PlayerProfileSectionState.Available;
            var observedAtUtc = currentSnapshots.Length == 0
                ? (DateTimeOffset?)null
                : currentSnapshots.Max(snapshot => snapshot.ObservedAtUtc);
            return new PlayerProfileSection<PlayerInventoryDiffsPage>(
                state,
                observedAtUtc,
                page,
                read.Gaps);
        }

        private SnapshotRead ReadSnapshots(PlayerInventoryDiffsQuery query)
        {
            var snapshots = new List<PlayerInventorySnapshot>();
            var gaps = new Dictionary<long, PlayerEvidenceGap>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var cursor = query.Cursor;
            var required = query.PageSize + 1;

            while (snapshots.Count < required)
            {
                var requestSize = Math.Min(
                    PlayerInventorySnapshotsQuery.MaximumPageSize,
                    required - snapshots.Count);
                var page = store.GetInventorySnapshots(
                    new PlayerInventorySnapshotsQuery(query.CrossplatformId, requestSize, cursor)) ??
                    throw new InvalidOperationException("The inventory source returned no page.");

                foreach (var gap in page.Gaps)
                    gaps[gap.GapId] = gap;
                foreach (var snapshot in page.Snapshots
                             .Where(snapshot =>
                                 string.Equals(
                                     snapshot.CrossplatformId,
                                     query.CrossplatformId,
                                     StringComparison.Ordinal) &&
                                 IsAfterCursor(snapshot, query.Cursor))
                             .OrderByDescending(snapshot => snapshot.ObservedAtUtc)
                             .ThenByDescending(snapshot => snapshot.SnapshotId))
                {
                    var key = snapshot.ObservedAtUtc.UtcTicks + ":" + snapshot.SnapshotId;
                    if (keys.Add(key)) snapshots.Add(snapshot);
                }

                snapshots.Sort(CompareSnapshots);
                if (snapshots.Count >= required || page.NextCursor == null)
                    break;
                if (cursor != null && page.NextCursor.CompareTo(cursor) == 0)
                    break;
                cursor = page.NextCursor;
            }

            return new SnapshotRead(
                snapshots.Take(required).ToArray(),
                gaps.Values.OrderBy(gap => gap.StartedAtUtc).ThenBy(gap => gap.GapId).ToArray());
        }

        private static int CompareSnapshots(
            PlayerInventorySnapshot left,
            PlayerInventorySnapshot right)
        {
            var observed = right.ObservedAtUtc.CompareTo(left.ObservedAtUtc);
            return observed != 0 ? observed : right.SnapshotId.CompareTo(left.SnapshotId);
        }

        private static bool IsAfterCursor(
            PlayerInventorySnapshot snapshot,
            PlayerEvidenceCursor? cursor) =>
            cursor == null ||
            snapshot.ObservedAtUtc < cursor.ObservedAtUtc ||
            (snapshot.ObservedAtUtc == cursor.ObservedAtUtc && snapshot.SnapshotId < cursor.Id);

        private static PlayerEvidenceCursor Cursor(PlayerInventorySnapshot snapshot) =>
            new PlayerEvidenceCursor(snapshot.ObservedAtUtc, snapshot.SnapshotId);

        private sealed class SnapshotRead
        {
            public SnapshotRead(
                IReadOnlyList<PlayerInventorySnapshot> snapshots,
                IReadOnlyList<PlayerEvidenceGap> gaps)
            {
                Snapshots = snapshots;
                Gaps = gaps;
            }

            public IReadOnlyList<PlayerInventorySnapshot> Snapshots { get; }
            public IReadOnlyList<PlayerEvidenceGap> Gaps { get; }
        }
    }
}
