using System;

namespace LSTY.SevenDPanel.Application.WorldOperations
{
    public enum WorldBlockShape
    {
        Default,
        Cube,
        Ramp,
        Wedge
    }

    public abstract class BlockPrefabOperationRequest
    {
        protected BlockPrefabOperationRequest(
            string actorSubject,
            string worldId,
            string worldVersion,
            string? mapResourceVersion,
            string catalogVersion,
            string correlationId,
            bool confirmed,
            bool strongConfirmed,
            DateTimeOffset requestedAtUtc)
        {
            ActorSubject = MapWorldOperationValidation.RequireText(actorSubject, nameof(actorSubject));
            WorldId = MapWorldOperationValidation.RequireText(worldId, nameof(worldId));
            WorldVersion = MapWorldOperationValidation.RequireText(worldVersion, nameof(worldVersion));
            MapResourceVersion = MapWorldOperationValidation.OptionalText(mapResourceVersion, nameof(mapResourceVersion));
            CatalogVersion = MapWorldOperationValidation.RequireText(catalogVersion, nameof(catalogVersion));
            CorrelationId = MapWorldOperationValidation.RequireText(correlationId, nameof(correlationId));
            Confirmed = confirmed;
            StrongConfirmed = strongConfirmed;
            MapWorldOperationValidation.RequireUtc(requestedAtUtc, nameof(requestedAtUtc));
            RequestedAtUtc = requestedAtUtc;
        }

        public string ActorSubject { get; }
        public string WorldId { get; }
        public string WorldVersion { get; }
        public string? MapResourceVersion { get; }
        public string CatalogVersion { get; }
        public string CorrelationId { get; }
        public bool Confirmed { get; }
        public bool StrongConfirmed { get; }
        public DateTimeOffset RequestedAtUtc { get; }
    }

    public sealed class SetBlockRequest : BlockPrefabOperationRequest
    {
        public SetBlockRequest(
            string actorSubject, string worldId, string worldVersion, string? mapResourceVersion,
            string catalogVersion, WorldCoordinate coordinate, string blockInternalName,
            int rotation, WorldBlockShape? shape, string correlationId, bool confirmed,
            bool strongConfirmed, DateTimeOffset requestedAtUtc)
            : base(actorSubject, worldId, worldVersion, mapResourceVersion, catalogVersion,
                correlationId, confirmed, strongConfirmed, requestedAtUtc)
        {
            Coordinate = coordinate ?? throw new ArgumentNullException(nameof(coordinate));
            X = ToBlockCoordinate(coordinate.X, nameof(coordinate));
            Y = ToBlockCoordinate(coordinate.Y, nameof(coordinate));
            Z = ToBlockCoordinate(coordinate.Z, nameof(coordinate));
            BlockInternalName = MapWorldOperationValidation.RequireText(
                blockInternalName,
                nameof(blockInternalName));
            if (rotation < 0 || rotation > 3) throw new ArgumentOutOfRangeException(nameof(rotation));
            if (shape.HasValue && !Enum.IsDefined(typeof(WorldBlockShape), shape.Value))
                throw new ArgumentOutOfRangeException(nameof(shape));
            Rotation = rotation;
            Shape = shape;
        }

        public WorldCoordinate Coordinate { get; }
        public string BlockInternalName { get; }
        public int Rotation { get; }
        public WorldBlockShape? Shape { get; }
        internal int X { get; }
        internal int Y { get; }
        internal int Z { get; }

        internal static int ToBlockCoordinate(double value, string parameterName)
        {
            if (value != Math.Truncate(value) || value < int.MinValue || value > int.MaxValue)
                throw new ArgumentOutOfRangeException(parameterName);
            return checked((int)value);
        }
    }

    public sealed class PlacePrefabRequest : BlockPrefabOperationRequest
    {
        public PlacePrefabRequest(
            string actorSubject, string worldId, string worldVersion, string? mapResourceVersion,
            string catalogVersion, string prefabResourceId, WorldCoordinate anchor, int rotation,
            WorldRegion knownBounds, string correlationId, bool confirmed, bool strongConfirmed,
            DateTimeOffset requestedAtUtc)
            : base(actorSubject, worldId, worldVersion, mapResourceVersion, catalogVersion,
                correlationId, confirmed, strongConfirmed, requestedAtUtc)
        {
            PrefabResourceId = MapWorldOperationValidation.RequireText(
                prefabResourceId,
                nameof(prefabResourceId));
            Anchor = anchor ?? throw new ArgumentNullException(nameof(anchor));
            AnchorX = SetBlockRequest.ToBlockCoordinate(anchor.X, nameof(anchor));
            AnchorY = SetBlockRequest.ToBlockCoordinate(anchor.Y, nameof(anchor));
            AnchorZ = SetBlockRequest.ToBlockCoordinate(anchor.Z, nameof(anchor));
            if (rotation < 0 || rotation > 3) throw new ArgumentOutOfRangeException(nameof(rotation));
            Rotation = rotation;
            KnownBounds = knownBounds ?? throw new ArgumentNullException(nameof(knownBounds));
        }

        public string PrefabResourceId { get; }
        public WorldCoordinate Anchor { get; }
        public int Rotation { get; }
        public WorldRegion KnownBounds { get; }
        internal int AnchorX { get; }
        internal int AnchorY { get; }
        internal int AnchorZ { get; }
    }

    public sealed class RemovePrefabRequest : BlockPrefabOperationRequest
    {
        public RemovePrefabRequest(
            string actorSubject, string worldId, string worldVersion, string? mapResourceVersion,
            string catalogVersion, string prefabResourceId, string prefabInstanceId,
            WorldCoordinate anchor, int rotation, WorldRegion knownBounds, string correlationId,
            bool confirmed, bool strongConfirmed, DateTimeOffset requestedAtUtc)
            : base(actorSubject, worldId, worldVersion, mapResourceVersion, catalogVersion,
                correlationId, confirmed, strongConfirmed, requestedAtUtc)
        {
            PrefabResourceId = MapWorldOperationValidation.RequireText(
                prefabResourceId,
                nameof(prefabResourceId));
            PrefabInstanceId = MapWorldOperationValidation.RequireText(
                prefabInstanceId,
                nameof(prefabInstanceId));
            Anchor = anchor ?? throw new ArgumentNullException(nameof(anchor));
            AnchorX = SetBlockRequest.ToBlockCoordinate(anchor.X, nameof(anchor));
            AnchorY = SetBlockRequest.ToBlockCoordinate(anchor.Y, nameof(anchor));
            AnchorZ = SetBlockRequest.ToBlockCoordinate(anchor.Z, nameof(anchor));
            if (rotation < 0 || rotation > 3) throw new ArgumentOutOfRangeException(nameof(rotation));
            Rotation = rotation;
            KnownBounds = knownBounds ?? throw new ArgumentNullException(nameof(knownBounds));
        }

        public string PrefabResourceId { get; }
        public string PrefabInstanceId { get; }
        public WorldCoordinate Anchor { get; }
        public int Rotation { get; }
        public WorldRegion KnownBounds { get; }
        internal int AnchorX { get; }
        internal int AnchorY { get; }
        internal int AnchorZ { get; }
    }
}
