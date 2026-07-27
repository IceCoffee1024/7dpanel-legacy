using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Web.Http;
using System.Web.Http.Description;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using LSTY.SevenDPanel.Application.WorldOperations;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    [OwnerAuthorize]
    [RoutePrefix("api/v1/world-operations")]
    public sealed class WorldOperationsController : ApiController
    {
        private readonly DeleteLandClaimUseCase deleteLandClaim;
        private readonly MoveOnlinePlayerUseCase moveOnlinePlayer;
        private readonly MoveWorldEntityUseCase moveWorldEntity;
        private readonly CopyRegionUseCase copyRegion;
        private readonly FillRegionUseCase fillRegion;
        private readonly ClearRegionUseCase clearRegion;
        private readonly PasteRegionUseCase pasteRegion;
        private readonly SetBlockUseCase setBlock;
        private readonly PlacePrefabUseCase placePrefab;
        private readonly RemovePrefabUseCase removePrefab;
        private readonly SpawnWorldEntityUseCase spawnEntity;
        private readonly DeleteWorldEntityUseCase deleteEntity;
        private readonly CleanupWorldEntitiesUseCase cleanupEntities;
        private readonly ReloadGameResourceUseCase reloadResource;
        private readonly CollectGameGarbageUseCase collectGarbage;
        private readonly UndoWorldChangeSetUseCase undo;
        private readonly IWorldOperationJobBridge bridge;

        public WorldOperationsController(
            DeleteLandClaimUseCase deleteLandClaim,
            MoveOnlinePlayerUseCase moveOnlinePlayer,
            MoveWorldEntityUseCase moveWorldEntity,
            CopyRegionUseCase copyRegion,
            FillRegionUseCase fillRegion,
            ClearRegionUseCase clearRegion,
            PasteRegionUseCase pasteRegion,
            SetBlockUseCase setBlock,
            PlacePrefabUseCase placePrefab,
            RemovePrefabUseCase removePrefab,
            SpawnWorldEntityUseCase spawnEntity,
            DeleteWorldEntityUseCase deleteEntity,
            CleanupWorldEntitiesUseCase cleanupEntities,
            ReloadGameResourceUseCase reloadResource,
            CollectGameGarbageUseCase collectGarbage,
            UndoWorldChangeSetUseCase undo,
            IWorldOperationJobBridge bridge)
        {
            this.deleteLandClaim = deleteLandClaim ?? throw new ArgumentNullException(nameof(deleteLandClaim));
            this.moveOnlinePlayer = moveOnlinePlayer ?? throw new ArgumentNullException(nameof(moveOnlinePlayer));
            this.moveWorldEntity = moveWorldEntity ?? throw new ArgumentNullException(nameof(moveWorldEntity));
            this.copyRegion = copyRegion ?? throw new ArgumentNullException(nameof(copyRegion));
            this.fillRegion = fillRegion ?? throw new ArgumentNullException(nameof(fillRegion));
            this.clearRegion = clearRegion ?? throw new ArgumentNullException(nameof(clearRegion));
            this.pasteRegion = pasteRegion ?? throw new ArgumentNullException(nameof(pasteRegion));
            this.setBlock = setBlock ?? throw new ArgumentNullException(nameof(setBlock));
            this.placePrefab = placePrefab ?? throw new ArgumentNullException(nameof(placePrefab));
            this.removePrefab = removePrefab ?? throw new ArgumentNullException(nameof(removePrefab));
            this.spawnEntity = spawnEntity ?? throw new ArgumentNullException(nameof(spawnEntity));
            this.deleteEntity = deleteEntity ?? throw new ArgumentNullException(nameof(deleteEntity));
            this.cleanupEntities = cleanupEntities ?? throw new ArgumentNullException(nameof(cleanupEntities));
            this.reloadResource = reloadResource ?? throw new ArgumentNullException(nameof(reloadResource));
            this.collectGarbage = collectGarbage ?? throw new ArgumentNullException(nameof(collectGarbage));
            this.undo = undo ?? throw new ArgumentNullException(nameof(undo));
            this.bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        }

        [HttpPost]
        [Route("land-claims/delete")]
        [ResponseType(typeof(WorldOperationReceiptHttpResponse))]
        public HttpResponseMessage DeleteLandClaim(DeleteLandClaimWorldOperationHttpRequest? request) =>
            Submit(request, (body, actor, correlation, now) => deleteLandClaim.Execute(
                new DeleteLandClaimRequest(
                    actor,
                    Text(body.ClaimId),
                    Text(body.OwnerStableIdentity),
                    Coordinate(body.Center),
                    Number(body.ProtectionRadius),
                    Text(body.WorldId),
                    Text(body.WorldVersion),
                    body.MapResourceVersion,
                    correlation,
                    body.Confirmed,
                    now)));

        [HttpPost]
        [Route("players/move")]
        [ResponseType(typeof(WorldOperationReceiptHttpResponse))]
        public HttpResponseMessage MoveOnlinePlayer(MoveOnlinePlayerWorldOperationHttpRequest? request) =>
            Submit(request, (body, actor, correlation, now) => moveOnlinePlayer.Execute(
                new MoveOnlinePlayerRequest(
                    actor,
                    Text(body.CrossplatformId),
                    Number(body.EntityId),
                    Number(body.OnlineObservedAtUtc),
                    Coordinate(body.Destination),
                    Text(body.WorldId),
                    Text(body.WorldVersion),
                    body.MapResourceVersion,
                    correlation,
                    body.Confirmed,
                    now)));

        [HttpPost]
        [Route("entities/move")]
        [ResponseType(typeof(WorldOperationReceiptHttpResponse))]
        public HttpResponseMessage MoveEntity(MoveWorldEntityOperationHttpRequest? request) =>
            Submit(request, (body, actor, correlation, now) => moveWorldEntity.Execute(
                new MoveWorldEntityRequest(
                    actor,
                    Text(body.TargetId),
                    Number(body.EntityId),
                    Text(body.EntityTypeResourceId),
                    body.OwnerStableIdentity,
                    Coordinate(body.ObservedPosition),
                    Coordinate(body.Destination),
                    Text(body.WorldId),
                    Text(body.WorldVersion),
                    body.MapResourceVersion,
                    correlation,
                    body.Confirmed,
                    now)));

        [HttpPost]
        [Route("regions/copy")]
        [ResponseType(typeof(WorldOperationReceiptHttpResponse))]
        public HttpResponseMessage CopyRegion(CopyRegionWorldOperationHttpRequest? request) =>
            Submit(request, (body, actor, correlation, now) => copyRegion.Execute(
                new CopyRegionRequest(
                    actor,
                    Text(body.WorldId),
                    Text(body.WorldVersion),
                    body.MapResourceVersion,
                    Region(body.Region),
                    correlation,
                    body.Confirmed,
                    now)));

        [HttpPost]
        [Route("regions/fill")]
        [ResponseType(typeof(WorldOperationReceiptHttpResponse))]
        public HttpResponseMessage FillRegion(FillRegionWorldOperationHttpRequest? request) =>
            Submit(request, (body, actor, correlation, now) => fillRegion.Execute(
                new FillRegionRequest(
                    actor,
                    Text(body.WorldId),
                    Text(body.WorldVersion),
                    body.MapResourceVersion,
                    Region(body.Region),
                    Text(body.CatalogVersion),
                    Text(body.BlockInternalName),
                    correlation,
                    body.Confirmed,
                    body.StrongConfirmed,
                    now)));

        [HttpPost]
        [Route("regions/clear")]
        [ResponseType(typeof(WorldOperationReceiptHttpResponse))]
        public HttpResponseMessage ClearRegion(ClearRegionWorldOperationHttpRequest? request) =>
            Submit(request, (body, actor, correlation, now) => clearRegion.Execute(
                new ClearRegionRequest(
                    actor,
                    Text(body.WorldId),
                    Text(body.WorldVersion),
                    body.MapResourceVersion,
                    Region(body.Region),
                    correlation,
                    body.Confirmed,
                    body.StrongConfirmed,
                    now)));

        [HttpPost]
        [Route("regions/paste")]
        [ResponseType(typeof(WorldOperationReceiptHttpResponse))]
        public HttpResponseMessage PasteRegion(PasteRegionWorldOperationHttpRequest? request) =>
            Submit(request, (body, actor, correlation, now) => pasteRegion.Execute(
                new PasteRegionRequest(
                    actor,
                    Text(body.WorldId),
                    Text(body.WorldVersion),
                    body.MapResourceVersion,
                    Region(body.Region),
                    Text(body.SourceChangeSetId),
                    correlation,
                    body.Confirmed,
                    body.StrongConfirmed,
                    now)));

        [HttpPost]
        [Route("blocks/set")]
        [ResponseType(typeof(WorldOperationReceiptHttpResponse))]
        public HttpResponseMessage SetBlock(SetBlockWorldOperationHttpRequest? request) =>
            Submit(request, (body, actor, correlation, now) => setBlock.Execute(
                new SetBlockRequest(
                    actor,
                    Text(body.WorldId),
                    Text(body.WorldVersion),
                    body.MapResourceVersion,
                    Text(body.CatalogVersion),
                    Coordinate(body.Coordinate),
                    Text(body.BlockInternalName),
                    Number(body.Rotation),
                    OptionalEnum<WorldBlockShape>(body.Shape),
                    correlation,
                    body.Confirmed,
                    body.StrongConfirmed,
                    now)));

        [HttpPost]
        [Route("prefabs/place")]
        [ResponseType(typeof(WorldOperationReceiptHttpResponse))]
        public HttpResponseMessage PlacePrefab(PlacePrefabWorldOperationHttpRequest? request) =>
            Submit(request, (body, actor, correlation, now) => placePrefab.Execute(
                new PlacePrefabRequest(
                    actor,
                    Text(body.WorldId),
                    Text(body.WorldVersion),
                    body.MapResourceVersion,
                    Text(body.CatalogVersion),
                    Text(body.PrefabResourceId),
                    Coordinate(body.Anchor),
                    Number(body.Rotation),
                    Region(body.KnownBounds),
                    correlation,
                    body.Confirmed,
                    body.StrongConfirmed,
                    now)));

        [HttpPost]
        [Route("prefabs/remove")]
        [ResponseType(typeof(WorldOperationReceiptHttpResponse))]
        public HttpResponseMessage RemovePrefab(RemovePrefabWorldOperationHttpRequest? request) =>
            Submit(request, (body, actor, correlation, now) => removePrefab.Execute(
                new RemovePrefabRequest(
                    actor,
                    Text(body.WorldId),
                    Text(body.WorldVersion),
                    body.MapResourceVersion,
                    Text(body.CatalogVersion),
                    Text(body.PrefabResourceId),
                    Text(body.PrefabInstanceId),
                    Coordinate(body.Anchor),
                    Number(body.Rotation),
                    Region(body.KnownBounds),
                    correlation,
                    body.Confirmed,
                    body.StrongConfirmed,
                    now)));

        [HttpPost]
        [Route("entities/spawn")]
        [ResponseType(typeof(WorldOperationReceiptHttpResponse))]
        public HttpResponseMessage SpawnEntity(SpawnWorldEntityOperationHttpRequest? request) =>
            Submit(request, (body, actor, correlation, now) => spawnEntity.Execute(
                new SpawnWorldEntityRequest(
                    actor,
                    Text(body.WorldId),
                    Text(body.WorldVersion),
                    body.MapResourceVersion,
                    Text(body.CatalogVersion),
                    Text(body.EntityTypeResourceId),
                    Number(body.Quantity),
                    Coordinate(body.Center),
                    Number(body.Radius),
                    correlation,
                    body.Confirmed,
                    body.StrongConfirmed,
                    now)));

        [HttpPost]
        [Route("entities/delete")]
        [ResponseType(typeof(WorldOperationReceiptHttpResponse))]
        public HttpResponseMessage DeleteEntity(DeleteWorldEntityOperationHttpRequest? request) =>
            Submit(request, (body, actor, correlation, now) => deleteEntity.Execute(
                new DeleteWorldEntityRequest(
                    actor,
                    Text(body.WorldId),
                    Text(body.WorldVersion),
                    body.MapResourceVersion,
                    Text(body.CatalogVersion),
                    Text(body.TargetId),
                    Number(body.EntityId),
                    Text(body.EntityTypeResourceId),
                    body.OwnerStableIdentity,
                    Coordinate(body.ObservedPosition),
                    correlation,
                    body.Confirmed,
                    body.StrongConfirmed,
                    now)));

        [HttpPost]
        [Route("entities/cleanup")]
        [ResponseType(typeof(WorldOperationReceiptHttpResponse))]
        public HttpResponseMessage CleanupEntities(CleanupWorldEntitiesOperationHttpRequest? request) =>
            Submit(request, (body, actor, correlation, now) => cleanupEntities.Execute(
                new CleanupWorldEntitiesRequest(
                    actor,
                    Text(body.WorldId),
                    Text(body.WorldVersion),
                    body.MapResourceVersion,
                    EnumValue<WorldEntityCategory>(body.Category),
                    Coordinate(body.Center),
                    Number(body.Radius),
                    Number(body.MaximumCount),
                    correlation,
                    body.Confirmed,
                    body.StrongConfirmed,
                    now)));

        [HttpPost]
        [Route("xml/reload")]
        [ResponseType(typeof(WorldOperationReceiptHttpResponse))]
        public HttpResponseMessage ReloadResource(ReloadWorldResourceOperationHttpRequest? request) =>
            Submit(request, (body, actor, correlation, now) => reloadResource.Execute(
                new ReloadGameResourceRequest(
                    actor,
                    Text(body.WorldId),
                    Text(body.WorldVersion),
                    body.MapResourceVersion,
                    EnumValue<WorldReloadResourceKind>(body.ResourceKind),
                    correlation,
                    body.Confirmed,
                    body.StrongConfirmed,
                    now)));

        [HttpPost]
        [Route("gc")]
        [ResponseType(typeof(WorldOperationReceiptHttpResponse))]
        public HttpResponseMessage CollectGarbage(CollectGameGarbageOperationHttpRequest? request) =>
            Submit(request, (body, actor, correlation, now) => collectGarbage.Execute(
                new CollectGameGarbageRequest(
                    actor,
                    Text(body.WorldId),
                    Text(body.WorldVersion),
                    body.MapResourceVersion,
                    correlation,
                    body.Confirmed,
                    now)));

        [HttpPost]
        [Route("undo")]
        [ResponseType(typeof(WorldOperationReceiptHttpResponse))]
        public HttpResponseMessage Undo(UndoWorldChangeSetOperationHttpRequest? request) =>
            Submit(request, (body, actor, correlation, now) => undo.Execute(
                new UndoWorldChangeSetRequest(
                    actor,
                    Text(body.SourceOperationId),
                    Text(body.ChangeSetId),
                    Text(body.WorldId),
                    Text(body.WorldVersion),
                    Text(body.CurrentRegionHash),
                    correlation,
                    body.Confirmed,
                    body.StrongConfirmed,
                    now)));

        [HttpGet]
        [Route("{operationId}")]
        [ResponseType(typeof(WorldOperationHttpResponse))]
        public HttpResponseMessage Get(string operationId)
        {
            if (!ModelState.IsValid || string.IsNullOrWhiteSpace(operationId))
                return InvalidRequest();

            try
            {
                var operation = bridge.Get(operationId);
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new WorldOperationHttpResponse(operation));
            }
            catch (KeyNotFoundException)
            {
                return Problem(
                    HttpStatusCode.NotFound,
                    "world_operation_not_found",
                    "The world operation was not found.");
            }
            catch (ArgumentException)
            {
                return InvalidRequest();
            }
            catch (Exception)
            {
                return Unavailable();
            }
        }

        private HttpResponseMessage Submit<TRequest>(
            TRequest? request,
            Func<TRequest, string, string, DateTimeOffset, WorldOperationReceipt> execute)
            where TRequest : class
        {
            if (!ModelState.IsValid || request == null) return InvalidRequest();
            var actor = ActorSubject();
            if (actor == null)
            {
                return Problem(
                    HttpStatusCode.Unauthorized,
                    "authentication_required",
                    "Authentication is required to submit a world operation.");
            }

            try
            {
                var receipt = execute(
                    request,
                    actor,
                    ApiProblemDetailsFactory.GetTraceId(Request),
                    DateTimeOffset.UtcNow);
                var response = Request.CreateResponse(
                    HttpStatusCode.Accepted,
                    new WorldOperationReceiptHttpResponse(receipt));
                response.Headers.Location = new Uri(
                    Request.RequestUri,
                    "/api/v1/world-operations/" + Uri.EscapeDataString(receipt.OperationId));
                return response;
            }
            catch (WorldOperationConfirmationRequiredException)
            {
                return Problem(
                    (HttpStatusCode)422,
                    "confirmation_required",
                    "Explicit confirmation is required for this world operation.");
            }
            catch (WorldOperationStrongConfirmationRequiredException)
            {
                return Problem(
                    (HttpStatusCode)422,
                    "strong_confirmation_required",
                    "Strong confirmation is required for this world operation.");
            }
            catch (WorldOperationConflictException exception)
            {
                return Problem(
                    HttpStatusCode.Conflict,
                    SafeConflictCode(exception.Code),
                    "The world operation conflicts with current server state.");
            }
            catch (KeyNotFoundException)
            {
                return Problem(
                    HttpStatusCode.NotFound,
                    "world_resource_not_found",
                    "The requested world resource was not found.");
            }
            catch (ArgumentException)
            {
                return InvalidRequest();
            }
            catch (Exception)
            {
                return Unavailable();
            }
        }

        private string? ActorSubject()
        {
            var identity = User?.Identity as ClaimsIdentity;
            var value = identity?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private HttpResponseMessage InvalidRequest() => Problem(
            (HttpStatusCode)422,
            "invalid_world_operation_request",
            "The world operation request is invalid.");

        private HttpResponseMessage Unavailable() => Problem(
            HttpStatusCode.ServiceUnavailable,
            "world_operation_unavailable",
            "The world operation service is unavailable.");

        private HttpResponseMessage Problem(
            HttpStatusCode status,
            string code,
            string detail) => ApiProblemDetailsFactory.CreateResponse(
                Request,
                status,
                code,
                detail);

        private static string Text(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A value is required.");
            return value!;
        }

        private static T Number<T>(T? value) where T : struct =>
            value ?? throw new ArgumentException("A numeric value is required.");

        private static WorldCoordinate Coordinate(WorldCoordinateHttpRequest? value)
        {
            if (value == null) throw new ArgumentException("A coordinate is required.");
            return new WorldCoordinate(Number(value.X), Number(value.Y), Number(value.Z));
        }

        private static WorldRegion Region(WorldRegionHttpRequest? value)
        {
            if (value == null) throw new ArgumentException("A region is required.");
            return new WorldRegion(Coordinate(value.First), Coordinate(value.Second));
        }

        private static T EnumValue<T>(string? value) where T : struct
        {
            if (value == null || !Enum.TryParse(value, true, out T result) ||
                !Enum.IsDefined(typeof(T), result))
            {
                throw new ArgumentException("An approved value is required.");
            }
            return result;
        }

        private static T? OptionalEnum<T>(string? value) where T : struct =>
            string.IsNullOrWhiteSpace(value) ? null : EnumValue<T>(value);

        private static string SafeConflictCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code) || code.Length > 100)
                return "world_operation_conflict";
            foreach (var character in code)
            {
                if (!((character >= 'a' && character <= 'z') ||
                      (character >= '0' && character <= '9') ||
                      character == '_'))
                {
                    return "world_operation_conflict";
                }
            }
            return code;
        }
    }
}
