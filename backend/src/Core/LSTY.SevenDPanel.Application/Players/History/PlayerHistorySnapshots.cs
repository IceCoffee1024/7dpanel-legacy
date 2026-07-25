using System;
using System.Collections.Generic;
using System.Linq;

namespace LSTY.SevenDPanel.Application
{
    public sealed class PlayerHistorySnapshotsQuery
    {
        public const int DefaultPageSize = 100;
        public const int MaximumPageSize = 200;

        public PlayerHistorySnapshotsQuery(string crossplatformId, int pageSize, long? beforeSnapshotId)
        {
            if (pageSize < 1 || pageSize > MaximumPageSize)
                throw new ArgumentOutOfRangeException(nameof(pageSize));
            if (beforeSnapshotId.HasValue && beforeSnapshotId.Value <= 0)
                throw new ArgumentOutOfRangeException(nameof(beforeSnapshotId));

            CrossplatformId = HistoryPlayerValidation.RequireCrossplatformId(
                crossplatformId,
                nameof(crossplatformId));
            PageSize = pageSize;
            BeforeSnapshotId = beforeSnapshotId;
        }

        public string CrossplatformId { get; }

        public int PageSize { get; }

        public long? BeforeSnapshotId { get; }
    }

    public sealed class HistoricalPlayerSnapshot
    {
        public HistoricalPlayerSnapshot(long snapshotId, PlayerSnapshot player)
        {
            if (snapshotId <= 0)
                throw new ArgumentOutOfRangeException(nameof(snapshotId));

            Player = player ?? throw new ArgumentNullException(nameof(player));
            if (Player.CrossplatformIdentity == null)
                throw new ArgumentException("A historical snapshot requires a cross-platform identity.", nameof(player));

            SnapshotId = snapshotId;
        }

        public long SnapshotId { get; }

        public PlayerSnapshot Player { get; }
    }

    public sealed class HistoricalPlayersPage
    {
        public HistoricalPlayersPage(
            IEnumerable<HistoricalPlayerSummary> players,
            HistoricalPlayersCursor? nextCursor)
        {
            if (players == null) throw new ArgumentNullException(nameof(players));

            Players = players.ToArray();
            NextCursor = nextCursor;
        }

        public IReadOnlyList<HistoricalPlayerSummary> Players { get; }

        public HistoricalPlayersCursor? NextCursor { get; }
    }

    public sealed class PlayerHistorySnapshotsPage
    {
        public PlayerHistorySnapshotsPage(
            IEnumerable<HistoricalPlayerSnapshot> snapshots,
            long? nextBeforeSnapshotId,
            IEnumerable<PlayerHistoryGap> gaps)
        {
            if (snapshots == null) throw new ArgumentNullException(nameof(snapshots));
            if (gaps == null) throw new ArgumentNullException(nameof(gaps));
            if (nextBeforeSnapshotId.HasValue && nextBeforeSnapshotId.Value <= 0)
                throw new ArgumentOutOfRangeException(nameof(nextBeforeSnapshotId));

            Snapshots = snapshots.ToArray();
            NextBeforeSnapshotId = nextBeforeSnapshotId;
            Gaps = gaps.ToArray();
        }

        public IReadOnlyList<HistoricalPlayerSnapshot> Snapshots { get; }

        public long? NextBeforeSnapshotId { get; }

        public IReadOnlyList<PlayerHistoryGap> Gaps { get; }
    }
}
