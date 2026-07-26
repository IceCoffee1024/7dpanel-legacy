using System;
using System.Collections.Generic;
using System.Linq;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    public sealed class MapMetadataHttpResponse
    {
        public MapMetadataHttpResponse(MapMetadataProjectionSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var metadata = snapshot.Metadata;
            Availability = OverviewHttpResponse.ToContract(snapshot.Availability);
            ObservedAtUtc = snapshot.ObservedAtUtc;
            WorldId = snapshot.WorldId;
            WorldName = metadata?.WorldName;
            Extent = metadata == null ? null : new MapExtentHttpResponse(metadata.Extent);
            Axes = metadata == null ? null : new MapAxesHttpResponse(metadata.Axes);
            AvailableZoomLevels = metadata?.AvailableZoomLevels;
            TileSize = metadata?.TileSizePixels;
            MapResourceVersion = metadata?.ResourceVersion;
        }

        public string Availability { get; }
        public DateTimeOffset? ObservedAtUtc { get; }
        public string? WorldId { get; }
        public string? WorldName { get; }
        public MapExtentHttpResponse? Extent { get; }
        public MapAxesHttpResponse? Axes { get; }
        public IReadOnlyList<int>? AvailableZoomLevels { get; }
        public int? TileSize { get; }
        public string? MapResourceVersion { get; }
    }

    public sealed class MapExtentHttpResponse
    {
        public MapExtentHttpResponse(MapExtent extent)
        {
            MinimumX = extent.MinimumX;
            MinimumZ = extent.MinimumZ;
            MaximumX = extent.MaximumX;
            MaximumZ = extent.MaximumZ;
        }

        public float MinimumX { get; }
        public float MinimumZ { get; }
        public float MaximumX { get; }
        public float MaximumZ { get; }
    }

    public sealed class MapAxesHttpResponse
    {
        public MapAxesHttpResponse(MapAxisConvention axes)
        {
            if (axes == null) throw new ArgumentNullException(nameof(axes));
            XAxisDirection = axes.XAxisDirection;
            ZAxisDirection = axes.ZAxisDirection;
        }

        public string XAxisDirection { get; }
        public string ZAxisDirection { get; }
    }

    public sealed class MapGameTimeHttpResponse
    {
        public MapGameTimeHttpResponse(MapGameTimeProjectionSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            Availability = OverviewHttpResponse.ToContract(snapshot.Availability);
            Day = snapshot.GameTime?.Day;
            Hour = snapshot.GameTime?.Hour;
            Minute = snapshot.GameTime?.Minute;
            ObservedAtUtc = snapshot.GameTime?.ObservedAtUtc;
        }

        public string Availability { get; }
        public int? Day { get; }
        public int? Hour { get; }
        public int? Minute { get; }
        public DateTimeOffset? ObservedAtUtc { get; }
    }

    public sealed class PlayerTrackHttpResponse
    {
        public PlayerTrackHttpResponse(string crossplatformId, GetPlayerTrackResult result)
        {
            if (string.IsNullOrWhiteSpace(crossplatformId))
                throw new ArgumentException("A cross-platform identity is required.", nameof(crossplatformId));
            if (result == null) throw new ArgumentNullException(nameof(result));
            CrossplatformId = crossplatformId;
            Segments = result.Segments
                .Select(segment => new PlayerTrackSegmentHttpResponse(segment))
                .ToArray();
        }

        public string CrossplatformId { get; }
        public IReadOnlyList<PlayerTrackSegmentHttpResponse> Segments { get; }
    }

    public sealed class PlayerTrackSegmentHttpResponse
    {
        public PlayerTrackSegmentHttpResponse(PlayerTrackSegment segment)
        {
            if (segment == null) throw new ArgumentNullException(nameof(segment));
            Points = segment.Points.Select(point => new PlayerTrackPointHttpResponse(point)).ToArray();
        }

        public IReadOnlyList<PlayerTrackPointHttpResponse> Points { get; }
    }

    public sealed class PlayerTrackPointHttpResponse
    {
        public PlayerTrackPointHttpResponse(PlayerTrackPoint point)
        {
            if (point == null) throw new ArgumentNullException(nameof(point));
            SnapshotId = point.SnapshotId;
            Name = point.Name;
            X = point.X;
            Y = point.Y;
            Z = point.Z;
            ObservedAtUtc = point.ObservedAtUtc;
        }

        public long SnapshotId { get; }
        public string Name { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public DateTimeOffset ObservedAtUtc { get; }
    }

    public sealed class MapLayerHttpResponse
    {
        private MapLayerHttpResponse(
            string layerId,
            AvailabilityState availability,
            DateTimeOffset? observedAtUtc,
            bool isZoomSufficient,
            IEnumerable<MapLayerItemHttpResponse> items)
        {
            LayerId = layerId;
            Availability = OverviewHttpResponse.ToContract(availability);
            ObservedAtUtc = observedAtUtc;
            IsZoomSufficient = isZoomSufficient;
            Items = items.ToArray();
        }

        public string LayerId { get; }
        public string Availability { get; }
        public DateTimeOffset? ObservedAtUtc { get; }
        public bool IsZoomSufficient { get; }
        public IReadOnlyList<MapLayerItemHttpResponse> Items { get; }

        public static MapLayerHttpResponse FromProjection(
            string layerId,
            MapLayerProjectionSnapshot snapshot) =>
            new MapLayerHttpResponse(
                layerId,
                snapshot.Availability,
                snapshot.ObservedAtUtc,
                snapshot.IsZoomSufficient,
                snapshot.Features.Select(feature =>
                    MapLayerItemHttpResponse.FromFeature(feature, snapshot.ObservedAtUtc)));

        public static MapLayerHttpResponse FromHistorical(
            string layerId,
            HistoricalPlayerLastLocationsResult result) =>
            new MapLayerHttpResponse(
                layerId,
                result.Availability,
                null,
                result.IsZoomSufficient,
                result.Locations.Select(MapLayerItemHttpResponse.FromHistoricalLocation));

        public static MapLayerHttpResponse FromTransient(
            string layerId,
            TransientEntityMapSnapshot snapshot) =>
            new MapLayerHttpResponse(
                layerId,
                snapshot.Availability,
                snapshot.ObservedAtUtc,
                snapshot.IsZoomSufficient,
                snapshot.Items.Select(item => MapLayerItemHttpResponse.FromTransient(
                    item,
                    snapshot.Kind,
                    snapshot.ObservedAtUtc)));

        public static MapLayerHttpResponse Unavailable(string layerId) =>
            new MapLayerHttpResponse(
                layerId,
                AvailabilityState.Unavailable,
                null,
                false,
                Array.Empty<MapLayerItemHttpResponse>());
    }

    public sealed class MapLayerItemHttpResponse
    {
        private MapLayerItemHttpResponse(
            string id,
            string kind,
            MapLayerPosition position,
            DateTimeOffset observedAtUtc)
        {
            Id = id;
            Kind = kind;
            Position = new MapLayerPositionHttpResponse(position);
            ObservedAtUtc = observedAtUtc;
        }

        public string Id { get; }
        public string Kind { get; }
        public MapLayerPositionHttpResponse Position { get; }
        public DateTimeOffset ObservedAtUtc { get; }
        public string? Name { get; private set; }
        public string? PlayerCombinedId { get; private set; }
        public string? Prefab { get; private set; }
        public MapLayerBoundsHttpResponse? PrefabBounds { get; private set; }
        public double? ProtectionRadius { get; private set; }
        public bool? IsOpen { get; private set; }
        public string? OwnerCrossplatformId { get; private set; }
        public bool? IsValid { get; private set; }
        public DateTimeOffset? OwnerLastLoginUtc { get; private set; }
        public string? VehicleType { get; private set; }
        public string? LoadState { get; private set; }
        public double? FuelPercentage { get; private set; }
        public int? Quality { get; private set; }
        public bool? IsLocked { get; private set; }
        public int? StorageItemCount { get; private set; }
        public string? EntityType { get; private set; }

        public static MapLayerItemHttpResponse FromFeature(
            MapLayerFeature feature,
            DateTimeOffset? observedAtUtc)
        {
            if (!observedAtUtc.HasValue)
                throw new ArgumentException("An observed time is required.", nameof(observedAtUtc));
            if (feature is TraderMapFeature trader)
            {
                return new MapLayerItemHttpResponse(
                    trader.Id,
                    "trader",
                    trader.Position,
                    observedAtUtc.Value)
                {
                    Name = trader.Name,
                    PrefabBounds = trader.PrefabBounds.HasValue
                        ? new MapLayerBoundsHttpResponse(trader.PrefabBounds.Value)
                        : null,
                    ProtectionRadius = trader.ProtectionRadius,
                    IsOpen = trader.IsOpen
                };
            }
            if (feature is LandClaimMapFeature claim)
            {
                return new MapLayerItemHttpResponse(
                    claim.Id,
                    "claim",
                    claim.Position,
                    observedAtUtc.Value)
                {
                    OwnerCrossplatformId = claim.OwnerCrossplatformId,
                    ProtectionRadius = claim.ProtectionRadius,
                    IsValid = claim.IsValid,
                    OwnerLastLoginUtc = claim.OwnerLastLoginUtc
                };
            }
            if (feature is VehicleMapFeature vehicle)
            {
                return new MapLayerItemHttpResponse(
                    vehicle.Id,
                    "vehicle",
                    vehicle.Position,
                    observedAtUtc.Value)
                {
                    VehicleType = vehicle.VehicleType,
                    OwnerCrossplatformId = vehicle.OwnerCrossplatformId,
                    LoadState = ToLoadState(vehicle.LoadState),
                    FuelPercentage = vehicle.FuelPercentage,
                    Quality = vehicle.Quality,
                    IsLocked = vehicle.IsLocked,
                    StorageItemCount = vehicle.StorageItemCount
                };
            }
            if (feature is DroneMapFeature drone)
            {
                return new MapLayerItemHttpResponse(
                    drone.Id,
                    "drone",
                    drone.Position,
                    observedAtUtc.Value)
                {
                    OwnerCrossplatformId = drone.OwnerCrossplatformId,
                    LoadState = ToLoadState(drone.LoadState)
                };
            }
            throw new ArgumentOutOfRangeException(nameof(feature));
        }

        public static MapLayerItemHttpResponse FromHistoricalLocation(
            HistoricalPlayerLastRetainedLocation location) =>
            new MapLayerItemHttpResponse(
                "history:" + location.SnapshotId,
                "historical-player",
                location.Position,
                location.ObservedAtUtc)
            {
                Name = location.DisplayName,
                PlayerCombinedId = location.CrossplatformId
            };

        public static MapLayerItemHttpResponse FromTransient(
            TransientEntityMapItem item,
            TransientEntityMapKind kind,
            DateTimeOffset? observedAtUtc)
        {
            if (!observedAtUtc.HasValue)
                throw new ArgumentException("An observed time is required.", nameof(observedAtUtc));
            var contractKind = kind == TransientEntityMapKind.Animals ? "animal" : "hostile";
            return new MapLayerItemHttpResponse(
                contractKind + ":" + item.EntityId,
                contractKind,
                item.Position,
                observedAtUtc.Value)
            {
                EntityType = item.EntityType
            };
        }

        private static string ToLoadState(MapEntityLoadState state) =>
            state == MapEntityLoadState.Loaded ? "loaded" : "unloaded";
    }

    public sealed class MapLayerPositionHttpResponse
    {
        public MapLayerPositionHttpResponse(MapLayerPosition position)
        {
            X = position.X;
            Y = position.Y;
            Z = position.Z;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }
    }

    public sealed class MapLayerBoundsHttpResponse
    {
        public MapLayerBoundsHttpResponse(MapLayerBounds bounds)
        {
            MinimumX = bounds.MinimumX;
            MinimumZ = bounds.MinimumZ;
            MaximumX = bounds.MaximumX;
            MaximumZ = bounds.MaximumZ;
        }

        public double MinimumX { get; }
        public double MinimumZ { get; }
        public double MaximumX { get; }
        public double MaximumZ { get; }
    }

    public sealed class PlayerAreaSearchHttpResponse
    {
        public PlayerAreaSearchHttpResponse(SearchPlayersInAreaResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            Hits = result.Hits.Select(hit => new PlayerAreaHitHttpResponse(hit)).ToArray();
            CandidateObservationCount = result.CandidateObservationCount;
            MatchingObservationCount = result.MatchingObservationCount;
            CandidateObservationLimitReached = result.CandidateObservationLimitReached;
            PlayerResultLimitReached = result.PlayerResultLimitReached;
        }

        public IReadOnlyList<PlayerAreaHitHttpResponse> Hits { get; }
        public int CandidateObservationCount { get; }
        public int MatchingObservationCount { get; }
        public bool CandidateObservationLimitReached { get; }
        public bool PlayerResultLimitReached { get; }
    }

    public sealed class PlayerAreaHitHttpResponse
    {
        public PlayerAreaHitHttpResponse(PlayerAreaRetainedObservationHit hit)
        {
            CrossplatformId = hit.CrossplatformId;
            DisplayName = hit.DisplayName;
            FirstHitUtc = hit.FirstHitUtc;
            LastHitUtc = hit.LastHitUtc;
            HitObservationCount = hit.HitObservationCount;
            LastPosition = new PlayerMapPositionHttpResponse(hit.LastPosition);
        }

        public string CrossplatformId { get; }
        public string DisplayName { get; }
        public DateTimeOffset FirstHitUtc { get; }
        public DateTimeOffset LastHitUtc { get; }
        public int HitObservationCount { get; }
        public PlayerMapPositionHttpResponse LastPosition { get; }
    }

    public sealed class PlayerMapPositionHttpResponse
    {
        public PlayerMapPositionHttpResponse(PlayerMapPosition position)
        {
            X = position.X;
            Y = position.Y;
            Z = position.Z;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }
    }
}
