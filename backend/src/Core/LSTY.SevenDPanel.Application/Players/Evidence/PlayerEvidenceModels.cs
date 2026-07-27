using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace LSTY.SevenDPanel.Application
{
    public enum PlayerProfileSectionState
    {
        Available,
        Partial,
        Unavailable,
        Forbidden
    }

    public enum CatalogResolutionState
    {
        Resolved,
        Unavailable
    }

    public enum SkillValueState
    {
        Known,
        UnsupportedByVersion,
        NotLoaded,
        Unknown
    }

    public enum InventoryDiffKind
    {
        Added,
        Removed,
        QuantityChanged,
        Moved,
        AttributesChanged,
        Uncomparable
    }

    public enum EvidenceLevel
    {
        Confirmed,
        ObservedChange
    }

    public sealed class PlayerProfileSection<T>
    {
        public PlayerProfileSection(
            PlayerProfileSectionState state,
            DateTimeOffset? observedAtUtc,
            T? value,
            IEnumerable<PlayerEvidenceGap> gapMetadata)
        {
            PlayerEvidenceValidation.RequireDefined(state, nameof(state));
            if (observedAtUtc.HasValue)
                PlayerEvidenceValidation.RequireUtc(observedAtUtc.Value, nameof(observedAtUtc));

            State = state;
            ObservedAtUtc = observedAtUtc;
            Value = value;
            GapMetadata = PlayerEvidenceValidation.Copy(gapMetadata, nameof(gapMetadata));
        }

        public PlayerProfileSectionState State { get; }

        public DateTimeOffset? ObservedAtUtc { get; }

        public T? Value { get; }

        public IReadOnlyList<PlayerEvidenceGap> GapMetadata { get; }
    }

    public sealed class PlayerProfile
    {
        public PlayerProfile(
            string crossplatformId,
            PlayerProfileSection<HistoricalPlayerSummary> summary,
            PlayerProfileSection<IReadOnlyList<PlayerSession>> sessions,
            PlayerProfileSection<IReadOnlyList<PlayerActivityEvent>> activity,
            PlayerProfileSection<PlayerInventorySnapshot> inventory,
            PlayerProfileSection<PlayerSkillSnapshot> skills,
            PlayerProfileSection<IReadOnlyList<PlayerDailyActivitySummary>> dailyActivity)
        {
            CrossplatformId = PlayerEvidenceValidation.RequireText(
                crossplatformId,
                nameof(crossplatformId));
            Summary = summary ?? throw new ArgumentNullException(nameof(summary));
            Sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            Activity = activity ?? throw new ArgumentNullException(nameof(activity));
            Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            Skills = skills ?? throw new ArgumentNullException(nameof(skills));
            DailyActivity = dailyActivity ?? throw new ArgumentNullException(nameof(dailyActivity));
        }

        public string CrossplatformId { get; }

        public PlayerProfileSection<HistoricalPlayerSummary> Summary { get; }

        public PlayerProfileSection<IReadOnlyList<PlayerSession>> Sessions { get; }

        public PlayerProfileSection<IReadOnlyList<PlayerActivityEvent>> Activity { get; }

        public PlayerProfileSection<PlayerInventorySnapshot> Inventory { get; }

        public PlayerProfileSection<PlayerSkillSnapshot> Skills { get; }

        public PlayerProfileSection<IReadOnlyList<PlayerDailyActivitySummary>> DailyActivity { get; }
    }

    public sealed class PlayerSession
    {
        public PlayerSession(
            long sessionId,
            string crossplatformId,
            string serverId,
            string worldId,
            DateTimeOffset startedAtUtc,
            DateTimeOffset? endedAtUtc,
            string? endReason,
            PlayerPosition? lastPosition,
            PlayerProfileSectionState completeness)
        {
            if (sessionId <= 0) throw new ArgumentOutOfRangeException(nameof(sessionId));
            PlayerEvidenceValidation.RequireDefined(completeness, nameof(completeness));

            SessionId = sessionId;
            CrossplatformId = PlayerEvidenceValidation.RequireText(crossplatformId, nameof(crossplatformId));
            ServerId = PlayerEvidenceValidation.RequireText(serverId, nameof(serverId));
            WorldId = PlayerEvidenceValidation.RequireText(worldId, nameof(worldId));
            StartedAtUtc = PlayerEvidenceValidation.RequireUtc(startedAtUtc, nameof(startedAtUtc));
            if (endedAtUtc.HasValue)
            {
                EndedAtUtc = PlayerEvidenceValidation.RequireUtc(endedAtUtc.Value, nameof(endedAtUtc));
                if (EndedAtUtc.Value < StartedAtUtc)
                    throw new ArgumentOutOfRangeException(nameof(endedAtUtc));
            }

            EndReason = PlayerEvidenceValidation.OptionalText(endReason, nameof(endReason));
            LastPosition = lastPosition;
            Completeness = completeness;
        }

        public long SessionId { get; }
        public string CrossplatformId { get; }
        public string ServerId { get; }
        public string WorldId { get; }
        public DateTimeOffset StartedAtUtc { get; }
        public DateTimeOffset? EndedAtUtc { get; }
        public string? EndReason { get; }
        public PlayerPosition? LastPosition { get; }
        public PlayerProfileSectionState Completeness { get; }
    }

    public sealed class PlayerActivityEvent
    {
        public PlayerActivityEvent(
            long activityId,
            string crossplatformId,
            string serverId,
            string worldId,
            string kind,
            DateTimeOffset observedAtUtc,
            string? correlationId,
            PlayerProfileSectionState completeness)
        {
            if (activityId <= 0) throw new ArgumentOutOfRangeException(nameof(activityId));
            PlayerEvidenceValidation.RequireDefined(completeness, nameof(completeness));

            ActivityId = activityId;
            CrossplatformId = PlayerEvidenceValidation.RequireText(crossplatformId, nameof(crossplatformId));
            ServerId = PlayerEvidenceValidation.RequireText(serverId, nameof(serverId));
            WorldId = PlayerEvidenceValidation.RequireText(worldId, nameof(worldId));
            Kind = PlayerEvidenceValidation.RequireText(kind, nameof(kind));
            ObservedAtUtc = PlayerEvidenceValidation.RequireUtc(observedAtUtc, nameof(observedAtUtc));
            CorrelationId = PlayerEvidenceValidation.OptionalText(correlationId, nameof(correlationId));
            Completeness = completeness;
        }

        public long ActivityId { get; }
        public string CrossplatformId { get; }
        public string ServerId { get; }
        public string WorldId { get; }
        public string Kind { get; }
        public DateTimeOffset ObservedAtUtc { get; }
        public string? CorrelationId { get; }
        public PlayerProfileSectionState Completeness { get; }
    }

    public sealed class PlayerDailyActivitySummary
    {
        public PlayerDailyActivitySummary(
            string localDate,
            int? sessionCount,
            int? loginCount,
            int? chatMessageCount,
            int? deathCount,
            int? killCount,
            int? inventoryObservationCount)
        {
            if (!DateTime.TryParseExact(
                    localDate,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out _))
                throw new ArgumentException("A yyyy-MM-dd local date is required.", nameof(localDate));

            RequireNonNegative(sessionCount, nameof(sessionCount));
            RequireNonNegative(loginCount, nameof(loginCount));
            RequireNonNegative(chatMessageCount, nameof(chatMessageCount));
            RequireNonNegative(deathCount, nameof(deathCount));
            RequireNonNegative(killCount, nameof(killCount));
            RequireNonNegative(inventoryObservationCount, nameof(inventoryObservationCount));

            LocalDate = localDate;
            SessionCount = sessionCount;
            LoginCount = loginCount;
            ChatMessageCount = chatMessageCount;
            DeathCount = deathCount;
            KillCount = killCount;
            InventoryObservationCount = inventoryObservationCount;
        }

        public string LocalDate { get; }
        public int? SessionCount { get; }
        public int? LoginCount { get; }
        public int? ChatMessageCount { get; }
        public int? DeathCount { get; }
        public int? KillCount { get; }
        public int? InventoryObservationCount { get; }

        private static void RequireNonNegative(int? value, string parameterName)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    public sealed class PlayerEvidenceGap
    {
        public PlayerEvidenceGap(
            long gapId,
            string crossplatformId,
            DateTimeOffset startedAtUtc,
            DateTimeOffset endedAtUtc,
            string reason,
            long estimatedLostCount)
        {
            if (gapId <= 0) throw new ArgumentOutOfRangeException(nameof(gapId));
            if (estimatedLostCount <= 0) throw new ArgumentOutOfRangeException(nameof(estimatedLostCount));

            GapId = gapId;
            CrossplatformId = PlayerEvidenceValidation.RequireText(crossplatformId, nameof(crossplatformId));
            StartedAtUtc = PlayerEvidenceValidation.RequireUtc(startedAtUtc, nameof(startedAtUtc));
            EndedAtUtc = PlayerEvidenceValidation.RequireUtc(endedAtUtc, nameof(endedAtUtc));
            if (EndedAtUtc < StartedAtUtc) throw new ArgumentOutOfRangeException(nameof(endedAtUtc));
            Reason = PlayerEvidenceValidation.RequireText(reason, nameof(reason));
            EstimatedLostCount = estimatedLostCount;
        }

        public long GapId { get; }
        public string CrossplatformId { get; }
        public DateTimeOffset StartedAtUtc { get; }
        public DateTimeOffset EndedAtUtc { get; }
        public string Reason { get; }
        public long EstimatedLostCount { get; }
    }

    public sealed record InventoryItemScalar
    {
        public InventoryItemScalar(
            string container,
            int slot,
            string internalName,
            int count,
            int? quality,
            decimal? useAmount,
            IEnumerable<string> modInternalNames)
        {
            if (slot < 0) throw new ArgumentOutOfRangeException(nameof(slot));
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (quality < 0) throw new ArgumentOutOfRangeException(nameof(quality));
            if (useAmount < 0) throw new ArgumentOutOfRangeException(nameof(useAmount));

            Container = PlayerEvidenceValidation.RequireText(container, nameof(container));
            Slot = slot;
            InternalName = PlayerEvidenceValidation.RequireText(internalName, nameof(internalName));
            Count = count;
            Quality = quality;
            UseAmount = useAmount;
            ModInternalNames = PlayerEvidenceValidation.CopyText(
                modInternalNames,
                nameof(modInternalNames));
        }

        public string Container { get; }
        public int Slot { get; }
        public string InternalName { get; }
        public int Count { get; }
        public int? Quality { get; }
        public decimal? UseAmount { get; }
        public IReadOnlyList<string> ModInternalNames { get; }
    }

    public sealed class PlayerInventorySnapshot
    {
        public PlayerInventorySnapshot(
            long snapshotId,
            string crossplatformId,
            string serverId,
            string worldId,
            DateTimeOffset observedAtUtc,
            string gameVersion,
            string? catalogVersion,
            CatalogResolutionState catalogResolution,
            string fingerprint,
            bool adminBoundary,
            IEnumerable<InventoryItemScalar> items)
        {
            if (snapshotId <= 0) throw new ArgumentOutOfRangeException(nameof(snapshotId));
            PlayerEvidenceValidation.RequireDefined(catalogResolution, nameof(catalogResolution));

            SnapshotId = snapshotId;
            CrossplatformId = PlayerEvidenceValidation.RequireText(crossplatformId, nameof(crossplatformId));
            ServerId = PlayerEvidenceValidation.RequireText(serverId, nameof(serverId));
            WorldId = PlayerEvidenceValidation.RequireText(worldId, nameof(worldId));
            ObservedAtUtc = PlayerEvidenceValidation.RequireUtc(observedAtUtc, nameof(observedAtUtc));
            GameVersion = PlayerEvidenceValidation.RequireText(gameVersion, nameof(gameVersion));
            CatalogVersion = PlayerEvidenceValidation.OptionalText(catalogVersion, nameof(catalogVersion));
            if (catalogResolution == CatalogResolutionState.Resolved && CatalogVersion == null)
                throw new ArgumentException("A resolved inventory requires a catalog version.", nameof(catalogVersion));
            CatalogResolution = catalogResolution;
            Fingerprint = PlayerEvidenceValidation.RequireText(fingerprint, nameof(fingerprint));
            AdminBoundary = adminBoundary;
            Items = PlayerEvidenceValidation.Copy(items, nameof(items));

            var locations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in Items)
            {
                if (item == null) throw new ArgumentException("Inventory items cannot contain null.", nameof(items));
                var location = item.Container + "\u001f" + item.Slot.ToString(CultureInfo.InvariantCulture);
                if (!locations.Add(location))
                    throw new ArgumentException("Inventory container and slot locations must be unique.", nameof(items));
            }
        }

        public long SnapshotId { get; }
        public string CrossplatformId { get; }
        public string ServerId { get; }
        public string WorldId { get; }
        public DateTimeOffset ObservedAtUtc { get; }
        public string GameVersion { get; }
        public string? CatalogVersion { get; }
        public CatalogResolutionState CatalogResolution { get; }
        public string Fingerprint { get; }
        public bool AdminBoundary { get; }
        public IReadOnlyList<InventoryItemScalar> Items { get; }
    }

    public sealed class PlayerSkillValue
    {
        public PlayerSkillValue(
            string skillKey,
            SkillValueState state,
            int? value,
            int? minimum,
            int? maximum,
            int? nextLevelCost,
            string? parentKey)
        {
            PlayerEvidenceValidation.RequireDefined(state, nameof(state));
            if (state == SkillValueState.Known && !value.HasValue)
                throw new ArgumentException("A known skill requires a value.", nameof(value));
            if (state != SkillValueState.Known && value.HasValue)
                throw new ArgumentException("An unavailable skill value must remain null.", nameof(value));
            if (minimum.HasValue && maximum.HasValue && maximum.Value < minimum.Value)
                throw new ArgumentOutOfRangeException(nameof(maximum));

            SkillKey = PlayerEvidenceValidation.RequireText(skillKey, nameof(skillKey));
            State = state;
            Value = value;
            Minimum = minimum;
            Maximum = maximum;
            NextLevelCost = nextLevelCost;
            ParentKey = PlayerEvidenceValidation.OptionalText(parentKey, nameof(parentKey));
        }

        public string SkillKey { get; }
        public SkillValueState State { get; }
        public int? Value { get; }
        public int? Minimum { get; }
        public int? Maximum { get; }
        public int? NextLevelCost { get; }
        public string? ParentKey { get; }
    }

    public sealed class PlayerSkillSnapshot
    {
        public PlayerSkillSnapshot(
            long snapshotId,
            string crossplatformId,
            string serverId,
            string worldId,
            DateTimeOffset observedAtUtc,
            string gameVersion,
            int? level,
            int? skillPoints,
            IEnumerable<PlayerSkillValue> values)
        {
            if (snapshotId <= 0) throw new ArgumentOutOfRangeException(nameof(snapshotId));
            if (level < 0) throw new ArgumentOutOfRangeException(nameof(level));
            if (skillPoints < 0) throw new ArgumentOutOfRangeException(nameof(skillPoints));

            SnapshotId = snapshotId;
            CrossplatformId = PlayerEvidenceValidation.RequireText(crossplatformId, nameof(crossplatformId));
            ServerId = PlayerEvidenceValidation.RequireText(serverId, nameof(serverId));
            WorldId = PlayerEvidenceValidation.RequireText(worldId, nameof(worldId));
            ObservedAtUtc = PlayerEvidenceValidation.RequireUtc(observedAtUtc, nameof(observedAtUtc));
            GameVersion = PlayerEvidenceValidation.RequireText(gameVersion, nameof(gameVersion));
            Level = level;
            SkillPoints = skillPoints;
            Values = PlayerEvidenceValidation.Copy(values, nameof(values));

            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in Values)
            {
                if (value == null) throw new ArgumentException("Skill values cannot contain null.", nameof(values));
                if (!keys.Add(value.SkillKey))
                    throw new ArgumentException("Skill keys must be unique within a snapshot.", nameof(values));
            }
        }

        public long SnapshotId { get; }
        public string CrossplatformId { get; }
        public string ServerId { get; }
        public string WorldId { get; }
        public DateTimeOffset ObservedAtUtc { get; }
        public string GameVersion { get; }
        public int? Level { get; }
        public int? SkillPoints { get; }
        public IReadOnlyList<PlayerSkillValue> Values { get; }
    }

    public sealed class PlayerInventoryDiffEntry
    {
        public PlayerInventoryDiffEntry(
            InventoryDiffKind kind,
            InventoryItemScalar? previousItem,
            InventoryItemScalar? currentItem,
            EvidenceLevel evidenceLevel,
            IEnumerable<string> sourceOperationIds)
        {
            PlayerEvidenceValidation.RequireDefined(kind, nameof(kind));
            PlayerEvidenceValidation.RequireDefined(evidenceLevel, nameof(evidenceLevel));
            var sourceIds = PlayerEvidenceValidation.CopyText(sourceOperationIds, nameof(sourceOperationIds));

            if (kind == InventoryDiffKind.Uncomparable && (previousItem != null || currentItem != null))
                throw new ArgumentException("An uncomparable diff cannot assert item details.", nameof(kind));
            if (kind == InventoryDiffKind.Added && (previousItem != null || currentItem == null))
                throw new ArgumentException("An added diff requires only the current item.", nameof(kind));
            if (kind == InventoryDiffKind.Removed && (previousItem == null || currentItem != null))
                throw new ArgumentException("A removed diff requires only the previous item.", nameof(kind));
            if (kind != InventoryDiffKind.Added &&
                kind != InventoryDiffKind.Removed &&
                kind != InventoryDiffKind.Uncomparable &&
                (previousItem == null || currentItem == null))
                throw new ArgumentException("A comparable item mutation requires both observations.", nameof(kind));
            if (evidenceLevel == EvidenceLevel.Confirmed && sourceIds.Count == 0)
                throw new ArgumentException("Confirmed evidence requires an operation source.", nameof(sourceOperationIds));
            if (evidenceLevel == EvidenceLevel.ObservedChange && sourceIds.Count != 0)
                throw new ArgumentException("Observed changes cannot claim confirmed operation sources.", nameof(sourceOperationIds));

            Kind = kind;
            PreviousItem = previousItem;
            CurrentItem = currentItem;
            EvidenceLevel = evidenceLevel;
            SourceOperationIds = sourceIds;
        }

        public InventoryDiffKind Kind { get; }
        public InventoryItemScalar? PreviousItem { get; }
        public InventoryItemScalar? CurrentItem { get; }
        public EvidenceLevel EvidenceLevel { get; }
        public IReadOnlyList<string> SourceOperationIds { get; }
    }

    public sealed class PlayerInventoryDiff
    {
        public PlayerInventoryDiff(
            long? previousSnapshotId,
            long currentSnapshotId,
            DateTimeOffset? previousObservedAtUtc,
            DateTimeOffset currentObservedAtUtc,
            bool isComplete,
            IEnumerable<PlayerInventoryDiffEntry> changes)
        {
            if (previousSnapshotId <= 0) throw new ArgumentOutOfRangeException(nameof(previousSnapshotId));
            if (currentSnapshotId <= 0) throw new ArgumentOutOfRangeException(nameof(currentSnapshotId));
            if (previousObservedAtUtc.HasValue)
                PlayerEvidenceValidation.RequireUtc(previousObservedAtUtc.Value, nameof(previousObservedAtUtc));

            PreviousSnapshotId = previousSnapshotId;
            CurrentSnapshotId = currentSnapshotId;
            PreviousObservedAtUtc = previousObservedAtUtc;
            CurrentObservedAtUtc = PlayerEvidenceValidation.RequireUtc(currentObservedAtUtc, nameof(currentObservedAtUtc));
            IsComplete = isComplete;
            Changes = PlayerEvidenceValidation.Copy(changes, nameof(changes));
            if (Changes.Any(change => change == null))
                throw new ArgumentException("Inventory changes cannot contain null.", nameof(changes));
            if (!isComplete && !Changes.Any(change => change.Kind == InventoryDiffKind.Uncomparable))
                throw new ArgumentException("An incomplete comparison must be explicitly uncomparable.", nameof(changes));
        }

        public long? PreviousSnapshotId { get; }
        public long CurrentSnapshotId { get; }
        public DateTimeOffset? PreviousObservedAtUtc { get; }
        public DateTimeOffset CurrentObservedAtUtc { get; }
        public bool IsComplete { get; }
        public IReadOnlyList<PlayerInventoryDiffEntry> Changes { get; }
    }

    internal static class PlayerEvidenceValidation
    {
        public static string RequireText(string? value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty value is required.", parameterName);
            return value!;
        }

        public static string? OptionalText(string? value, string parameterName)
        {
            if (value != null && string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Optional text cannot be empty.", parameterName);
            return value;
        }

        public static DateTimeOffset RequireUtc(DateTimeOffset value, string parameterName)
        {
            if (value.Offset != TimeSpan.Zero)
                throw new ArgumentException("A UTC timestamp is required.", parameterName);
            return value;
        }

        public static void RequireDefined<TEnum>(TEnum value, string parameterName)
            where TEnum : struct, Enum
        {
            if (!Enum.IsDefined(typeof(TEnum), value))
                throw new ArgumentOutOfRangeException(parameterName);
        }

        public static IReadOnlyList<T> Copy<T>(IEnumerable<T> values, string parameterName)
        {
            if (values == null) throw new ArgumentNullException(parameterName);
            return new ReadOnlyCollection<T>(values.ToArray());
        }

        public static IReadOnlyList<string> CopyText(IEnumerable<string> values, string parameterName)
        {
            var copy = Copy(values, parameterName);
            for (var index = 0; index < copy.Count; index++)
                RequireText(copy[index], parameterName);
            return copy;
        }
    }
}
