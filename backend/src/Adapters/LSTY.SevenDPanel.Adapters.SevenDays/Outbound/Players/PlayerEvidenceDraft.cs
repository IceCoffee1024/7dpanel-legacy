using System;
using System.Collections.Generic;
using System.Linq;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players
{
    internal sealed class PlayerEvidenceDraft
    {
        public PlayerEvidenceDraft(
            string crossplatformId,
            string serverId,
            string worldId,
            DateTimeOffset observedAtUtc,
            PlayerEvidenceSessionDraft? session,
            PlayerEvidenceActivityDraft? activity,
            PlayerEvidenceInventoryDraft? inventory,
            PlayerEvidenceSkillDraft? skills,
            PlayerPosition? position)
        {
            CrossplatformId = RequireText(crossplatformId, nameof(crossplatformId));
            ServerId = RequireText(serverId, nameof(serverId));
            WorldId = RequireText(worldId, nameof(worldId));
            RequireUtc(observedAtUtc, nameof(observedAtUtc));
            ObservedAtUtc = observedAtUtc;
            Session = session;
            Activity = activity;
            Inventory = inventory;
            Skills = skills;
            Position = position;
        }

        public string CrossplatformId { get; }
        public string ServerId { get; }
        public string WorldId { get; }
        public DateTimeOffset ObservedAtUtc { get; }
        public PlayerEvidenceSessionDraft? Session { get; }
        public PlayerEvidenceActivityDraft? Activity { get; }
        public PlayerEvidenceInventoryDraft? Inventory { get; }
        public PlayerEvidenceSkillDraft? Skills { get; }
        public PlayerPosition? Position { get; }

        public PlayerEvidenceDraft WithBoundary(
            PlayerEvidenceSessionDraft? session,
            PlayerEvidenceActivityDraft activity)
        {
            return new PlayerEvidenceDraft(
                CrossplatformId,
                ServerId,
                WorldId,
                ObservedAtUtc,
                session,
                activity,
                Inventory,
                Skills,
                Position);
        }

        internal static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-blank scalar value is required.", parameterName);
            return value.Trim();
        }

        internal static void RequireUtc(DateTimeOffset value, string parameterName)
        {
            if (value.Offset != TimeSpan.Zero)
                throw new ArgumentException("A UTC timestamp is required.", parameterName);
        }
    }

    internal sealed class PlayerEvidenceIdentitySource
    {
        public PlayerEvidenceIdentitySource(string? combinedId)
        {
            CombinedId = string.IsNullOrWhiteSpace(combinedId) ? null : combinedId!.Trim();
        }

        public string? CombinedId { get; }
    }

    internal sealed class PlayerEvidenceSessionDraft
    {
        public PlayerEvidenceSessionDraft(
            long sessionId,
            DateTimeOffset startedAtUtc,
            DateTimeOffset? endedAtUtc,
            string? endReason,
            PlayerPosition? lastPosition,
            PlayerProfileSectionState completeness)
        {
            if (sessionId <= 0) throw new ArgumentOutOfRangeException(nameof(sessionId));
            PlayerEvidenceDraft.RequireUtc(startedAtUtc, nameof(startedAtUtc));
            if (endedAtUtc.HasValue)
            {
                PlayerEvidenceDraft.RequireUtc(endedAtUtc.Value, nameof(endedAtUtc));
                if (endedAtUtc.Value < startedAtUtc)
                    throw new ArgumentOutOfRangeException(nameof(endedAtUtc));
            }
            if (!Enum.IsDefined(typeof(PlayerProfileSectionState), completeness))
                throw new ArgumentOutOfRangeException(nameof(completeness));

            SessionId = sessionId;
            StartedAtUtc = startedAtUtc;
            EndedAtUtc = endedAtUtc;
            EndReason = string.IsNullOrWhiteSpace(endReason) ? null : endReason!.Trim();
            LastPosition = lastPosition;
            Completeness = completeness;
        }

        public long SessionId { get; }
        public DateTimeOffset StartedAtUtc { get; }
        public DateTimeOffset? EndedAtUtc { get; }
        public string? EndReason { get; }
        public PlayerPosition? LastPosition { get; }
        public PlayerProfileSectionState Completeness { get; }
    }

    internal sealed class PlayerEvidenceActivityDraft
    {
        public PlayerEvidenceActivityDraft(
            string kind,
            string? correlationId,
            PlayerProfileSectionState completeness)
        {
            Kind = PlayerEvidenceDraft.RequireText(kind, nameof(kind));
            CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? null : correlationId!.Trim();
            if (!Enum.IsDefined(typeof(PlayerProfileSectionState), completeness))
                throw new ArgumentOutOfRangeException(nameof(completeness));
            Completeness = completeness;
        }

        public string Kind { get; }
        public string? CorrelationId { get; }
        public PlayerProfileSectionState Completeness { get; }
    }

    internal sealed class PlayerEvidenceInventoryDraft
    {
        private readonly InventoryItemScalar[] items;

        public PlayerEvidenceInventoryDraft(
            string gameVersion,
            string? catalogVersion,
            CatalogResolutionState catalogResolution,
            string fingerprint,
            bool adminBoundary,
            IEnumerable<InventoryItemScalar> items)
        {
            GameVersion = PlayerEvidenceDraft.RequireText(gameVersion, nameof(gameVersion));
            CatalogVersion = string.IsNullOrWhiteSpace(catalogVersion) ? null : catalogVersion!.Trim();
            if (!Enum.IsDefined(typeof(CatalogResolutionState), catalogResolution))
                throw new ArgumentOutOfRangeException(nameof(catalogResolution));
            if (catalogResolution == CatalogResolutionState.Resolved && CatalogVersion == null)
                throw new ArgumentException("A resolved catalog requires a version.", nameof(catalogVersion));
            CatalogResolution = catalogResolution;
            Fingerprint = PlayerEvidenceDraft.RequireText(fingerprint, nameof(fingerprint));
            AdminBoundary = adminBoundary;
            this.items = items?.ToArray() ?? throw new ArgumentNullException(nameof(items));
            if (this.items.Any(item => item == null))
                throw new ArgumentException("Inventory items cannot contain null.", nameof(items));
        }

        public string GameVersion { get; }
        public string? CatalogVersion { get; }
        public CatalogResolutionState CatalogResolution { get; }
        public string Fingerprint { get; }
        public bool AdminBoundary { get; }
        public IReadOnlyList<InventoryItemScalar> Items => Array.AsReadOnly(items);
    }

    internal sealed class PlayerEvidenceSkillDraft
    {
        private readonly PlayerSkillValue[] values;

        public PlayerEvidenceSkillDraft(
            string gameVersion,
            int? level,
            int? skillPoints,
            IEnumerable<PlayerSkillValue> values)
        {
            if (level < 0) throw new ArgumentOutOfRangeException(nameof(level));
            if (skillPoints < 0) throw new ArgumentOutOfRangeException(nameof(skillPoints));
            GameVersion = PlayerEvidenceDraft.RequireText(gameVersion, nameof(gameVersion));
            Level = level;
            SkillPoints = skillPoints;
            this.values = values?.ToArray() ?? throw new ArgumentNullException(nameof(values));
            if (this.values.Any(value => value == null))
                throw new ArgumentException("Skill values cannot contain null.", nameof(values));
        }

        public string GameVersion { get; }
        public int? Level { get; }
        public int? SkillPoints { get; }
        public IReadOnlyList<PlayerSkillValue> Values => Array.AsReadOnly(values);
    }

    internal sealed class PlayerEvidenceProgressionDefinition
    {
        public PlayerEvidenceProgressionDefinition(
            string skillKey,
            int? minimum,
            int? maximum,
            string? parentKey)
        {
            if (minimum.HasValue && maximum.HasValue && maximum.Value < minimum.Value)
                throw new ArgumentOutOfRangeException(nameof(maximum));
            SkillKey = PlayerEvidenceDraft.RequireText(skillKey, nameof(skillKey));
            Minimum = minimum;
            Maximum = maximum;
            ParentKey = string.IsNullOrWhiteSpace(parentKey) ? null : parentKey!.Trim();
        }

        public string SkillKey { get; }
        public int? Minimum { get; }
        public int? Maximum { get; }
        public string? ParentKey { get; }
    }

    internal sealed class PlayerEvidenceProgressionDraft
    {
        private readonly PlayerSkillValue[] values;

        public PlayerEvidenceProgressionDraft(
            int? level,
            int? skillPoints,
            IEnumerable<PlayerSkillValue> values)
        {
            Level = level;
            SkillPoints = skillPoints;
            this.values = values?.ToArray() ?? throw new ArgumentNullException(nameof(values));
        }

        public int? Level { get; }
        public int? SkillPoints { get; }
        public IReadOnlyList<PlayerSkillValue> Values => Array.AsReadOnly(values);
    }
}
