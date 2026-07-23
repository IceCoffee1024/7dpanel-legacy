using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using LSTY.SevenDPanel.Application.ConsoleCommands;
using LSTY.SevenDPanel.Hosting;
using ApplicationConsoleCommandRequest =
    LSTY.SevenDPanel.Application.ConsoleCommands.ConsoleCommandRequest;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    [Authorize(Roles = "Owner,Admin")]
    [RoutePrefix("api/v1/console/commands")]
    public sealed class ConsoleCommandsController : ApiController
    {
        private readonly ExecuteConsoleCommandUseCase useCase;
        private readonly IPanelRuntimeStatus runtimeStatus;

        public ConsoleCommandsController(
            ExecuteConsoleCommandUseCase useCase,
            IPanelRuntimeStatus runtimeStatus)
        {
            this.useCase = useCase ?? throw new ArgumentNullException(nameof(useCase));
            this.runtimeStatus = runtimeStatus ?? throw new ArgumentNullException(nameof(runtimeStatus));
        }

        [HttpPost]
        [Route("")]
        [ResponseType(typeof(ConsoleCommandResponse))]
        public async Task<HttpResponseMessage> Post(
            ConsoleCommandRequest? request,
            CancellationToken cancellationToken)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Command))
            {
                return ApiProblemDetailsFactory.CreateResponse(
                    Request,
                    HttpStatusCode.BadRequest,
                    "console_command_required",
                    "A console command is required.");
            }

            if (runtimeStatus.GameReadiness != GameReadinessState.Ready)
            {
                return ApiProblemDetailsFactory.CreateResponse(
                    Request,
                    HttpStatusCode.ServiceUnavailable,
                    "game_not_ready",
                    "The game is not ready to execute console commands.");
            }

            var identity = User?.Identity as ClaimsIdentity;
            var actorSubject = identity?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(actorSubject))
            {
                return ApiProblemDetailsFactory.CreateResponse(
                    Request,
                    HttpStatusCode.Unauthorized,
                    "authentication_required",
                    "Authentication is required to execute console commands.");
            }

            try
            {
                var result = await useCase
                    .ExecuteAsync(
                        new ApplicationConsoleCommandRequest(actorSubject!, request.Command!),
                        cancellationToken)
                    .ConfigureAwait(false);
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new ConsoleCommandResponse(result.Command, result.Output));
            }
                catch (ConsoleCommandQueueFullException)
            {
                return ApiProblemDetailsFactory.CreateResponse(
                    Request,
                    HttpStatusCode.ServiceUnavailable,
                    "console_command_queue_full",
                    "The console command queue is full.");
            }
                catch (ConsoleCommandUnavailableException)
            {
                return ApiProblemDetailsFactory.CreateResponse(
                    Request,
                    HttpStatusCode.ServiceUnavailable,
                    "console_command_unavailable",
                    "The console command service is unavailable.");
            }
            catch (TimeoutException)
            {
                return ApiProblemDetailsFactory.CreateResponse(
                    Request,
                    HttpStatusCode.ServiceUnavailable,
                    "game_thread_timeout",
                    "The game thread did not start the console command before the deadline.");
            }
        }
    }

    public sealed class ConsoleCommandRequest
    {
        public string? Command { get; set; }
    }

    public sealed class ConsoleCommandResponse
    {
        public ConsoleCommandResponse(string command, IReadOnlyList<string> output)
        {
            Command = command;
            Output = output;
        }

        public string Command { get; }
        public IReadOnlyList<string> Output { get; }
    }
}
