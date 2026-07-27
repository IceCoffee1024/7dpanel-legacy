using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using LSTY.SevenDPanel.Application.Rewards;
using LSTY.SevenDPanel.Domain.Rewards;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    [OwnerAuthorize]
    [RoutePrefix("api/v1")]
    public sealed class RewardsController : ApiController
    {
        private readonly IRewardStore store;
        private readonly IDailyRewardPolicyStore dailyPolicies;
        private readonly SaveRewardPackageUseCase savePackage;
        private readonly SaveDailyRewardPolicyUseCase saveDailyPolicy;
        private readonly GrantRewardUseCase grantReward;
        private readonly PendingRewardReconciliationUseCase pending;
        private readonly ConfirmRewardGrantUseCase confirm;
        private readonly RefundRewardGrantUseCase refund;
        private readonly CompensateRewardGrantUseCase compensate;
        private readonly IPanelRuntimeStatus runtimeStatus;

        public RewardsController(
            IRewardStore store,
            IDailyRewardPolicyStore dailyPolicies,
            SaveRewardPackageUseCase savePackage,
            SaveDailyRewardPolicyUseCase saveDailyPolicy,
            GrantRewardUseCase grantReward,
            PendingRewardReconciliationUseCase pending,
            ConfirmRewardGrantUseCase confirm,
            RefundRewardGrantUseCase refund,
            CompensateRewardGrantUseCase compensate,
            IPanelRuntimeStatus runtimeStatus)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.dailyPolicies = dailyPolicies ?? throw new ArgumentNullException(nameof(dailyPolicies));
            this.savePackage = savePackage ?? throw new ArgumentNullException(nameof(savePackage));
            this.saveDailyPolicy = saveDailyPolicy ??
                throw new ArgumentNullException(nameof(saveDailyPolicy));
            this.grantReward = grantReward ?? throw new ArgumentNullException(nameof(grantReward));
            this.pending = pending ?? throw new ArgumentNullException(nameof(pending));
            this.confirm = confirm ?? throw new ArgumentNullException(nameof(confirm));
            this.refund = refund ?? throw new ArgumentNullException(nameof(refund));
            this.compensate = compensate ?? throw new ArgumentNullException(nameof(compensate));
            this.runtimeStatus = runtimeStatus ?? throw new ArgumentNullException(nameof(runtimeStatus));
        }

        [HttpGet]
        [Route("daily-reward-rules/{ruleId}")]
        [ResponseType(typeof(DailyRewardPolicyHttpResponse))]
        public HttpResponseMessage GetDailyPolicy(string ruleId)
        {
            try
            {
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new DailyRewardPolicyHttpResponse(dailyPolicies.GetDailyRewardPolicy(ruleId)));
            }
            catch (System.Collections.Generic.KeyNotFoundException)
            {
                return Problem(HttpStatusCode.NotFound, "daily_reward_policy_not_found",
                    "The daily reward policy was not found.");
            }
            catch (ArgumentException)
            {
                return Invalid("invalid_daily_reward_policy", "The daily reward policy identifier is invalid.");
            }
            catch (Exception)
            {
                return Unavailable();
            }
        }

        [HttpPut]
        [Route("daily-reward-rules/{ruleId}")]
        [ResponseType(typeof(DailyRewardPolicyHttpResponse))]
        public HttpResponseMessage PutDailyPolicy(
            string ruleId,
            DailyRewardPolicyUpsertHttpRequest? body)
        {
            if (!ModelState.IsValid || body == null)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
            try
            {
                var policy = saveDailyPolicy.Execute(new DailyRewardPolicyDraft(
                    ruleId,
                    CommerceRewardHttpSupport.RequireText(body.RewardPackageId),
                    body.Enabled,
                    body.ExpectedRowVersion));
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new DailyRewardPolicyHttpResponse(policy));
            }
            catch (DailyRewardPolicyConcurrencyException)
            {
                return Conflict("daily_reward_policy_concurrency_conflict",
                    "The daily reward policy changed before the request completed.");
            }
            catch (RewardPackageNotFoundException)
            {
                return Problem(HttpStatusCode.NotFound, "reward_package_not_found",
                    "The reward package was not found.");
            }
            catch (ArgumentException)
            {
                return Invalid("invalid_daily_reward_policy", "The daily reward policy is invalid.");
            }
            catch (Exception)
            {
                return Unavailable();
            }
        }

        [HttpGet]
        [Route("reward-packages/{packageId}")]
        [ResponseType(typeof(RewardPackageHttpResponse))]
        public HttpResponseMessage GetPackage(string packageId)
        {
            try
            {
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new RewardPackageHttpResponse(store.GetPackage(packageId)));
            }
            catch (RewardPackageNotFoundException)
            {
                return Problem(HttpStatusCode.NotFound, "reward_package_not_found", "The reward package was not found.");
            }
            catch (ArgumentException)
            {
                return Invalid("invalid_reward_package", "The reward package identifier is invalid.");
            }
            catch (Exception)
            {
                return Unavailable();
            }
        }

        [HttpPut]
        [Route("reward-packages/{packageId}")]
        [ResponseType(typeof(RewardPackageHttpResponse))]
        public HttpResponseMessage PutPackage(string packageId, RewardPackageUpsertHttpRequest? body)
        {
            if (!ModelState.IsValid || body?.Entries == null)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
            try
            {
                var package = savePackage.Execute(new RewardPackageDraft(
                    packageId,
                    CommerceRewardHttpSupport.RequireText(body.Name),
                    body.Description ?? string.Empty,
                    body.Enabled,
                    body.SortOrder,
                    body.Entries.Select(entry => entry.ToDraft())));
                return Request.CreateResponse(HttpStatusCode.OK, new RewardPackageHttpResponse(package));
            }
            catch (RewardCatalogValidationException exception)
            {
                var unavailable = string.Equals(exception.Message, "reward_catalog_unavailable", StringComparison.Ordinal);
                return Problem(
                    unavailable ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Conflict,
                    exception.Message,
                    unavailable ? "The game resource catalog is unavailable." : "The game resource catalog changed.");
            }
            catch (RewardConcurrencyException)
            {
                return Conflict("reward_concurrency_conflict", "The reward package changed before the request completed.");
            }
            catch (ArgumentException)
            {
                return Invalid("invalid_reward_package", "The reward package is invalid.");
            }
            catch (Exception)
            {
                return Unavailable();
            }
        }

        [HttpGet]
        [Route("grant-operations/{operationId}")]
        [ResponseType(typeof(GrantOperationHttpResponse))]
        public HttpResponseMessage GetGrant(string operationId)
        {
            try
            {
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new GrantOperationHttpResponse(store.GetGrant(operationId)));
            }
            catch (RewardGrantNotFoundException)
            {
                return Problem(HttpStatusCode.NotFound, "reward_grant_not_found", "The reward grant was not found.");
            }
            catch (ArgumentException)
            {
                return Invalid("invalid_reward_grant", "The reward grant identifier is invalid.");
            }
            catch (Exception)
            {
                return Unavailable();
            }
        }

        [HttpGet]
        [Route("grant-operations")]
        [ResponseType(typeof(GrantOperationsHttpResponse))]
        public HttpResponseMessage GetPendingGrants(string? take = null)
        {
            if (!TryTake(take, out var value))
                return Invalid("invalid_reward_query", "The reward grant query is invalid.");
            try
            {
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new GrantOperationsHttpResponse(pending.Execute(value)));
            }
            catch (Exception)
            {
                return Unavailable();
            }
        }

        [HttpPost]
        [Route("grant-operations")]
        [ResponseType(typeof(GrantOperationHttpResponse))]
        public async Task<HttpResponseMessage> Grant(
            GrantRewardHttpRequest? body,
            CancellationToken cancellationToken)
        {
            if (runtimeStatus.GameReadiness != GameReadinessState.Ready)
                return GameNotReady();
            if (!ModelState.IsValid || body == null)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
            try
            {
                var actor = CommerceRewardHttpSupport.RequireActor(this);
                var result = await grantReward.ExecuteAsync(new GrantRewardCommand(
                        CommerceRewardHttpSupport.RequireText(body.PackageId),
                        CommerceRewardHttpSupport.RequireText(body.CrossplatformId),
                        body.ExpectedEntityId,
                        CommerceRewardHttpSupport.RequireText(body.ExpectedWorldId),
                        CommerceRewardHttpSupport.RequireText(body.ClientRequestKey),
                        null,
                        null,
                        null,
                        "Owner",
                        actor,
                        CommerceRewardHttpSupport.Correlation(this)),
                    cancellationToken).ConfigureAwait(false);
                var status = result.Operation.State == GrantOperationState.Completed
                    ? HttpStatusCode.OK
                    : HttpStatusCode.Accepted;
                return Request.CreateResponse(
                    status,
                    new GrantOperationHttpResponse(result.Operation, result.Reused));
            }
            catch (Exception exception)
            {
                return Map(exception);
            }
        }

        [HttpPost]
        [Route("grant-operations/{operationId}/confirm")]
        [ResponseType(typeof(GrantOperationHttpResponse))]
        public HttpResponseMessage Confirm(string operationId)
        {
            try
            {
                var operation = confirm.Execute(new ConfirmRewardGrantCommand(
                    operationId,
                    CommerceRewardHttpSupport.RequireActor(this),
                    CommerceRewardHttpSupport.Correlation(this),
                    DateTimeOffset.UtcNow));
                return Request.CreateResponse(HttpStatusCode.OK, new GrantOperationHttpResponse(operation));
            }
            catch (Exception exception)
            {
                return Map(exception);
            }
        }

        [HttpPost]
        [Route("grant-operations/{operationId}/refund")]
        [ResponseType(typeof(GrantOperationHttpResponse))]
        public HttpResponseMessage Refund(string operationId, RefundRewardGrantHttpRequest? body)
        {
            if (!ModelState.IsValid || body == null)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
            try
            {
                var operation = refund.Execute(new RefundRewardGrantCommand(
                    operationId,
                    CommerceRewardHttpSupport.RequireText(body.ClientRequestKey),
                    "Owner",
                    CommerceRewardHttpSupport.RequireActor(this),
                    CommerceRewardHttpSupport.Correlation(this),
                    DateTimeOffset.UtcNow));
                return Request.CreateResponse(HttpStatusCode.OK, new GrantOperationHttpResponse(operation));
            }
            catch (Exception exception)
            {
                return Map(exception);
            }
        }

        [HttpPost]
        [Route("grant-operations/{operationId}/compensate")]
        [ResponseType(typeof(GrantOperationHttpResponse))]
        public async Task<HttpResponseMessage> Compensate(
            string operationId,
            CompensateRewardGrantHttpRequest? body,
            CancellationToken cancellationToken)
        {
            if (runtimeStatus.GameReadiness != GameReadinessState.Ready)
                return GameNotReady();
            if (!ModelState.IsValid || body == null)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
            try
            {
                var result = await compensate.ExecuteAsync(new CompensateRewardGrantCommand(
                        operationId,
                        CommerceRewardHttpSupport.RequireText(body.ClientRequestKey),
                        "Owner",
                        CommerceRewardHttpSupport.RequireActor(this),
                        CommerceRewardHttpSupport.Correlation(this)),
                    cancellationToken).ConfigureAwait(false);
                var status = result.Operation.State == GrantOperationState.Completed
                    ? HttpStatusCode.OK
                    : HttpStatusCode.Accepted;
                return Request.CreateResponse(
                    status,
                    new GrantOperationHttpResponse(result.Operation, result.Reused));
            }
            catch (Exception exception)
            {
                return Map(exception);
            }
        }

        private HttpResponseMessage Map(Exception exception)
        {
            if (exception is RewardPackageNotFoundException)
                return Problem(HttpStatusCode.NotFound, "reward_package_not_found", "The reward package was not found.");
            if (exception is RewardGrantNotFoundException)
                return Problem(HttpStatusCode.NotFound, "reward_grant_not_found", "The reward grant was not found.");
            if (exception is RewardIdempotencyConflictException)
                return Conflict("reward_idempotency_conflict", "The client request key was already used for a different grant.");
            if (exception is RewardConcurrencyException)
                return Conflict("reward_concurrency_conflict", "The reward grant changed before the request completed.");
            if (exception is RewardCatalogValidationException catalog)
            {
                return string.Equals(catalog.Message, "reward_catalog_unavailable", StringComparison.Ordinal)
                    ? Problem(HttpStatusCode.ServiceUnavailable, catalog.Message, "The game resource catalog is unavailable.")
                    : Conflict(catalog.Message, "The game resource catalog changed.");
            }
            if (exception is ArgumentException)
                return Invalid("invalid_reward_grant", "The reward grant request is invalid.");
            if (exception is InvalidOperationException invalid &&
                (invalid.Message == "reward_grant_not_pending_reconciliation" ||
                 invalid.Message == "reward_grant_not_refundable" ||
                 invalid.Message == "reward_grant_not_compensatable"))
            {
                return Conflict(invalid.Message, "The reward grant is not in the required state.");
            }
            return Unavailable();
        }

        private static bool TryTake(string? text, out int take)
        {
            take = 50;
            return text == null ||
                int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out take) &&
                take >= 1 && take <= 200;
        }

        private HttpResponseMessage Invalid(string code, string detail) =>
            Problem(HttpStatusCode.BadRequest, code, detail);

        private HttpResponseMessage Conflict(string code, string detail) =>
            Problem(HttpStatusCode.Conflict, code, detail);

        private HttpResponseMessage Unavailable() =>
            Problem(HttpStatusCode.ServiceUnavailable, "rewards_unavailable", "The reward service is unavailable.");

        private HttpResponseMessage GameNotReady() =>
            Problem(HttpStatusCode.ServiceUnavailable, "game_not_ready", "The game is not ready for reward delivery.");

        private HttpResponseMessage Problem(HttpStatusCode status, string code, string detail) =>
            ApiProblemDetailsFactory.CreateResponse(Request, status, code, detail);
    }
}
