using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using System.Web.Http.Description;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    [OwnerAuthorize]
    [RoutePrefix("api/v1/players")]
    public sealed class PlayerEvidenceController : ApiController
    {
        private const int DefaultPageSize = 50;
        private readonly GetPlayerProfileUseCase profile;
        private readonly GetInventorySnapshotsUseCase inventorySnapshots;
        private readonly GetInventoryDiffsUseCase inventoryDiffs;
        private readonly GetPlayerSkillsUseCase skills;

        public PlayerEvidenceController(
            GetPlayerProfileUseCase profile,
            GetInventorySnapshotsUseCase inventorySnapshots,
            GetInventoryDiffsUseCase inventoryDiffs,
            GetPlayerSkillsUseCase skills)
        {
            this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
            this.inventorySnapshots = inventorySnapshots ??
                throw new ArgumentNullException(nameof(inventorySnapshots));
            this.inventoryDiffs = inventoryDiffs ??
                throw new ArgumentNullException(nameof(inventoryDiffs));
            this.skills = skills ?? throw new ArgumentNullException(nameof(skills));
        }

        [HttpGet]
        [Route("{crossplatformId}/profile")]
        [ResponseType(typeof(PlayerProfileHttpResponse))]
        public HttpResponseMessage GetProfile(string crossplatformId)
        {
            if (!TryReadProfile(crossplatformId, out var value, out var error))
                return error!;
            return Request.CreateResponse(
                HttpStatusCode.OK,
                new PlayerProfileHttpResponse(value!));
        }

        [HttpGet]
        [Route("{crossplatformId}/inventory-snapshots")]
        [ResponseType(typeof(PlayerInventorySnapshotsPageHttpResponse))]
        public HttpResponseMessage GetInventorySnapshots(
            string crossplatformId,
            int pageSize = DefaultPageSize,
            string? cursor = null)
        {
            if (!TryPreparePage(crossplatformId, pageSize, cursor, out var decoded, out var error))
                return error!;
            try
            {
                var section = inventorySnapshots.Execute(
                    new PlayerInventorySnapshotsQuery(crossplatformId, pageSize, decoded),
                    PlayerEvidenceAccess.Owner);
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new PlayerInventorySnapshotsPageHttpResponse(crossplatformId, section));
            }
            catch (ArgumentException)
            {
                return InvalidQuery();
            }
            catch (Exception)
            {
                return ReadFailed();
            }
        }

        [HttpGet]
        [Route("{crossplatformId}/inventory-diffs")]
        [ResponseType(typeof(PlayerInventoryDiffsPageHttpResponse))]
        public HttpResponseMessage GetInventoryDiffs(
            string crossplatformId,
            int pageSize = DefaultPageSize,
            string? cursor = null)
        {
            if (!TryPreparePage(crossplatformId, pageSize, cursor, out var decoded, out var error))
                return error!;
            try
            {
                var section = inventoryDiffs.Execute(
                    new PlayerInventoryDiffsQuery(crossplatformId, pageSize, decoded),
                    PlayerEvidenceAccess.Owner,
                    Array.Empty<string>());
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new PlayerInventoryDiffsPageHttpResponse(crossplatformId, section));
            }
            catch (ArgumentException)
            {
                return InvalidQuery();
            }
            catch (Exception)
            {
                return ReadFailed();
            }
        }

        [HttpGet]
        [Route("{crossplatformId}/skills")]
        [ResponseType(typeof(PlayerSkillsPageHttpResponse))]
        public HttpResponseMessage GetSkills(
            string crossplatformId,
            int pageSize = DefaultPageSize,
            string? cursor = null)
        {
            if (!TryPreparePage(crossplatformId, pageSize, cursor, out var decoded, out var error))
                return error!;
            try
            {
                var section = skills.Execute(
                    new PlayerSkillSnapshotsQuery(crossplatformId, pageSize, decoded),
                    PlayerEvidenceAccess.Owner);
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new PlayerSkillsPageHttpResponse(crossplatformId, section));
            }
            catch (ArgumentException)
            {
                return InvalidQuery();
            }
            catch (Exception)
            {
                return ReadFailed();
            }
        }

        private bool TryPreparePage(
            string crossplatformId,
            int pageSize,
            string? cursor,
            out PlayerEvidenceCursor? decoded,
            out HttpResponseMessage? error)
        {
            decoded = null;
            error = null;
            if (!ModelState.IsValid || string.IsNullOrWhiteSpace(crossplatformId) ||
                pageSize < 1 || pageSize > PlayerInventorySnapshotsQuery.MaximumPageSize)
            {
                error = InvalidQuery();
                return false;
            }
            if (cursor != null &&
                !PlayerEvidenceCursorCodec.TryDecode(cursor, crossplatformId, out decoded))
            {
                error = Problem(
                    HttpStatusCode.BadRequest,
                    "invalid_player_evidence_cursor",
                    "The player evidence cursor is invalid.");
                return false;
            }
            if (!TryReadProfile(crossplatformId, out _, out error)) return false;
            return true;
        }

        private bool TryReadProfile(
            string crossplatformId,
            out PlayerProfile? value,
            out HttpResponseMessage? error)
        {
            value = null;
            error = null;
            if (!ModelState.IsValid || string.IsNullOrWhiteSpace(crossplatformId))
            {
                error = InvalidQuery();
                return false;
            }

            try
            {
                value = profile.Execute(
                    new PlayerEvidenceRangeQuery(
                        crossplatformId,
                        DateTimeOffset.MinValue,
                        DateTimeOffset.MaxValue,
                        PlayerEvidenceRangeQuery.MaximumResultCount),
                    PlayerEvidenceAccess.Owner);
                if (!HasKnownPlayer(value))
                {
                    error = Problem(
                        HttpStatusCode.NotFound,
                        "player_not_found",
                        "The player was not found.");
                    return false;
                }
                return true;
            }
            catch (ArgumentException)
            {
                error = InvalidQuery();
                return false;
            }
            catch (Exception)
            {
                error = ReadFailed();
                return false;
            }
        }

        private static bool HasKnownPlayer(PlayerProfile value)
        {
            if (value.Summary.Value != null || value.Inventory.Value != null || value.Skills.Value != null)
                return true;
            if (value.Sessions.Value?.Count > 0 ||
                value.Activity.Value?.Count > 0 ||
                value.DailyActivity.Value?.Count > 0)
            {
                return true;
            }

            return value.Summary.State == PlayerProfileSectionState.Unavailable;
        }

        private HttpResponseMessage InvalidQuery() => Problem(
            HttpStatusCode.BadRequest,
            "invalid_player_evidence_query",
            "The player evidence query is invalid.");

        private HttpResponseMessage ReadFailed() => Problem(
            HttpStatusCode.InternalServerError,
            "player_evidence_read_failed",
            "Player evidence could not be read.");

        private HttpResponseMessage Problem(
            HttpStatusCode status,
            string code,
            string detail) =>
            ApiProblemDetailsFactory.CreateResponse(Request, status, code, detail);
    }
}
