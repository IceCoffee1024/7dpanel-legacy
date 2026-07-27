using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using LSTY.SevenDPanel.Application.Commerce;
using LSTY.SevenDPanel.Application.Rewards;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    [OwnerAuthorize]
    [RoutePrefix("api/v1/online-rewards")]
    public sealed class OnlineRewardsController : ApiController
    {
        private readonly ICommerceStore store;
        private readonly SaveOnlineRewardRuleUseCase save;
        private readonly ManualOnlineRewardGrantUseCase manualGrant;
        private readonly IPanelRuntimeStatus runtimeStatus;

        public OnlineRewardsController(
            ICommerceStore store,
            SaveOnlineRewardRuleUseCase save,
            ManualOnlineRewardGrantUseCase manualGrant,
            IPanelRuntimeStatus runtimeStatus)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.save = save ?? throw new ArgumentNullException(nameof(save));
            this.manualGrant = manualGrant ?? throw new ArgumentNullException(nameof(manualGrant));
            this.runtimeStatus = runtimeStatus ?? throw new ArgumentNullException(nameof(runtimeStatus));
        }

        [HttpPut]
        [Route("rules/{ruleId}")]
        [ResponseType(typeof(OnlineRewardRuleHttpResponse))]
        public HttpResponseMessage PutRule(string ruleId, OnlineRewardRuleUpsertHttpRequest? body)
        {
            if (!ModelState.IsValid || body == null)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
            try
            {
                var rule = save.Execute(new OnlineRewardRuleDraft(
                    ruleId,
                    CommerceRewardHttpSupport.RequireText(body.Name),
                    TimeSpan.FromSeconds(body.RequiredOnlineSeconds),
                    body.RepeatIntervalSeconds.HasValue
                        ? TimeSpan.FromSeconds(body.RepeatIntervalSeconds.Value)
                        : null,
                    body.GapPolicy,
                    CommerceRewardHttpSupport.RequireText(body.RewardPackageId),
                    body.Enabled,
                    body.SortOrder));
                return Request.CreateResponse(HttpStatusCode.OK, new OnlineRewardRuleHttpResponse(rule));
            }
            catch (CommerceConcurrencyException)
            {
                return Conflict("commerce_concurrency_conflict", "The online reward rule changed before the request completed.");
            }
            catch (KeyNotFoundException)
            {
                return Problem(HttpStatusCode.NotFound, "reward_package_not_found", "The reward package was not found.");
            }
            catch (ArgumentException)
            {
                return Invalid("invalid_online_reward_rule", "The online reward rule is invalid.");
            }
            catch (OverflowException)
            {
                return Invalid("invalid_online_reward_rule", "The online reward duration is invalid.");
            }
            catch (Exception)
            {
                return Unavailable();
            }
        }

        [HttpGet]
        [Route("records")]
        [ResponseType(typeof(OnlineRewardRecordsHttpResponse))]
        public HttpResponseMessage GetRecords(string? ruleId = null, string? crossplatformId = null)
        {
            try
            {
                var records = store.ListEligibilities(
                    "OnlineReward",
                    CommerceRewardHttpSupport.RequireText(ruleId),
                    CommerceRewardHttpSupport.RequireText(crossplatformId));
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new OnlineRewardRecordsHttpResponse(records));
            }
            catch (ArgumentException)
            {
                return Invalid("invalid_online_reward_query", "The online reward query is invalid.");
            }
            catch (Exception)
            {
                return Unavailable();
            }
        }

        [HttpPost]
        [Route("records/manual")]
        [ResponseType(typeof(OnlineRewardRecordHttpResponse))]
        public async Task<HttpResponseMessage> ManualGrant(
            ManualOnlineRewardGrantHttpRequest? body,
            CancellationToken cancellationToken)
        {
            if (runtimeStatus.GameReadiness != GameReadinessState.Ready)
                return Problem(HttpStatusCode.ServiceUnavailable, "game_not_ready", "The game is not ready for online reward delivery.");
            if (!ModelState.IsValid || body == null)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
            try
            {
                var record = await manualGrant.ExecuteAsync(new ManualOnlineRewardCommand(
                        CommerceRewardHttpSupport.RequireText(body.RuleId),
                        CommerceRewardHttpSupport.RequireText(body.CrossplatformId),
                        body.ExpectedEntityId,
                        CommerceRewardHttpSupport.RequireText(body.ExpectedWorldId),
                        CommerceRewardHttpSupport.RequireText(body.ClientRequestKey),
                        CommerceRewardHttpSupport.RequireActor(this),
                        CommerceRewardHttpSupport.Correlation(this),
                        DateTimeOffset.UtcNow),
                    cancellationToken).ConfigureAwait(false);
                var status = record.State == RewardEligibilityState.Eligible ||
                             record.State == RewardEligibilityState.GrantReserved ||
                             record.State == RewardEligibilityState.PendingReconciliation
                    ? HttpStatusCode.Accepted
                    : HttpStatusCode.OK;
                return Request.CreateResponse(status, new OnlineRewardRecordHttpResponse(record));
            }
            catch (CommerceIdempotencyConflictException)
            {
                return Conflict("commerce_idempotency_conflict", "The client request key was already used for a different online reward grant.");
            }
            catch (CommerceConcurrencyException)
            {
                return Conflict("commerce_concurrency_conflict", "The online reward record changed before the request completed.");
            }
            catch (KeyNotFoundException)
            {
                return Problem(HttpStatusCode.NotFound, "online_reward_rule_not_found", "The online reward rule was not found.");
            }
            catch (ArgumentException)
            {
                return Invalid("invalid_online_reward_grant", "The online reward grant request is invalid.");
            }
            catch (Exception)
            {
                return Unavailable();
            }
        }

        private HttpResponseMessage Invalid(string code, string detail) =>
            Problem(HttpStatusCode.BadRequest, code, detail);

        private HttpResponseMessage Conflict(string code, string detail) =>
            Problem(HttpStatusCode.Conflict, code, detail);

        private HttpResponseMessage Unavailable() =>
            Problem(HttpStatusCode.ServiceUnavailable, "online_rewards_unavailable", "Online rewards are unavailable.");

        private HttpResponseMessage Problem(HttpStatusCode status, string code, string detail) =>
            ApiProblemDetailsFactory.CreateResponse(Request, status, code, detail);
    }
}
