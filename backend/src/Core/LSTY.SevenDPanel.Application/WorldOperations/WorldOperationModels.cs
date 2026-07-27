using System;
using System.Collections.Generic;

namespace LSTY.SevenDPanel.Application.WorldOperations
{
    public enum WorldOperationStatus
    {
        Queued,
        Running,
        Succeeded,
        Failed,
        Cancelled,
        Interrupted,
        ResultUnknown,
        RollbackFailed
    }

    public enum WorldOperationKind
    {
        DeleteLandClaim,
        MoveOnlinePlayer,
        MoveEntity,
        RefreshMapResources,
        RenderExploredMap,
        RenderFullMap,
        CopyRegion,
        FillRegion,
        ClearRegion,
        PasteRegion,
        SetBlock,
        PlacePrefab,
        RemovePrefab,
        SpawnEntity,
        DeleteEntity,
        CleanupEntities,
        ReloadBlocks,
        ReloadItems,
        ReloadEntityClasses,
        ReloadPrefabs,
        CollectGarbage,
        UndoChangeSet
    }

    public abstract record WorldOperationTarget;

    public sealed record WorldEntityOperationTarget(
        string TargetId,
        long? EntityId,
        string? StableIdentity,
        string? EntityTypeResourceId,
        string? OwnerIdentity,
        double? ObservedX,
        double? ObservedY,
        double? ObservedZ,
        double? DestinationX,
        double? DestinationY,
        double? DestinationZ,
        int? Quantity = null,
        double? Radius = null,
        string? EntityCategory = null) : WorldOperationTarget;

    public sealed record WorldMapOperationTarget(
        int? MinimumX,
        int? MinimumZ,
        int? MaximumX,
        int? MaximumZ) : WorldOperationTarget;

    public sealed record WorldRegionOperationTarget(
        int MinimumX,
        int MinimumY,
        int MinimumZ,
        int MaximumX,
        int MaximumY,
        int MaximumZ,
        string? SourceChangeSetId,
        string? BlockInternalName) : WorldOperationTarget;

    public sealed record WorldBlockOperationTarget(
        int X,
        int Y,
        int Z,
        string BlockInternalName,
        int Rotation,
        string? Shape) : WorldOperationTarget;

    public sealed record WorldPrefabOperationTarget(
        string PrefabResourceId,
        string? PrefabInstanceId,
        int AnchorX,
        int AnchorY,
        int AnchorZ,
        int Rotation,
        int? MinimumX = null,
        int? MinimumY = null,
        int? MinimumZ = null,
        int? MaximumX = null,
        int? MaximumY = null,
        int? MaximumZ = null) : WorldOperationTarget;

    public sealed record WorldMaintenanceOperationTarget(
        string? EntityTypeResourceId) : WorldOperationTarget;

    public sealed class WorldOperationIntent
    {
        public WorldOperationIntent(
            string actorSubject,
            WorldOperationKind kind,
            string worldId,
            string worldVersion,
            string? mapResourceVersion,
            string correlationId,
            string confirmationSummary,
            bool isReversible,
            WorldOperationTarget target,
            DateTimeOffset createdAtUtc)
        {
            ActorSubject = RequireText(actorSubject, nameof(actorSubject), 200);
            if (!Enum.IsDefined(typeof(WorldOperationKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            Kind = kind;
            WorldId = RequireText(worldId, nameof(worldId), 200);
            WorldVersion = RequireText(worldVersion, nameof(worldVersion), 200);
            MapResourceVersion = Normalize(mapResourceVersion, nameof(mapResourceVersion), 200);
            CorrelationId = RequireText(correlationId, nameof(correlationId), 200);
            ConfirmationSummary = RequireSafeSummary(confirmationSummary);
            IsReversible = isReversible;
            Target = target ?? throw new ArgumentNullException(nameof(target));
            RequireUtc(createdAtUtc, nameof(createdAtUtc));
            CreatedAtUtc = createdAtUtc;
        }

        public string ActorSubject { get; }
        public WorldOperationKind Kind { get; }
        public string WorldId { get; }
        public string WorldVersion { get; }
        public string? MapResourceVersion { get; }
        public string CorrelationId { get; }
        public string ConfirmationSummary { get; }
        public bool IsReversible { get; }
        public WorldOperationTarget Target { get; }
        public DateTimeOffset CreatedAtUtc { get; }

        private static string RequireSafeSummary(string value)
        {
            var summary = RequireText(value, nameof(value), 256);
            if (summary.IndexOf('/') >= 0 || summary.IndexOf('\\') >= 0 ||
                summary.IndexOf('\r') >= 0 || summary.IndexOf('\n') >= 0 ||
                summary.IndexOf("payload_json", StringComparison.OrdinalIgnoreCase) >= 0 ||
                summary.IndexOf("file_path", StringComparison.OrdinalIgnoreCase) >= 0 ||
                summary.IndexOf("type_name", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                throw new ArgumentException(
                    "The confirmation summary must not contain payloads, paths, or arbitrary type names.",
                    nameof(value));
            }
            return summary;
        }

        internal static string RequireText(string value, string parameterName, int maximumLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty value is required.", parameterName);
            var normalized = value.Trim();
            if (normalized.Length > maximumLength)
                throw new ArgumentOutOfRangeException(parameterName);
            return normalized;
        }

        internal static string? Normalize(string? value, string parameterName, int maximumLength) =>
            string.IsNullOrWhiteSpace(value)
                ? null
                : RequireText(value!, parameterName, maximumLength);

        internal static void RequireUtc(DateTimeOffset value, string parameterName)
        {
            if (value.Offset != TimeSpan.Zero)
                throw new ArgumentException("A UTC timestamp is required.", parameterName);
        }
    }

    public sealed record WorldOperationProgress(long? Current, long? Total);

    public sealed record WorldOperationReceipt(
        string OperationId,
        Guid JobId,
        WorldOperationStatus Status,
        string CorrelationId,
        DateTimeOffset CreatedAtUtc);

    public sealed record WorldOperationExecutionRecord(
        string OperationId,
        Guid JobId,
        WorldOperationIntent Intent);

    public sealed record WorldOperationRecord(
        string OperationId,
        Guid JobId,
        string ActorSubject,
        WorldOperationKind Kind,
        string WorldId,
        string WorldVersion,
        string? MapResourceVersion,
        string CorrelationId,
        string ConfirmationSummary,
        bool IsReversible,
        string? ChangeSetId,
        WorldOperationStatus Status,
        WorldOperationProgress? Progress,
        string? ErrorCode,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? StartedAtUtc,
        DateTimeOffset? CompletedAtUtc);

    public sealed record WorldOperationCursor(DateTimeOffset CreatedAtUtc, string OperationId);

    public sealed record WorldOperationQuery(
        int PageSize,
        WorldOperationKind? Kind,
        WorldOperationStatus? Status,
        DateTimeOffset? FromUtc,
        DateTimeOffset? ToUtc,
        WorldOperationCursor? Cursor);

    public sealed record WorldOperationPage(
        IReadOnlyList<WorldOperationRecord> Items,
        WorldOperationCursor? NextCursor);
}
