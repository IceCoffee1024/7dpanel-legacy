using System;
using System.Collections.Generic;

namespace LSTY.SevenDPanel.Application
{
    public sealed class PlayerEvidenceCursor : IComparable<PlayerEvidenceCursor>
    {
        public PlayerEvidenceCursor(DateTimeOffset observedAtUtc, long id)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
            ObservedAtUtc = PlayerEvidenceValidation.RequireUtc(observedAtUtc, nameof(observedAtUtc));
            Id = id;
        }

        public DateTimeOffset ObservedAtUtc { get; }

        public long Id { get; }

        public int CompareTo(PlayerEvidenceCursor? other)
        {
            if (other == null) return -1;
            var observedComparison = other.ObservedAtUtc.CompareTo(ObservedAtUtc);
            return observedComparison != 0 ? observedComparison : other.Id.CompareTo(Id);
        }
    }

    public sealed class PlayerInventorySnapshotsQuery
    {
        public const int MaximumPageSize = 200;

        public PlayerInventorySnapshotsQuery(
            string crossplatformId,
            int pageSize,
            PlayerEvidenceCursor? cursor)
        {
            CrossplatformId = PlayerEvidenceQueryValidation.Require(crossplatformId, pageSize);
            PageSize = pageSize;
            Cursor = cursor;
        }

        public string CrossplatformId { get; }
        public int PageSize { get; }
        public PlayerEvidenceCursor? Cursor { get; }
    }

    public sealed class PlayerInventoryDiffsQuery
    {
        public const int MaximumPageSize = 200;

        public PlayerInventoryDiffsQuery(
            string crossplatformId,
            int pageSize,
            PlayerEvidenceCursor? cursor)
        {
            CrossplatformId = PlayerEvidenceQueryValidation.Require(crossplatformId, pageSize);
            PageSize = pageSize;
            Cursor = cursor;
        }

        public string CrossplatformId { get; }
        public int PageSize { get; }
        public PlayerEvidenceCursor? Cursor { get; }
    }

    public sealed class PlayerSkillSnapshotsQuery
    {
        public const int MaximumPageSize = 200;

        public PlayerSkillSnapshotsQuery(
            string crossplatformId,
            int pageSize,
            PlayerEvidenceCursor? cursor)
        {
            CrossplatformId = PlayerEvidenceQueryValidation.Require(crossplatformId, pageSize);
            PageSize = pageSize;
            Cursor = cursor;
        }

        public string CrossplatformId { get; }
        public int PageSize { get; }
        public PlayerEvidenceCursor? Cursor { get; }
    }

    public sealed class PlayerEvidenceRangeQuery
    {
        public const int MaximumResultCount = 5000;

        public PlayerEvidenceRangeQuery(
            string crossplatformId,
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            int maximumResultCount)
        {
            if (maximumResultCount < 1 || maximumResultCount > MaximumResultCount)
                throw new ArgumentOutOfRangeException(nameof(maximumResultCount));

            CrossplatformId = PlayerEvidenceValidation.RequireText(crossplatformId, nameof(crossplatformId));
            FromUtc = PlayerEvidenceValidation.RequireUtc(fromUtc, nameof(fromUtc));
            ToUtc = PlayerEvidenceValidation.RequireUtc(toUtc, nameof(toUtc));
            if (ToUtc < FromUtc) throw new ArgumentOutOfRangeException(nameof(toUtc));
            MaximumResults = maximumResultCount;
        }

        public string CrossplatformId { get; }
        public DateTimeOffset FromUtc { get; }
        public DateTimeOffset ToUtc { get; }
        public int MaximumResults { get; }
    }

    public sealed class PlayerInventorySnapshotsPage
    {
        public PlayerInventorySnapshotsPage(
            IEnumerable<PlayerInventorySnapshot> snapshots,
            PlayerEvidenceCursor? nextCursor,
            IEnumerable<PlayerEvidenceGap> gaps)
        {
            Snapshots = PlayerEvidenceValidation.Copy(snapshots, nameof(snapshots));
            NextCursor = nextCursor;
            Gaps = PlayerEvidenceValidation.Copy(gaps, nameof(gaps));
        }

        public IReadOnlyList<PlayerInventorySnapshot> Snapshots { get; }
        public PlayerEvidenceCursor? NextCursor { get; }
        public IReadOnlyList<PlayerEvidenceGap> Gaps { get; }
    }

    public sealed class PlayerSkillSnapshotsPage
    {
        public PlayerSkillSnapshotsPage(
            IEnumerable<PlayerSkillSnapshot> snapshots,
            PlayerEvidenceCursor? nextCursor,
            IEnumerable<PlayerEvidenceGap> gaps)
        {
            Snapshots = PlayerEvidenceValidation.Copy(snapshots, nameof(snapshots));
            NextCursor = nextCursor;
            Gaps = PlayerEvidenceValidation.Copy(gaps, nameof(gaps));
        }

        public IReadOnlyList<PlayerSkillSnapshot> Snapshots { get; }
        public PlayerEvidenceCursor? NextCursor { get; }
        public IReadOnlyList<PlayerEvidenceGap> Gaps { get; }
    }

    public sealed class PlayerInventoryDiffsPage
    {
        public PlayerInventoryDiffsPage(
            IEnumerable<PlayerInventoryDiff> diffs,
            PlayerEvidenceCursor? nextCursor)
        {
            Diffs = PlayerEvidenceValidation.Copy(diffs, nameof(diffs));
            NextCursor = nextCursor;
        }

        public IReadOnlyList<PlayerInventoryDiff> Diffs { get; }
        public PlayerEvidenceCursor? NextCursor { get; }
    }

    public sealed class PlayerEvidenceCompactionRequest
    {
        public PlayerEvidenceCompactionRequest(
            DateTimeOffset retainAfterUtc,
            TimeSpan bucketSize)
        {
            if (bucketSize <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(bucketSize));
            RetainAfterUtc = PlayerEvidenceValidation.RequireUtc(retainAfterUtc, nameof(retainAfterUtc));
            BucketSize = bucketSize;
        }

        public DateTimeOffset RetainAfterUtc { get; }
        public TimeSpan BucketSize { get; }
    }

    internal static class PlayerEvidenceQueryValidation
    {
        public static string Require(string crossplatformId, int pageSize)
        {
            if (pageSize < 1 || pageSize > PlayerInventorySnapshotsQuery.MaximumPageSize)
                throw new ArgumentOutOfRangeException(nameof(pageSize));
            return PlayerEvidenceValidation.RequireText(crossplatformId, nameof(crossplatformId));
        }
    }
}
