using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Web.Http;
using System.Web.Http.Description;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using LSTY.SevenDPanel.Application.Automations;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    [OwnerAuthorize]
    [RoutePrefix("api/v1/automations")]
    public sealed class AutomationsController : ApiController
    {
        private readonly AutomationRuleUseCases rules;
        private readonly DryRunAutomationRuleUseCase dryRun;
        private readonly IAutomationExecutionQuery? executionQuery;

        public AutomationsController(
            AutomationRuleUseCases rules,
            DryRunAutomationRuleUseCase dryRun,
            IAutomationExecutionQuery? executionQuery = null)
        {
            this.rules = rules ?? throw new ArgumentNullException(nameof(rules));
            this.dryRun = dryRun ?? throw new ArgumentNullException(nameof(dryRun));
            this.executionQuery = executionQuery;
        }

        [HttpGet]
        [Route("")]
        [ResponseType(typeof(AutomationRuleHttpResponse[]))]
        public HttpResponseMessage List()
        {
            if (!TryActor(out var actor)) return InvalidActor();
            try
            {
                var response = rules.List(actor!)
                    .Select(AutomationHttpMapper.ToResponse)
                    .ToArray();
                return Request.CreateResponse(HttpStatusCode.OK, response);
            }
            catch (AutomationAuthorizationException)
            {
                return OwnerRequired();
            }
            catch
            {
                return Unavailable();
            }
        }

        [HttpGet]
        [Route("{ruleId}")]
        [ResponseType(typeof(AutomationRuleHttpResponse))]
        public HttpResponseMessage Find(string ruleId)
        {
            if (!AutomationHttpMapper.IsSafeIdentifier(ruleId)) return InvalidRuleId();
            if (!TryActor(out var actor)) return InvalidActor();
            try
            {
                var rule = rules.Find(ruleId, actor!);
                return rule == null
                    ? Problem(
                        HttpStatusCode.NotFound,
                        "automation_rule_not_found",
                        "The automation rule was not found.")
                    : Request.CreateResponse(
                        HttpStatusCode.OK,
                        AutomationHttpMapper.ToResponse(rule));
            }
            catch (AutomationAuthorizationException)
            {
                return OwnerRequired();
            }
            catch (ArgumentException)
            {
                return InvalidRuleId();
            }
            catch
            {
                return Unavailable();
            }
        }

        [HttpPost]
        [Route("")]
        [ResponseType(typeof(AutomationRuleHttpResponse))]
        public HttpResponseMessage Create(AutomationRuleHttpRequest? request)
        {
            if (!ModelState.IsValid || request == null)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
            if (!AutomationHttpMapper.TryToDraft(request, out var draft))
                return InvalidRuleRequest();
            if (!TryActor(out var actor)) return InvalidActor();

            try
            {
                var created = rules.Create(draft!, actor!);
                var response = Request.CreateResponse(
                    HttpStatusCode.Created,
                    AutomationHttpMapper.ToResponse(created));
                response.Headers.Location = new Uri(
                    Request.RequestUri.GetLeftPart(UriPartial.Authority) +
                    "/api/v1/automations/" + Uri.EscapeDataString(created.Id));
                return response;
            }
            catch (AutomationRuleValidationException)
            {
                return InvalidRule();
            }
            catch (AutomationVersionConflictException)
            {
                return VersionConflict();
            }
            catch (AutomationAuthorizationException)
            {
                return OwnerRequired();
            }
            catch (ArgumentException)
            {
                return InvalidRuleRequest();
            }
            catch (OverflowException)
            {
                return InvalidRuleRequest();
            }
            catch
            {
                return Unavailable();
            }
        }

        [HttpPut]
        [Route("{ruleId}")]
        [ResponseType(typeof(AutomationRuleHttpResponse))]
        public HttpResponseMessage Update(
            string ruleId,
            AutomationRuleHttpRequest? request)
        {
            if (!ModelState.IsValid || request == null)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
            if (!AutomationHttpMapper.IsSafeIdentifier(ruleId)) return InvalidRuleId();
            if (!string.Equals(ruleId, request.Id, StringComparison.Ordinal))
            {
                return Problem(
                    HttpStatusCode.BadRequest,
                    "automation_rule_id_mismatch",
                    "The route and request automation rule IDs must match.");
            }
            if (!AutomationHttpMapper.TryToDraft(request, out var draft))
                return InvalidRuleRequest();
            if (!TryActor(out var actor)) return InvalidActor();

            try
            {
                var updated = rules.Update(draft!, actor!);
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    AutomationHttpMapper.ToResponse(updated));
            }
            catch (AutomationRuleValidationException)
            {
                return InvalidRule();
            }
            catch (AutomationVersionConflictException)
            {
                return VersionConflict();
            }
            catch (AutomationAuthorizationException)
            {
                return OwnerRequired();
            }
            catch (ArgumentException)
            {
                return InvalidRuleRequest();
            }
            catch (OverflowException)
            {
                return InvalidRuleRequest();
            }
            catch
            {
                return Unavailable();
            }
        }

        [HttpDelete]
        [Route("{ruleId}")]
        [ResponseType(typeof(void))]
        public HttpResponseMessage Delete(
            string ruleId,
            [FromUri] long? expectedVersion = null)
        {
            if (!AutomationHttpMapper.IsSafeIdentifier(ruleId)) return InvalidRuleId();
            if (!expectedVersion.HasValue || expectedVersion.Value <= 0 ||
                expectedVersion.Value == long.MaxValue)
            {
                return Problem(
                    HttpStatusCode.BadRequest,
                    "automation_rule_version_invalid",
                    "A positive expectedVersion query value is required.");
            }
            if (!TryActor(out var actor)) return InvalidActor();

            try
            {
                rules.Delete(ruleId, expectedVersion.Value, actor!);
                return Request.CreateResponse(HttpStatusCode.NoContent);
            }
            catch (AutomationVersionConflictException)
            {
                return VersionConflict();
            }
            catch (AutomationAuthorizationException)
            {
                return OwnerRequired();
            }
            catch (ArgumentException)
            {
                return InvalidRuleRequest();
            }
            catch (OverflowException)
            {
                return InvalidRuleRequest();
            }
            catch
            {
                return Unavailable();
            }
        }

        [HttpPost]
        [Route("validate")]
        [ResponseType(typeof(AutomationValidationHttpResponse))]
        public HttpResponseMessage Validate(AutomationRuleHttpRequest? request)
        {
            if (!ModelState.IsValid || request == null)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
            if (!AutomationHttpMapper.TryToDraft(request, out var draft))
                return InvalidRuleRequest();
            if (!TryActor(out var actor)) return InvalidActor();

            try
            {
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    AutomationHttpMapper.ToResponse(rules.Validate(draft!, actor!)));
            }
            catch (AutomationAuthorizationException)
            {
                return OwnerRequired();
            }
            catch (ArgumentException)
            {
                return InvalidRuleRequest();
            }
            catch
            {
                return Unavailable();
            }
        }

        [HttpPost]
        [Route("dry-run")]
        [ResponseType(typeof(AutomationDryRunHttpResponse))]
        public HttpResponseMessage DryRun(AutomationDryRunHttpRequest? request)
        {
            if (!ModelState.IsValid || request == null)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
            if (request.HasUnknownProperties ||
                !AutomationHttpMapper.TryToDraft(request.Rule, out var draft) ||
                !AutomationHttpMapper.TryToSnapshot(request.Snapshot, out var snapshot))
            {
                return Problem(
                    HttpStatusCode.BadRequest,
                    "invalid_automation_dry_run_request",
                    "The automation dry-run request is invalid.");
            }
            if (!TryActor(out var actor)) return InvalidActor();

            try
            {
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    AutomationHttpMapper.ToResponse(dryRun.Execute(draft!, snapshot!, actor!)));
            }
            catch (AutomationAuthorizationException)
            {
                return OwnerRequired();
            }
            catch (ArgumentException)
            {
                return Problem(
                    HttpStatusCode.BadRequest,
                    "invalid_automation_dry_run_request",
                    "The automation dry-run request is invalid.");
            }
            catch
            {
                return Problem(
                    HttpStatusCode.ServiceUnavailable,
                    "automation_dry_run_unavailable",
                    "Automation dry-run is temporarily unavailable.");
            }
        }

        [HttpGet]
        [Route("executions")]
        [ResponseType(typeof(AutomationExecutionHttpResponse[]))]
        public HttpResponseMessage ListExecutions()
        {
            if (executionQuery == null) return ExecutionQueryUnavailable();
            try
            {
                var executions = executionQuery.ListExecutions(100)
                    .Select(execution => new AutomationExecutionHttpResponse(
                        execution,
                        Array.Empty<AutomationConditionExecutionResult>(),
                        Array.Empty<AutomationActionExecutionResult>()))
                    .ToArray();
                return Request.CreateResponse(HttpStatusCode.OK, executions);
            }
            catch { return ExecutionQueryUnavailable(); }
        }

        [HttpGet]
        [Route("executions/{executionId}")]
        [ResponseType(typeof(AutomationExecutionHttpResponse))]
        public HttpResponseMessage FindExecution(string executionId)
        {
            if (!AutomationHttpMapper.IsSafeIdentifier(executionId))
                return Problem(
                    HttpStatusCode.BadRequest,
                    "automation_execution_id_invalid",
                    "The automation execution ID is invalid.");
            if (executionQuery == null) return ExecutionQueryUnavailable();
            try
            {
                var execution = executionQuery.FindExecution(executionId);
                if (execution == null)
                    return Problem(
                        HttpStatusCode.NotFound,
                        "automation_execution_not_found",
                        "The automation execution was not found.");
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new AutomationExecutionHttpResponse(
                        execution,
                        executionQuery.ListConditionResults(execution.ExecutionId),
                        executionQuery.ListActionResults(execution.ExecutionId)));
            }
            catch { return ExecutionQueryUnavailable(); }
        }

        private bool TryActor(out AuthenticatedActor? actor)
        {
            actor = null;
            var identity = RequestContext.Principal?.Identity as ClaimsIdentity;
            var subject = identity?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(subject) || subject!.Length > 256)
                return false;
            actor = new AuthenticatedActor(subject, AutomationActorRole.Owner);
            return true;
        }

        private HttpResponseMessage InvalidActor() =>
            Problem(
                HttpStatusCode.Unauthorized,
                "automation_actor_invalid",
                "An authenticated Owner subject is required.");

        private HttpResponseMessage OwnerRequired() =>
            Problem(
                HttpStatusCode.Forbidden,
                "owner_required",
                "Owner access is required.");

        private HttpResponseMessage InvalidRuleId() =>
            Problem(
                HttpStatusCode.BadRequest,
                "automation_rule_id_invalid",
                "The automation rule ID is invalid.");

        private HttpResponseMessage InvalidRuleRequest() =>
            Problem(
                HttpStatusCode.BadRequest,
                "invalid_automation_rule_request",
                "The automation rule request is invalid.");

        private HttpResponseMessage InvalidRule() =>
            Problem(
                HttpStatusCode.BadRequest,
                "automation_rule_invalid",
                "The automation rule failed validation.");

        private HttpResponseMessage VersionConflict() =>
            Problem(
                HttpStatusCode.Conflict,
                "automation_rule_version_conflict",
                "The automation rule changed before the operation completed.");

        private HttpResponseMessage Unavailable() =>
            Problem(
                HttpStatusCode.ServiceUnavailable,
                "automation_rules_unavailable",
                "Automation rules are temporarily unavailable.");

        private HttpResponseMessage ExecutionQueryUnavailable() =>
            Problem(
                HttpStatusCode.NotImplemented,
                "automation_execution_query_unavailable",
                "Automation execution queries are not available from the current application contract.");

        private HttpResponseMessage Problem(
            HttpStatusCode status,
            string code,
            string detail) =>
            ApiProblemDetailsFactory.CreateResponse(Request, status, code, detail);
    }
}
