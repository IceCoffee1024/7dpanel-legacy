using System;

namespace LSTY.SevenDPanel.Application.WorldOperations
{
    public sealed class WorldCoordinate
    {
        public WorldCoordinate(double x, double y, double z)
        {
            if (!IsFinite(x)) throw new ArgumentOutOfRangeException(nameof(x));
            if (!IsFinite(y)) throw new ArgumentOutOfRangeException(nameof(y));
            if (!IsFinite(z)) throw new ArgumentOutOfRangeException(nameof(z));
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        private static bool IsFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public sealed class WorldMapBounds
    {
        public WorldMapBounds(int minimumX, int minimumZ, int maximumX, int maximumZ)
        {
            if (minimumX > maximumX) throw new ArgumentOutOfRangeException(nameof(maximumX));
            if (minimumZ > maximumZ) throw new ArgumentOutOfRangeException(nameof(maximumZ));
            MinimumX = minimumX;
            MinimumZ = minimumZ;
            MaximumX = maximumX;
            MaximumZ = maximumZ;
        }

        public int MinimumX { get; }
        public int MinimumZ { get; }
        public int MaximumX { get; }
        public int MaximumZ { get; }
    }

    public enum MapJobKind
    {
        RefreshResources,
        RenderExplored,
        RenderFull
    }

    public sealed class DeleteLandClaimRequest
    {
        public DeleteLandClaimRequest(
            string actorSubject,
            string claimId,
            string ownerStableIdentity,
            WorldCoordinate center,
            double protectionRadius,
            string worldId,
            string worldVersion,
            string? mapResourceVersion,
            string correlationId,
            bool confirmed,
            DateTimeOffset requestedAtUtc)
        {
            ActorSubject = MapWorldOperationValidation.RequireText(actorSubject, nameof(actorSubject));
            ClaimId = MapWorldOperationValidation.RequireText(claimId, nameof(claimId));
            OwnerStableIdentity = MapWorldOperationValidation.RequireText(
                ownerStableIdentity,
                nameof(ownerStableIdentity));
            Center = center ?? throw new ArgumentNullException(nameof(center));
            if (!MapWorldOperationValidation.IsFinite(protectionRadius) || protectionRadius < 0)
                throw new ArgumentOutOfRangeException(nameof(protectionRadius));
            ProtectionRadius = protectionRadius;
            WorldId = MapWorldOperationValidation.RequireText(worldId, nameof(worldId));
            WorldVersion = MapWorldOperationValidation.RequireText(worldVersion, nameof(worldVersion));
            MapResourceVersion = MapWorldOperationValidation.OptionalText(mapResourceVersion, nameof(mapResourceVersion));
            CorrelationId = MapWorldOperationValidation.RequireText(correlationId, nameof(correlationId));
            Confirmed = confirmed;
            MapWorldOperationValidation.RequireUtc(requestedAtUtc, nameof(requestedAtUtc));
            RequestedAtUtc = requestedAtUtc;
        }

        public string ActorSubject { get; }
        public string ClaimId { get; }
        public string OwnerStableIdentity { get; }
        public WorldCoordinate Center { get; }
        public double ProtectionRadius { get; }
        public string WorldId { get; }
        public string WorldVersion { get; }
        public string? MapResourceVersion { get; }
        public string CorrelationId { get; }
        public bool Confirmed { get; }
        public DateTimeOffset RequestedAtUtc { get; }
    }

    public sealed class MoveOnlinePlayerRequest
    {
        public MoveOnlinePlayerRequest(
            string actorSubject,
            string crossplatformId,
            long entityId,
            DateTimeOffset onlineObservedAtUtc,
            WorldCoordinate destination,
            string worldId,
            string worldVersion,
            string? mapResourceVersion,
            string correlationId,
            bool confirmed,
            DateTimeOffset requestedAtUtc)
        {
            ActorSubject = MapWorldOperationValidation.RequireText(actorSubject, nameof(actorSubject));
            CrossplatformId = MapWorldOperationValidation.RequireText(crossplatformId, nameof(crossplatformId));
            if (entityId < 0) throw new ArgumentOutOfRangeException(nameof(entityId));
            EntityId = entityId;
            MapWorldOperationValidation.RequireUtc(onlineObservedAtUtc, nameof(onlineObservedAtUtc));
            OnlineObservedAtUtc = onlineObservedAtUtc;
            Destination = destination ?? throw new ArgumentNullException(nameof(destination));
            WorldId = MapWorldOperationValidation.RequireText(worldId, nameof(worldId));
            WorldVersion = MapWorldOperationValidation.RequireText(worldVersion, nameof(worldVersion));
            MapResourceVersion = MapWorldOperationValidation.OptionalText(mapResourceVersion, nameof(mapResourceVersion));
            CorrelationId = MapWorldOperationValidation.RequireText(correlationId, nameof(correlationId));
            Confirmed = confirmed;
            MapWorldOperationValidation.RequireUtc(requestedAtUtc, nameof(requestedAtUtc));
            RequestedAtUtc = requestedAtUtc;
        }

        public string ActorSubject { get; }
        public string CrossplatformId { get; }
        public long EntityId { get; }
        public DateTimeOffset OnlineObservedAtUtc { get; }
        public WorldCoordinate Destination { get; }
        public string WorldId { get; }
        public string WorldVersion { get; }
        public string? MapResourceVersion { get; }
        public string CorrelationId { get; }
        public bool Confirmed { get; }
        public DateTimeOffset RequestedAtUtc { get; }
    }

    public sealed class MoveWorldEntityRequest
    {
        public MoveWorldEntityRequest(
            string actorSubject,
            string targetId,
            long entityId,
            string entityTypeResourceId,
            string? ownerStableIdentity,
            WorldCoordinate observedPosition,
            WorldCoordinate destination,
            string worldId,
            string worldVersion,
            string? mapResourceVersion,
            string correlationId,
            bool confirmed,
            DateTimeOffset requestedAtUtc)
        {
            ActorSubject = MapWorldOperationValidation.RequireText(actorSubject, nameof(actorSubject));
            TargetId = MapWorldOperationValidation.RequireText(targetId, nameof(targetId));
            if (entityId < 0) throw new ArgumentOutOfRangeException(nameof(entityId));
            EntityId = entityId;
            EntityTypeResourceId = MapWorldOperationValidation.RequireText(
                entityTypeResourceId,
                nameof(entityTypeResourceId));
            OwnerStableIdentity = MapWorldOperationValidation.OptionalText(
                ownerStableIdentity,
                nameof(ownerStableIdentity));
            ObservedPosition = observedPosition ?? throw new ArgumentNullException(nameof(observedPosition));
            Destination = destination ?? throw new ArgumentNullException(nameof(destination));
            WorldId = MapWorldOperationValidation.RequireText(worldId, nameof(worldId));
            WorldVersion = MapWorldOperationValidation.RequireText(worldVersion, nameof(worldVersion));
            MapResourceVersion = MapWorldOperationValidation.OptionalText(mapResourceVersion, nameof(mapResourceVersion));
            CorrelationId = MapWorldOperationValidation.RequireText(correlationId, nameof(correlationId));
            Confirmed = confirmed;
            MapWorldOperationValidation.RequireUtc(requestedAtUtc, nameof(requestedAtUtc));
            RequestedAtUtc = requestedAtUtc;
        }

        public string ActorSubject { get; }
        public string TargetId { get; }
        public long EntityId { get; }
        public string EntityTypeResourceId { get; }
        public string? OwnerStableIdentity { get; }
        public WorldCoordinate ObservedPosition { get; }
        public WorldCoordinate Destination { get; }
        public string WorldId { get; }
        public string WorldVersion { get; }
        public string? MapResourceVersion { get; }
        public string CorrelationId { get; }
        public bool Confirmed { get; }
        public DateTimeOffset RequestedAtUtc { get; }
    }

    public sealed class SubmitMapJobRequest
    {
        public SubmitMapJobRequest(
            string actorSubject,
            MapJobKind kind,
            string worldId,
            string worldVersion,
            string? mapResourceVersion,
            WorldMapBounds? bounds,
            string correlationId,
            bool confirmed,
            bool strongConfirmed,
            DateTimeOffset requestedAtUtc)
        {
            if (!Enum.IsDefined(typeof(MapJobKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            ActorSubject = MapWorldOperationValidation.RequireText(actorSubject, nameof(actorSubject));
            Kind = kind;
            WorldId = MapWorldOperationValidation.RequireText(worldId, nameof(worldId));
            WorldVersion = MapWorldOperationValidation.RequireText(worldVersion, nameof(worldVersion));
            MapResourceVersion = MapWorldOperationValidation.OptionalText(mapResourceVersion, nameof(mapResourceVersion));
            Bounds = bounds;
            CorrelationId = MapWorldOperationValidation.RequireText(correlationId, nameof(correlationId));
            Confirmed = confirmed;
            StrongConfirmed = strongConfirmed;
            MapWorldOperationValidation.RequireUtc(requestedAtUtc, nameof(requestedAtUtc));
            RequestedAtUtc = requestedAtUtc;
        }

        public string ActorSubject { get; }
        public MapJobKind Kind { get; }
        public string WorldId { get; }
        public string WorldVersion { get; }
        public string? MapResourceVersion { get; }
        public WorldMapBounds? Bounds { get; }
        public string CorrelationId { get; }
        public bool Confirmed { get; }
        public bool StrongConfirmed { get; }
        public DateTimeOffset RequestedAtUtc { get; }
    }

    public sealed class WorldOperationConfirmationRequiredException : InvalidOperationException
    {
        public WorldOperationConfirmationRequiredException()
            : base("The world operation requires explicit confirmation.") { }
    }

    public sealed class WorldOperationStrongConfirmationRequiredException : InvalidOperationException
    {
        public WorldOperationStrongConfirmationRequiredException()
            : base("The world operation requires strong confirmation.") { }
    }

    public sealed class WorldOperationConflictException : InvalidOperationException
    {
        public WorldOperationConflictException(string code)
            : base(MapWorldOperationValidation.RequireText(code, nameof(code)))
        {
            Code = code;
        }

        public string Code { get; }
    }

    internal static class MapWorldOperationValidation
    {
        internal static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A value is required.", parameterName);
            var normalized = value.Trim();
            if (normalized.Length > 200) throw new ArgumentOutOfRangeException(parameterName);
            return normalized;
        }

        internal static string? OptionalText(string? value, string parameterName) =>
            string.IsNullOrWhiteSpace(value) ? null : RequireText(value!, parameterName);

        internal static void RequireUtc(DateTimeOffset value, string parameterName)
        {
            if (value.Offset != TimeSpan.Zero)
                throw new ArgumentException("A UTC timestamp is required.", parameterName);
        }

        internal static bool IsFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);

        internal static void RequireConfirmation(bool confirmed)
        {
            if (!confirmed) throw new WorldOperationConfirmationRequiredException();
        }
    }
}
