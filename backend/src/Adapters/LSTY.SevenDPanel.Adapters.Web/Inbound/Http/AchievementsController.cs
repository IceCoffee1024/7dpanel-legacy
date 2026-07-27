using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using System.Web.Http.Description;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using LSTY.SevenDPanel.Application.Commerce;
using LSTY.SevenDPanel.Application.Rewards;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    [OwnerAuthorize]
    [RoutePrefix("api/v1/achievements")]
    public sealed class AchievementsController : ApiController
    {
        private readonly ICommerceStore store;
        private readonly SaveAchievementDefinitionUseCase save;

        public AchievementsController(ICommerceStore store, SaveAchievementDefinitionUseCase save)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.save = save ?? throw new ArgumentNullException(nameof(save));
        }

        [HttpPut]
        [Route("definitions/{achievementId}")]
        [ResponseType(typeof(AchievementDefinitionHttpResponse))]
        public HttpResponseMessage PutDefinition(
            string achievementId,
            AchievementDefinitionUpsertHttpRequest? body)
        {
            if (!ModelState.IsValid || body == null)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
            try
            {
                var definition = save.Execute(new AchievementDefinitionDraft(
                    achievementId,
                    CommerceRewardHttpSupport.RequireText(body.Name),
                    body.Description ?? string.Empty,
                    body.Statistic,
                    body.ThresholdValue,
                    CommerceRewardHttpSupport.RequireText(body.RewardPackageId),
                    body.Enabled,
                    body.SortOrder));
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new AchievementDefinitionHttpResponse(definition));
            }
            catch (CommerceConcurrencyException)
            {
                return Problem(HttpStatusCode.Conflict, "commerce_concurrency_conflict", "The achievement definition changed before the request completed.");
            }
            catch (KeyNotFoundException)
            {
                return Problem(HttpStatusCode.NotFound, "reward_package_not_found", "The reward package was not found.");
            }
            catch (ArgumentException)
            {
                return Problem(HttpStatusCode.BadRequest, "invalid_achievement_definition", "The achievement definition is invalid.");
            }
            catch (Exception)
            {
                return Unavailable();
            }
        }

        [HttpGet]
        [Route("records/{achievementId}/{crossplatformId}")]
        [ResponseType(typeof(AchievementRecordHttpResponse))]
        public HttpResponseMessage GetRecord(string achievementId, string crossplatformId)
        {
            try
            {
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new AchievementRecordHttpResponse(
                        store.GetAchievementProgress(achievementId, crossplatformId)));
            }
            catch (KeyNotFoundException)
            {
                return Problem(HttpStatusCode.NotFound, "achievement_record_not_found", "The achievement record was not found.");
            }
            catch (ArgumentException)
            {
                return Problem(HttpStatusCode.BadRequest, "invalid_achievement_query", "The achievement query is invalid.");
            }
            catch (Exception)
            {
                return Unavailable();
            }
        }

        private HttpResponseMessage Unavailable() =>
            Problem(HttpStatusCode.ServiceUnavailable, "achievements_unavailable", "Achievements are unavailable.");

        private HttpResponseMessage Problem(HttpStatusCode status, string code, string detail) =>
            ApiProblemDetailsFactory.CreateResponse(Request, status, code, detail);
    }
}
