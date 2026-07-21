using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using LSTY.SevenDPanel.Application.ConsoleCommands;
using LSTY.SevenDPanel.Hosting;

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

            try
            {
                var result = await useCase
                    .ExecuteAsync(request.Command!, cancellationToken)
                    .ConfigureAwait(false);
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new ConsoleCommandResponse(result.Command, result.Output));
            }
            catch (ConsoleCommandNotSupportedException)
            {
                return ApiProblemDetailsFactory.CreateResponse(
                    Request,
                    HttpStatusCode.BadRequest,
                    "console_command_not_supported",
                    "The console command is not supported.");
            }
            catch (ConsoleCommandBusyException)
            {
                return ApiProblemDetailsFactory.CreateResponse(
                    Request,
                    HttpStatusCode.ServiceUnavailable,
                    "console_command_busy",
                    "Another console command is already in progress.");
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
