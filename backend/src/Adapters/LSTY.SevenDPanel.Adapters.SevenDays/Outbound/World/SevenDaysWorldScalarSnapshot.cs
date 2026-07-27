using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Maps;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.World
{
    public sealed class SevenDaysWorldScalar
    {
        public SevenDaysWorldScalar(
            string worldId,
            string worldVersion,
            string? seed,
            int? width,
            int? height,
            string? gameVersion,
            string? mapResourceVersion,
            MapExtent? availableExtent)
        {
            if (string.IsNullOrWhiteSpace(worldId))
                throw new ArgumentException("A world identifier is required.", nameof(worldId));
            if (string.IsNullOrWhiteSpace(worldVersion))
                throw new ArgumentException("A world version is required.", nameof(worldVersion));
            if (seed != null && string.IsNullOrWhiteSpace(seed))
                throw new ArgumentException("Optional seed cannot be blank.", nameof(seed));
            if (width.HasValue && width.Value <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height.HasValue && height.Value <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (gameVersion != null && string.IsNullOrWhiteSpace(gameVersion))
                throw new ArgumentException("Optional game version cannot be blank.", nameof(gameVersion));
            if (mapResourceVersion != null && string.IsNullOrWhiteSpace(mapResourceVersion))
                throw new ArgumentException("Optional map resource version cannot be blank.", nameof(mapResourceVersion));

            WorldId = worldId;
            WorldVersion = worldVersion;
            Seed = seed;
            Width = width;
            Height = height;
            GameVersion = gameVersion;
            MapResourceVersion = mapResourceVersion;
            AvailableExtent = availableExtent;
        }

        public string WorldId { get; }
        public string WorldVersion { get; }
        public string? Seed { get; }
        public int? Width { get; }
        public int? Height { get; }
        public string? GameVersion { get; }
        public string? MapResourceVersion { get; }
        public MapExtent? AvailableExtent { get; }
    }

    public sealed class SevenDaysWorldScalarSnapshot
    {
        public SevenDaysWorldScalarSnapshot(
            SevenDaysMapSample mapSample,
            SevenDaysWorldScalar? world,
            IEnumerable<LandClaimSummary> landClaims,
            IEnumerable<VehicleSummary> vehicles,
            IEnumerable<DroneSummary> drones,
            IEnumerable<ContainerSummary> containers,
            IEnumerable<string> blockInternalNames,
            IEnumerable<string> prefabResourceIds,
            IEnumerable<string> entityTypeResourceIds,
            bool worldCaptureFailed = false,
            bool landClaimsCaptureFailed = false,
            bool vehiclesCaptureFailed = false,
            bool dronesCaptureFailed = false,
            bool containersCaptureFailed = false,
            bool toolCatalogCaptureFailed = false)
        {
            MapSample = mapSample ?? throw new ArgumentNullException(nameof(mapSample));
            if (worldCaptureFailed && world != null)
                throw new ArgumentException("A failed world capture cannot contain a world scalar.", nameof(world));
            if (!mapSample.WorldAvailable && world != null)
                throw new ArgumentException("An unavailable game world cannot contain world scalars.", nameof(world));

            World = world;
            LandClaims = Copy(landClaims, nameof(landClaims));
            Vehicles = Copy(vehicles, nameof(vehicles));
            Drones = Copy(drones, nameof(drones));
            Containers = Copy(containers, nameof(containers));
            BlockInternalNames = CopyText(blockInternalNames, nameof(blockInternalNames));
            PrefabResourceIds = CopyOpaqueIds(prefabResourceIds, nameof(prefabResourceIds));
            EntityTypeResourceIds = CopyOpaqueIds(entityTypeResourceIds, nameof(entityTypeResourceIds));
            WorldCaptureFailed = mapSample.WorldAvailable && (worldCaptureFailed || world == null);
            LandClaimsCaptureFailed = mapSample.WorldAvailable && landClaimsCaptureFailed;
            VehiclesCaptureFailed = mapSample.WorldAvailable && vehiclesCaptureFailed;
            DronesCaptureFailed = mapSample.WorldAvailable && dronesCaptureFailed;
            ContainersCaptureFailed = mapSample.WorldAvailable && containersCaptureFailed;
            ToolCatalogCaptureFailed = mapSample.WorldAvailable && toolCatalogCaptureFailed;
        }

        public SevenDaysMapSample MapSample { get; }
        public SevenDaysWorldScalar? World { get; }
        public IReadOnlyList<LandClaimSummary> LandClaims { get; }
        public IReadOnlyList<VehicleSummary> Vehicles { get; }
        public IReadOnlyList<DroneSummary> Drones { get; }
        public IReadOnlyList<ContainerSummary> Containers { get; }
        public IReadOnlyList<string> BlockInternalNames { get; }
        public IReadOnlyList<string> PrefabResourceIds { get; }
        public IReadOnlyList<string> EntityTypeResourceIds { get; }
        public bool WorldCaptureFailed { get; }
        public bool LandClaimsCaptureFailed { get; }
        public bool VehiclesCaptureFailed { get; }
        public bool DronesCaptureFailed { get; }
        public bool ContainersCaptureFailed { get; }
        public bool ToolCatalogCaptureFailed { get; }
        public bool WorldAvailable => MapSample.WorldAvailable;

        public static SevenDaysWorldScalarSnapshot FromMapSample(SevenDaysMapSample sample) =>
            new SevenDaysWorldScalarSnapshot(
                sample,
                null,
                Array.Empty<LandClaimSummary>(),
                Array.Empty<VehicleSummary>(),
                Array.Empty<DroneSummary>(),
                Array.Empty<ContainerSummary>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                worldCaptureFailed: sample.WorldAvailable,
                landClaimsCaptureFailed: sample.WorldAvailable,
                vehiclesCaptureFailed: sample.WorldAvailable,
                dronesCaptureFailed: sample.WorldAvailable,
                containersCaptureFailed: sample.WorldAvailable,
                toolCatalogCaptureFailed: sample.WorldAvailable);

        private static IReadOnlyList<T> Copy<T>(IEnumerable<T> values, string parameterName)
            where T : class
        {
            if (values == null) throw new ArgumentNullException(parameterName);
            var copy = values.ToArray();
            if (copy.Any(item => item == null))
                throw new ArgumentException("Scalar collections cannot contain null values.", parameterName);
            return new ReadOnlyCollection<T>(copy);
        }

        private static IReadOnlyList<string> CopyText(IEnumerable<string> values, string parameterName)
        {
            if (values == null) throw new ArgumentNullException(parameterName);
            var copy = values.ToArray();
            if (copy.Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException("Catalog values cannot be blank.", parameterName);
            return new ReadOnlyCollection<string>(copy);
        }

        private static IReadOnlyList<string> CopyOpaqueIds(IEnumerable<string> values, string parameterName)
        {
            var copy = CopyText(values, parameterName);
            if (copy.Any(value => value.IndexOf('/') >= 0 || value.IndexOf('\\') >= 0))
                throw new ArgumentException("Opaque resource identifiers cannot contain path separators.", parameterName);
            return copy;
        }
    }

    internal static class SevenDaysWorldResourceId
    {
        public static string Create(string kind, string source)
        {
            if (string.IsNullOrWhiteSpace(kind)) throw new ArgumentException("A resource kind is required.", nameof(kind));
            if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("A resource source is required.", nameof(source));
            byte[] hash;
            using (var algorithm = SHA256.Create())
                hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(kind + "\n" + source));
            var truncated = new byte[24];
            Buffer.BlockCopy(hash, 0, truncated, 0, truncated.Length);
            return Convert.ToBase64String(truncated)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        public static string CatalogVersion(SevenDaysWorldScalarSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var source = string.Join(
                "\n",
                snapshot.BlockInternalNames.OrderBy(value => value, StringComparer.Ordinal)
                    .Concat(snapshot.PrefabResourceIds.OrderBy(value => value, StringComparer.Ordinal))
                    .Concat(snapshot.EntityTypeResourceIds.OrderBy(value => value, StringComparer.Ordinal)));
            return Create("world-tool-catalog", source.Length == 0 ? "empty" : source);
        }
    }
}
