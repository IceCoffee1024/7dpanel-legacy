using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LSTY.SevenDPanel.Application.WorldOperations;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    public sealed class StrictWorldHttpRequestConverter : JsonConverter
    {
        public override bool CanWrite => false;

        public override bool CanConvert(Type objectType) =>
            objectType.GetCustomAttribute<JsonConverterAttribute>()?.ConverterType == GetType();

        public override object? ReadJson(
            JsonReader reader,
            Type objectType,
            object? existingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            var body = JObject.Load(reader);
            var allowed = new HashSet<string>(
                objectType
                    .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .Where(property => property.CanWrite)
                    .Select(property => property.Name),
                StringComparer.OrdinalIgnoreCase);
            if (body.Properties().Any(property => !allowed.Contains(property.Name)))
                throw new JsonSerializationException("The request contains an unsupported member.");

            var target = existingValue ?? Activator.CreateInstance(objectType) ??
                throw new JsonSerializationException("The request type could not be created.");
            using var bodyReader = body.CreateReader();
            serializer.Populate(bodyReader, target);
            return target;
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer) =>
            throw new NotSupportedException();
    }

    [JsonConverter(typeof(StrictWorldHttpRequestConverter))]
    public sealed class WorldCoordinateHttpRequest
    {
        public double? X { get; set; }
        public double? Y { get; set; }
        public double? Z { get; set; }
    }

    [JsonConverter(typeof(StrictWorldHttpRequestConverter))]
    public sealed class WorldRegionHttpRequest
    {
        public WorldCoordinateHttpRequest? First { get; set; }
        public WorldCoordinateHttpRequest? Second { get; set; }
    }

    [JsonConverter(typeof(StrictWorldHttpRequestConverter))]
    public sealed class WorldMapBoundsHttpRequest
    {
        public int? MinimumX { get; set; }
        public int? MinimumZ { get; set; }
        public int? MaximumX { get; set; }
        public int? MaximumZ { get; set; }
    }

    public abstract class ConfirmedWorldHttpRequest
    {
        public string? WorldId { get; set; }
        public string? WorldVersion { get; set; }
        public string? MapResourceVersion { get; set; }
        public bool Confirmed { get; set; }
    }

    public abstract class StrongConfirmedWorldHttpRequest : ConfirmedWorldHttpRequest
    {
        public bool StrongConfirmed { get; set; }
    }

    [JsonConverter(typeof(StrictWorldHttpRequestConverter))]
    public sealed class DeleteLandClaimWorldOperationHttpRequest : ConfirmedWorldHttpRequest
    {
        public string? ClaimId { get; set; }
        public string? OwnerStableIdentity { get; set; }
        public WorldCoordinateHttpRequest? Center { get; set; }
        public double? ProtectionRadius { get; set; }
    }

    [JsonConverter(typeof(StrictWorldHttpRequestConverter))]
    public sealed class MoveOnlinePlayerWorldOperationHttpRequest : ConfirmedWorldHttpRequest
    {
        public string? CrossplatformId { get; set; }
        public long? EntityId { get; set; }
        public DateTimeOffset? OnlineObservedAtUtc { get; set; }
        public WorldCoordinateHttpRequest? Destination { get; set; }
    }

    [JsonConverter(typeof(StrictWorldHttpRequestConverter))]
    public sealed class MoveWorldEntityOperationHttpRequest : ConfirmedWorldHttpRequest
    {
        public string? TargetId { get; set; }
        public long? EntityId { get; set; }
        public string? EntityTypeResourceId { get; set; }
        public string? OwnerStableIdentity { get; set; }
        public WorldCoordinateHttpRequest? ObservedPosition { get; set; }
        public WorldCoordinateHttpRequest? Destination { get; set; }
    }

    [JsonConverter(typeof(StrictWorldHttpRequestConverter))]
    public sealed class CopyRegionWorldOperationHttpRequest : ConfirmedWorldHttpRequest
    {
        public WorldRegionHttpRequest? Region { get; set; }
    }

    [JsonConverter(typeof(StrictWorldHttpRequestConverter))]
    public sealed class FillRegionWorldOperationHttpRequest : StrongConfirmedWorldHttpRequest
    {
        public WorldRegionHttpRequest? Region { get; set; }
        public string? CatalogVersion { get; set; }
        public string? BlockInternalName { get; set; }
    }

    [JsonConverter(typeof(StrictWorldHttpRequestConverter))]
    public sealed class ClearRegionWorldOperationHttpRequest : StrongConfirmedWorldHttpRequest
    {
        public WorldRegionHttpRequest? Region { get; set; }
    }

    [JsonConverter(typeof(StrictWorldHttpRequestConverter))]
    public sealed class PasteRegionWorldOperationHttpRequest : StrongConfirmedWorldHttpRequest
    {
        public WorldRegionHttpRequest? Region { get; set; }
        public string? SourceChangeSetId { get; set; }
    }

    [JsonConverter(typeof(StrictWorldHttpRequestConverter))]
    public sealed class SetBlockWorldOperationHttpRequest : StrongConfirmedWorldHttpRequest
    {
        public string? CatalogVersion { get; set; }
        public WorldCoordinateHttpRequest? Coordinate { get; set; }
        public string? BlockInternalName { get; set; }
        public int? Rotation { get; set; }
        public string? Shape { get; set; }
    }

    [JsonConverter(typeof(StrictWorldHttpRequestConverter))]
    public sealed class PlacePrefabWorldOperationHttpRequest : StrongConfirmedWorldHttpRequest
    {
        public string? CatalogVersion { get; set; }
        public string? PrefabResourceId { get; set; }
        public WorldCoordinateHttpRequest? Anchor { get; set; }
        public int? Rotation { get; set; }
        public WorldRegionHttpRequest? KnownBounds { get; set; }
    }

    [JsonConverter(typeof(StrictWorldHttpRequestConverter))]
    public sealed class RemovePrefabWorldOperationHttpRequest : StrongConfirmedWorldHttpRequest
    {
        public string? CatalogVersion { get; set; }
        public string? PrefabResourceId { get; set; }
        public string? PrefabInstanceId { get; set; }
        public WorldCoordinateHttpRequest? Anchor { get; set; }
        public int? Rotation { get; set; }
        public WorldRegionHttpRequest? KnownBounds { get; set; }
    }

    [JsonConverter(typeof(StrictWorldHttpRequestConverter))]
    public sealed class SpawnWorldEntityOperationHttpRequest : StrongConfirmedWorldHttpRequest
    {
        public string? CatalogVersion { get; set; }
        public string? EntityTypeResourceId { get; set; }
        public int? Quantity { get; set; }
        public WorldCoordinateHttpRequest? Center { get; set; }
        public double? Radius { get; set; }
    }

    [JsonConverter(typeof(StrictWorldHttpRequestConverter))]
    public sealed class DeleteWorldEntityOperationHttpRequest : StrongConfirmedWorldHttpRequest
    {
        public string? CatalogVersion { get; set; }
        public string? TargetId { get; set; }
        public long? EntityId { get; set; }
        public string? EntityTypeResourceId { get; set; }
        public string? OwnerStableIdentity { get; set; }
        public WorldCoordinateHttpRequest? ObservedPosition { get; set; }
    }

    [JsonConverter(typeof(StrictWorldHttpRequestConverter))]
    public sealed class CleanupWorldEntitiesOperationHttpRequest : StrongConfirmedWorldHttpRequest
    {
        public string? Category { get; set; }
        public WorldCoordinateHttpRequest? Center { get; set; }
        public double? Radius { get; set; }
        public int? MaximumCount { get; set; }
    }

    [JsonConverter(typeof(StrictWorldHttpRequestConverter))]
    public sealed class ReloadWorldResourceOperationHttpRequest : StrongConfirmedWorldHttpRequest
    {
        public string? ResourceKind { get; set; }
    }

    [JsonConverter(typeof(StrictWorldHttpRequestConverter))]
    public sealed class CollectGameGarbageOperationHttpRequest : ConfirmedWorldHttpRequest
    {
    }

    [JsonConverter(typeof(StrictWorldHttpRequestConverter))]
    public sealed class UndoWorldChangeSetOperationHttpRequest : StrongConfirmedWorldHttpRequest
    {
        public string? SourceOperationId { get; set; }
        public string? ChangeSetId { get; set; }
        public string? CurrentRegionHash { get; set; }
    }

    [JsonConverter(typeof(StrictWorldHttpRequestConverter))]
    public sealed class RefreshMapResourcesJobHttpRequest : ConfirmedWorldHttpRequest
    {
        public WorldMapBoundsHttpRequest? Bounds { get; set; }
    }

    [JsonConverter(typeof(StrictWorldHttpRequestConverter))]
    public sealed class RenderExploredMapJobHttpRequest : ConfirmedWorldHttpRequest
    {
        public WorldMapBoundsHttpRequest? Bounds { get; set; }
    }

    [JsonConverter(typeof(StrictWorldHttpRequestConverter))]
    public sealed class RenderFullMapJobHttpRequest : StrongConfirmedWorldHttpRequest
    {
        public WorldMapBoundsHttpRequest? Bounds { get; set; }
    }

    public sealed class WorldOperationReceiptHttpResponse
    {
        internal WorldOperationReceiptHttpResponse(WorldOperationReceipt receipt)
        {
            OperationId = receipt.OperationId;
            JobId = receipt.JobId;
            Status = receipt.Status.ToString();
            CorrelationId = receipt.CorrelationId;
            CreatedAtUtc = receipt.CreatedAtUtc;
        }

        public string OperationId { get; }
        public Guid JobId { get; }
        public string Status { get; }
        public string CorrelationId { get; }
        public DateTimeOffset CreatedAtUtc { get; }
    }

    public sealed class WorldOperationProgressHttpResponse
    {
        internal WorldOperationProgressHttpResponse(WorldOperationProgress progress)
        {
            Current = progress.Current;
            Total = progress.Total;
        }

        public long? Current { get; }
        public long? Total { get; }
    }

    public sealed class WorldOperationHttpResponse
    {
        internal WorldOperationHttpResponse(WorldOperationRecord operation)
        {
            OperationId = operation.OperationId;
            JobId = operation.JobId;
            Kind = operation.Kind.ToString();
            WorldId = operation.WorldId;
            WorldVersion = operation.WorldVersion;
            MapResourceVersion = operation.MapResourceVersion;
            CorrelationId = operation.CorrelationId;
            ConfirmationSummary = operation.ConfirmationSummary;
            IsReversible = operation.IsReversible;
            ChangeSetId = operation.ChangeSetId;
            Status = operation.Status.ToString();
            Progress = operation.Progress == null
                ? null
                : new WorldOperationProgressHttpResponse(operation.Progress);
            ErrorCode = SafeCode(operation.ErrorCode);
            CreatedAtUtc = operation.CreatedAtUtc;
            StartedAtUtc = operation.StartedAtUtc;
            CompletedAtUtc = operation.CompletedAtUtc;
        }

        public string OperationId { get; }
        public Guid JobId { get; }
        public string Kind { get; }
        public string WorldId { get; }
        public string WorldVersion { get; }
        public string? MapResourceVersion { get; }
        public string CorrelationId { get; }
        public string ConfirmationSummary { get; }
        public bool IsReversible { get; }
        public string? ChangeSetId { get; }
        public string Status { get; }
        public WorldOperationProgressHttpResponse? Progress { get; }
        public string? ErrorCode { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset? StartedAtUtc { get; }
        public DateTimeOffset? CompletedAtUtc { get; }

        private static string? SafeCode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code) || code!.Length > 100) return null;
            foreach (var character in code)
            {
                if (!((character >= 'a' && character <= 'z') ||
                      (character >= '0' && character <= '9') ||
                      character == '_'))
                {
                    return "world_operation_failed";
                }
            }
            return code;
        }
    }

    public sealed class UndoWorldChangeSetPreflightHttpResponse
    {
        internal UndoWorldChangeSetPreflightHttpResponse(UndoWorldChangeSetPreflight preflight)
        {
            if (preflight == null) throw new ArgumentNullException(nameof(preflight));
            SourceOperationId = preflight.SourceOperationId;
            ChangeSetId = preflight.ChangeSetId;
            WorldId = preflight.WorldId;
            WorldVersion = preflight.WorldVersion;
            AfterHash = preflight.AfterHash;
            CurrentRegionHash = preflight.CurrentRegionHash;
            CurrentHashMatches = preflight.CurrentHashMatches;
            Status = preflight.Status;
        }

        public string SourceOperationId { get; }
        public string ChangeSetId { get; }
        public string WorldId { get; }
        public string WorldVersion { get; }
        public string AfterHash { get; }
        public string? CurrentRegionHash { get; }
        public bool? CurrentHashMatches { get; }
        public string Status { get; }
    }
}
