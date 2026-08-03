using System;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    [Authorize]
    [RoutePrefix("api/v1/server-operations")]
    public sealed class ServerOperationsController : ApiController
    {
        private readonly RestartServerUseCase restartUseCase;
        private readonly ShutdownServerUseCase shutdownUseCase;
        private readonly GetServerOperationUseCase getOperationUseCase;

        public ServerOperationsController(
            RestartServerUseCase restartUseCase,
            ShutdownServerUseCase shutdownUseCase,
            GetServerOperationUseCase getOperationUseCase)
        {
            this.restartUseCase = restartUseCase ?? throw new ArgumentNullException(nameof(restartUseCase));
            this.shutdownUseCase = shutdownUseCase ?? throw new ArgumentNullException(nameof(shutdownUseCase));
            this.getOperationUseCase = getOperationUseCase ?? throw new ArgumentNullException(nameof(getOperationUseCase));
        }

        [HttpGet]
        [Route("{operationId}")]
        [Authorize(Roles = "Owner,Admin,Viewer")]
        [ResponseType(typeof(ServerOperationStatusHttpResponse))]
        public HttpResponseMessage Get(string operationId)
        {
            try
            {
                var operation = getOperationUseCase.Execute(operationId);
                if (operation == null)
                {
                    return ApiProblemDetailsFactory.CreateResponse(
                        Request, HttpStatusCode.NotFound, "operation_not_found", "The server operation was not found.");
                }
                return Request.CreateResponse(HttpStatusCode.OK, new ServerOperationStatusHttpResponse(operation));
            }
            catch (ArgumentException)
            {
                return ApiProblemDetailsFactory.CreateResponse(
                    Request, HttpStatusCode.NotFound, "operation_not_found", "The server operation was not found.");
            }
            catch (ServerOperationSourceUnavailableException)
            {
                return ApiProblemDetailsFactory.CreateResponse(
                    Request, HttpStatusCode.ServiceUnavailable,
                    "operation_status_unavailable", "The server operation status is unavailable.");
            }
        }

        [HttpPost]
        [Route("restart")]
        [Authorize(Roles = "Owner")]
        [ResponseType(typeof(RestartServerOperationHttpResponse))]
        public async Task<HttpResponseMessage> Restart(
            ConfirmedServerOperationRequest? request,
            CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid || request == null)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);

            var actorSubject = GetActorSubject();
            if (actorSubject == null) return AuthenticationRequired();

            try
            {
                var result = await restartUseCase.ExecuteAsync(
                        actorSubject,
                        request.Confirmed,
                        cancellationToken)
                    .ConfigureAwait(false);
                return Request.CreateResponse(
                    HttpStatusCode.Accepted,
                    new RestartServerOperationHttpResponse(result));
            }
            catch (ServerOperationConfirmationRequiredException)
            {
                return ConfirmationRequired();
            }
            catch (ServerOperationBusyException)
            {
                return OperationBusy();
            }
            catch (ServerOperationAuditUnavailableException)
            {
                return AuditUnavailable();
            }
            catch (ServerOperationFailedException exception)
            {
                var code = ServerOperationCodeContract.IsFailure(
                    ServerOperationCodeContract.RestartScript,
                    exception.FailureCode)
                    ? exception.FailureCode
                    : ServerOperationCodeContract.RestartScriptStartFailed;
                return ApiProblemDetailsFactory.CreateResponse(
                    Request,
                    HttpStatusCode.ServiceUnavailable,
                    code,
                    "The configured restart script could not be started.");
            }
            catch (OperationCanceledException)
            {
                return ApiProblemDetailsFactory.CreateResponse(
                    Request,
                    HttpStatusCode.ServiceUnavailable,
                    "operation_cancelled",
                    "The restart request was cancelled.");
            }
        }

        [HttpPost]
        [Route("shutdown")]
        [Authorize(Roles = "Owner")]
        [ResponseType(typeof(ShutdownServerOperationHttpResponse))]
        public async Task<HttpResponseMessage> Shutdown(
            ConfirmedServerOperationRequest? request,
            CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid || request == null)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);

            var actorSubject = GetActorSubject();
            if (actorSubject == null) return AuthenticationRequired();

            try
            {
                var result = await shutdownUseCase.ExecuteAsync(
                        actorSubject,
                        request.Confirmed,
                        cancellationToken)
                    .ConfigureAwait(false);
                return Request.CreateResponse(
                    HttpStatusCode.Accepted,
                    new ShutdownServerOperationHttpResponse(result));
            }
            catch (ServerOperationConfirmationRequiredException)
            {
                return ConfirmationRequired();
            }
            catch (ServerOperationBusyException)
            {
                return OperationBusy();
            }
            catch (ServerOperationAuditUnavailableException)
            {
                return AuditUnavailable();
            }
            catch (ServerOperationFailedException exception)
            {
                var code = ServerOperationCodeContract.IsFailure(
                    ServerOperationCodeContract.Shutdown,
                    exception.FailureCode)
                    ? exception.FailureCode
                    : ServerOperationCodeContract.ShutdownFailed;
                return ApiProblemDetailsFactory.CreateResponse(
                    Request,
                    HttpStatusCode.ServiceUnavailable,
                    code,
                    "The server shutdown request could not be accepted.");
            }
            catch (OperationCanceledException)
            {
                return ApiProblemDetailsFactory.CreateResponse(
                    Request,
                    HttpStatusCode.ServiceUnavailable,
                    ServerOperationCodeContract.ShutdownCancelled,
                    "The server shutdown request was cancelled.");
            }
        }

        private string? GetActorSubject()
        {
            var identity = User?.Identity as ClaimsIdentity;
            var subject = identity?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return string.IsNullOrWhiteSpace(subject) ? null : subject;
        }

        private HttpResponseMessage AuthenticationRequired() =>
            ApiProblemDetailsFactory.CreateResponse(
                Request,
                HttpStatusCode.Unauthorized,
                "authentication_required",
                "Authentication is required to request a server operation.");

        private HttpResponseMessage ConfirmationRequired() =>
            ApiProblemDetailsFactory.CreateResponse(
                Request,
                HttpStatusCode.BadRequest,
                "confirmation_required",
                "Explicit confirmation is required for this server operation.");

        private HttpResponseMessage OperationBusy() =>
            ApiProblemDetailsFactory.CreateResponse(
                Request,
                HttpStatusCode.Conflict,
                "operation_in_progress",
                "A server operation is already in progress.");

        private HttpResponseMessage AuditUnavailable() =>
            ApiProblemDetailsFactory.CreateResponse(
                Request,
                HttpStatusCode.ServiceUnavailable,
                "audit_unavailable",
                "The server operation audit trail is unavailable.");
    }
}
