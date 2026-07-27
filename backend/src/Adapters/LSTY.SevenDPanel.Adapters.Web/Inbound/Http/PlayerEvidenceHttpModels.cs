using System;
using System.Collections.Generic;
using System.Linq;
using LSTY.SevenDPanel.Application;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    public sealed class PlayerEvidenceGapHttpResponse
    {
        public PlayerEvidenceGapHttpResponse(PlayerEvidenceGap value)
        {
            GapId = value.GapId;
            CrossplatformId = value.CrossplatformId;
            StartedAtUtc = value.StartedAtUtc;
            EndedAtUtc = value.EndedAtUtc;
            Reason = value.Reason;
            EstimatedLostCount = value.EstimatedLostCount;
        }

        public long GapId { get; }
        public string CrossplatformId { get; }
        public DateTimeOffset StartedAtUtc { get; }
        public DateTimeOffset EndedAtUtc { get; }
        public string Reason { get; }
        public long EstimatedLostCount { get; }
    }

    public sealed class PlayerPositionHttpResponse
    {
        public PlayerPositionHttpResponse(PlayerPosition value)
        {
            X = value.X;
            Y = value.Y;
            Z = value.Z;
        }

        public float X { get; }
        public float Y { get; }
        public float Z { get; }
    }

    public sealed class HistoricalPlayerSummaryHttpResponse
    {
        public HistoricalPlayerSummaryHttpResponse(HistoricalPlayerSummary value)
        {
            CrossplatformId = value.CrossplatformId;
            LatestName = value.LatestName;
            FirstObservedAtUtc = value.FirstObservedAtUtc;
            LastObservedAtUtc = value.LastObservedAtUtc;
            TotalObservationCount = value.TotalObservationCount;
            RetainedSnapshotCount = value.RetainedSnapshotCount;
            CompactedSnapshotCount = value.CompactedSnapshotCount;
            HasGaps = value.HasGaps;
        }

        public string CrossplatformId { get; }
        public string LatestName { get; }
        public DateTimeOffset FirstObservedAtUtc { get; }
        public DateTimeOffset LastObservedAtUtc { get; }
        public long TotalObservationCount { get; }
        public long RetainedSnapshotCount { get; }
        public long CompactedSnapshotCount { get; }
        public bool HasGaps { get; }
    }

    public sealed class PlayerSessionHttpResponse
    {
        public PlayerSessionHttpResponse(PlayerSession value)
        {
            SessionId = value.SessionId;
            CrossplatformId = value.CrossplatformId;
            ServerId = value.ServerId;
            WorldId = value.WorldId;
            StartedAtUtc = value.StartedAtUtc;
            EndedAtUtc = value.EndedAtUtc;
            EndReason = value.EndReason;
            LastPosition = value.LastPosition == null
                ? null
                : new PlayerPositionHttpResponse(value.LastPosition.Value);
            Completeness = value.Completeness;
        }

        public long SessionId { get; }
        public string CrossplatformId { get; }
        public string ServerId { get; }
        public string WorldId { get; }
        public DateTimeOffset StartedAtUtc { get; }
        public DateTimeOffset? EndedAtUtc { get; }
        public string? EndReason { get; }
        public PlayerPositionHttpResponse? LastPosition { get; }
        [JsonConverter(typeof(StringEnumConverter))]
        public PlayerProfileSectionState Completeness { get; }
    }

    public sealed class PlayerActivityEventHttpResponse
    {
        public PlayerActivityEventHttpResponse(PlayerActivityEvent value)
        {
            ActivityId = value.ActivityId;
            CrossplatformId = value.CrossplatformId;
            ServerId = value.ServerId;
            WorldId = value.WorldId;
            Kind = value.Kind;
            ObservedAtUtc = value.ObservedAtUtc;
            CorrelationId = value.CorrelationId;
            Completeness = value.Completeness;
        }

        public long ActivityId { get; }
        public string CrossplatformId { get; }
        public string ServerId { get; }
        public string WorldId { get; }
        public string Kind { get; }
        public DateTimeOffset ObservedAtUtc { get; }
        public string? CorrelationId { get; }
        [JsonConverter(typeof(StringEnumConverter))]
        public PlayerProfileSectionState Completeness { get; }
    }

    public sealed class PlayerDailyActivityHttpResponse
    {
        public PlayerDailyActivityHttpResponse(PlayerDailyActivitySummary value)
        {
            LocalDate = value.LocalDate;
            SessionCount = value.SessionCount;
            LoginCount = value.LoginCount;
            ChatMessageCount = value.ChatMessageCount;
            DeathCount = value.DeathCount;
            KillCount = value.KillCount;
            InventoryObservationCount = value.InventoryObservationCount;
        }

        public string LocalDate { get; }
        public int? SessionCount { get; }
        public int? LoginCount { get; }
        public int? ChatMessageCount { get; }
        public int? DeathCount { get; }
        public int? KillCount { get; }
        public int? InventoryObservationCount { get; }
    }

    public sealed class InventoryItemScalarHttpResponse
    {
        public InventoryItemScalarHttpResponse(InventoryItemScalar value)
        {
            Container = value.Container;
            Slot = value.Slot;
            InternalName = value.InternalName;
            Count = value.Count;
            Quality = value.Quality;
            UseAmount = value.UseAmount;
            ModInternalNames = value.ModInternalNames;
        }

        public string Container { get; }
        public int Slot { get; }
        public string InternalName { get; }
        public int Count { get; }
        public int? Quality { get; }
        public decimal? UseAmount { get; }
        public IReadOnlyList<string> ModInternalNames { get; }
    }

    public sealed class PlayerInventorySnapshotHttpResponse
    {
        public PlayerInventorySnapshotHttpResponse(PlayerInventorySnapshot value)
        {
            SnapshotId = value.SnapshotId;
            CrossplatformId = value.CrossplatformId;
            ServerId = value.ServerId;
            WorldId = value.WorldId;
            ObservedAtUtc = value.ObservedAtUtc;
            GameVersion = value.GameVersion;
            CatalogVersion = value.CatalogVersion;
            CatalogResolution = value.CatalogResolution;
            Fingerprint = value.Fingerprint;
            AdminBoundary = value.AdminBoundary;
            Items = value.Items.Select(item => new InventoryItemScalarHttpResponse(item)).ToArray();
        }

        public long SnapshotId { get; }
        public string CrossplatformId { get; }
        public string ServerId { get; }
        public string WorldId { get; }
        public DateTimeOffset ObservedAtUtc { get; }
        public string GameVersion { get; }
        public string? CatalogVersion { get; }
        [JsonConverter(typeof(StringEnumConverter))]
        public CatalogResolutionState CatalogResolution { get; }
        public string Fingerprint { get; }
        public bool AdminBoundary { get; }
        public IReadOnlyList<InventoryItemScalarHttpResponse> Items { get; }
    }

    public sealed class PlayerSkillValueHttpResponse
    {
        public PlayerSkillValueHttpResponse(PlayerSkillValue value)
        {
            SkillKey = value.SkillKey;
            State = value.State;
            Value = value.Value;
            Minimum = value.Minimum;
            Maximum = value.Maximum;
            NextLevelCost = value.NextLevelCost;
            ParentKey = value.ParentKey;
        }

        public string SkillKey { get; }
        [JsonConverter(typeof(StringEnumConverter))]
        public SkillValueState State { get; }
        public int? Value { get; }
        public int? Minimum { get; }
        public int? Maximum { get; }
        public int? NextLevelCost { get; }
        public string? ParentKey { get; }
    }

    public sealed class PlayerSkillSnapshotHttpResponse
    {
        public PlayerSkillSnapshotHttpResponse(PlayerSkillSnapshot value)
        {
            SnapshotId = value.SnapshotId;
            CrossplatformId = value.CrossplatformId;
            ServerId = value.ServerId;
            WorldId = value.WorldId;
            ObservedAtUtc = value.ObservedAtUtc;
            GameVersion = value.GameVersion;
            Level = value.Level;
            SkillPoints = value.SkillPoints;
            Values = value.Values.Select(item => new PlayerSkillValueHttpResponse(item)).ToArray();
        }

        public long SnapshotId { get; }
        public string CrossplatformId { get; }
        public string ServerId { get; }
        public string WorldId { get; }
        public DateTimeOffset ObservedAtUtc { get; }
        public string GameVersion { get; }
        public int? Level { get; }
        public int? SkillPoints { get; }
        public IReadOnlyList<PlayerSkillValueHttpResponse> Values { get; }
    }

    public sealed class PlayerInventoryDiffEntryHttpResponse
    {
        public PlayerInventoryDiffEntryHttpResponse(PlayerInventoryDiffEntry value)
        {
            Kind = value.Kind;
            PreviousItem = value.PreviousItem == null
                ? null
                : new InventoryItemScalarHttpResponse(value.PreviousItem);
            CurrentItem = value.CurrentItem == null
                ? null
                : new InventoryItemScalarHttpResponse(value.CurrentItem);
            EvidenceLevel = value.EvidenceLevel;
            SourceOperationIds = value.SourceOperationIds;
        }

        [JsonConverter(typeof(StringEnumConverter))]
        public InventoryDiffKind Kind { get; }
        public InventoryItemScalarHttpResponse? PreviousItem { get; }
        public InventoryItemScalarHttpResponse? CurrentItem { get; }
        [JsonConverter(typeof(StringEnumConverter))]
        public EvidenceLevel EvidenceLevel { get; }
        public IReadOnlyList<string> SourceOperationIds { get; }
    }

    public sealed class PlayerInventoryDiffHttpResponse
    {
        public PlayerInventoryDiffHttpResponse(PlayerInventoryDiff value)
        {
            PreviousSnapshotId = value.PreviousSnapshotId;
            CurrentSnapshotId = value.CurrentSnapshotId;
            PreviousObservedAtUtc = value.PreviousObservedAtUtc;
            CurrentObservedAtUtc = value.CurrentObservedAtUtc;
            IsComplete = value.IsComplete;
            Changes = value.Changes
                .Select(change => new PlayerInventoryDiffEntryHttpResponse(change))
                .ToArray();
        }

        public long? PreviousSnapshotId { get; }
        public long CurrentSnapshotId { get; }
        public DateTimeOffset? PreviousObservedAtUtc { get; }
        public DateTimeOffset CurrentObservedAtUtc { get; }
        public bool IsComplete { get; }
        public IReadOnlyList<PlayerInventoryDiffEntryHttpResponse> Changes { get; }
    }

    public sealed class PlayerEvidenceSectionHttpResponse<T>
    {
        public PlayerEvidenceSectionHttpResponse(
            PlayerProfileSectionState state,
            DateTimeOffset? observedAtUtc,
            T? value,
            IEnumerable<PlayerEvidenceGap> gaps)
        {
            State = state;
            ObservedAtUtc = observedAtUtc;
            Value = value;
            GapMetadata = gaps.Select(gap => new PlayerEvidenceGapHttpResponse(gap)).ToArray();
        }

        [JsonConverter(typeof(StringEnumConverter))]
        public PlayerProfileSectionState State { get; }
        public DateTimeOffset? ObservedAtUtc { get; }
        public T? Value { get; }
        public IReadOnlyList<PlayerEvidenceGapHttpResponse> GapMetadata { get; }
    }

    public sealed class PlayerProfileHttpResponse
    {
        public PlayerProfileHttpResponse(PlayerProfile value)
        {
            CrossplatformId = value.CrossplatformId;
            Summary = Section(value.Summary, item => new HistoricalPlayerSummaryHttpResponse(item));
            Sessions = Section(value.Sessions, items =>
                (IReadOnlyList<PlayerSessionHttpResponse>)items
                    .Select(item => new PlayerSessionHttpResponse(item)).ToArray());
            Activity = Section(value.Activity, items =>
                (IReadOnlyList<PlayerActivityEventHttpResponse>)items
                    .Select(item => new PlayerActivityEventHttpResponse(item)).ToArray());
            Inventory = Section(value.Inventory, item => new PlayerInventorySnapshotHttpResponse(item));
            Skills = Section(value.Skills, item => new PlayerSkillSnapshotHttpResponse(item));
            DailyActivity = Section(value.DailyActivity, items =>
                (IReadOnlyList<PlayerDailyActivityHttpResponse>)items
                    .Select(item => new PlayerDailyActivityHttpResponse(item)).ToArray());
        }

        public string CrossplatformId { get; }
        public PlayerEvidenceSectionHttpResponse<HistoricalPlayerSummaryHttpResponse> Summary { get; }
        public PlayerEvidenceSectionHttpResponse<IReadOnlyList<PlayerSessionHttpResponse>> Sessions { get; }
        public PlayerEvidenceSectionHttpResponse<IReadOnlyList<PlayerActivityEventHttpResponse>> Activity { get; }
        public PlayerEvidenceSectionHttpResponse<PlayerInventorySnapshotHttpResponse> Inventory { get; }
        public PlayerEvidenceSectionHttpResponse<PlayerSkillSnapshotHttpResponse> Skills { get; }
        public PlayerEvidenceSectionHttpResponse<IReadOnlyList<PlayerDailyActivityHttpResponse>> DailyActivity { get; }

        private static PlayerEvidenceSectionHttpResponse<TResponse> Section<TSource, TResponse>(
            PlayerProfileSection<TSource> section,
            Func<TSource, TResponse> map) =>
            new PlayerEvidenceSectionHttpResponse<TResponse>(
                section.State,
                section.ObservedAtUtc,
                section.Value == null ? default : map(section.Value),
                section.GapMetadata);
    }

    public sealed class PlayerInventorySnapshotsPageHttpResponse
    {
        public PlayerInventorySnapshotsPageHttpResponse(
            string crossplatformId,
            PlayerProfileSection<PlayerInventorySnapshotsPage> section)
        {
            State = section.State;
            ObservedAtUtc = section.ObservedAtUtc;
            Snapshots = section.Value?.Snapshots
                .Select(value => new PlayerInventorySnapshotHttpResponse(value)).ToArray()
                ?? Array.Empty<PlayerInventorySnapshotHttpResponse>();
            NextCursor = section.Value?.NextCursor == null
                ? null
                : PlayerEvidenceCursorCodec.Encode(crossplatformId, section.Value.NextCursor);
            GapMetadata = section.GapMetadata
                .Select(gap => new PlayerEvidenceGapHttpResponse(gap)).ToArray();
        }

        [JsonConverter(typeof(StringEnumConverter))]
        public PlayerProfileSectionState State { get; }
        public DateTimeOffset? ObservedAtUtc { get; }
        public IReadOnlyList<PlayerInventorySnapshotHttpResponse> Snapshots { get; }
        public string? NextCursor { get; }
        public IReadOnlyList<PlayerEvidenceGapHttpResponse> GapMetadata { get; }
    }

    public sealed class PlayerInventoryDiffsPageHttpResponse
    {
        public PlayerInventoryDiffsPageHttpResponse(
            string crossplatformId,
            PlayerProfileSection<PlayerInventoryDiffsPage> section)
        {
            State = section.State;
            ObservedAtUtc = section.ObservedAtUtc;
            Diffs = section.Value?.Diffs
                .Select(value => new PlayerInventoryDiffHttpResponse(value)).ToArray()
                ?? Array.Empty<PlayerInventoryDiffHttpResponse>();
            NextCursor = section.Value?.NextCursor == null
                ? null
                : PlayerEvidenceCursorCodec.Encode(crossplatformId, section.Value.NextCursor);
            GapMetadata = section.GapMetadata
                .Select(gap => new PlayerEvidenceGapHttpResponse(gap)).ToArray();
        }

        [JsonConverter(typeof(StringEnumConverter))]
        public PlayerProfileSectionState State { get; }
        public DateTimeOffset? ObservedAtUtc { get; }
        public IReadOnlyList<PlayerInventoryDiffHttpResponse> Diffs { get; }
        public string? NextCursor { get; }
        public IReadOnlyList<PlayerEvidenceGapHttpResponse> GapMetadata { get; }
    }

    public sealed class PlayerSkillsPageHttpResponse
    {
        public PlayerSkillsPageHttpResponse(
            string crossplatformId,
            PlayerProfileSection<PlayerSkillSnapshotsPage> section)
        {
            State = section.State;
            ObservedAtUtc = section.ObservedAtUtc;
            Snapshots = section.Value?.Snapshots
                .Select(value => new PlayerSkillSnapshotHttpResponse(value)).ToArray()
                ?? Array.Empty<PlayerSkillSnapshotHttpResponse>();
            NextCursor = section.Value?.NextCursor == null
                ? null
                : PlayerEvidenceCursorCodec.Encode(crossplatformId, section.Value.NextCursor);
            GapMetadata = section.GapMetadata
                .Select(gap => new PlayerEvidenceGapHttpResponse(gap)).ToArray();
        }

        [JsonConverter(typeof(StringEnumConverter))]
        public PlayerProfileSectionState State { get; }
        public DateTimeOffset? ObservedAtUtc { get; }
        public IReadOnlyList<PlayerSkillSnapshotHttpResponse> Snapshots { get; }
        public string? NextCursor { get; }
        public IReadOnlyList<PlayerEvidenceGapHttpResponse> GapMetadata { get; }
    }
}
