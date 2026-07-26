using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using LSTY.SevenDPanel.Application.ConsoleCommands;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    [Authorize(Roles = "Owner,Admin")]
    [RoutePrefix("api/v1/console/commands")]
    public sealed class ConsoleCommandCatalogController : ApiController
    {
        private readonly IConsoleCommandCatalogQuery catalogQuery;
        private readonly IPanelRuntimeStatus runtimeStatus;

        public ConsoleCommandCatalogController(
            IConsoleCommandCatalogQuery catalogQuery,
            IPanelRuntimeStatus runtimeStatus)
        {
            this.catalogQuery = catalogQuery ?? throw new ArgumentNullException(nameof(catalogQuery));
            this.runtimeStatus = runtimeStatus ?? throw new ArgumentNullException(nameof(runtimeStatus));
        }

        [HttpGet]
        [Route("catalog")]
        [ResponseType(typeof(ConsoleCommandCatalog))]
        public async Task<HttpResponseMessage> GetCatalog(CancellationToken cancellationToken)
        {
            if (runtimeStatus.GameReadiness != GameReadinessState.Ready)
                return Unavailable("game_not_ready", "The game is not ready to read console commands.");

            try
            {
                var catalog = await catalogQuery
                    .GetCatalogAsync(cancellationToken)
                    .ConfigureAwait(false);
                return Request.CreateResponse(HttpStatusCode.OK, catalog);
            }
            catch (ConsoleCommandCatalogUnavailableException)
            {
                return Unavailable(
                    "console_command_catalog_unavailable",
                    "The console command catalog is unavailable.");
            }
            catch (ObjectDisposedException)
            {
                return Unavailable(
                    "console_command_catalog_unavailable",
                    "The console command catalog is unavailable.");
            }
            catch (TimeoutException)
            {
                return Unavailable(
                    "game_thread_timeout",
                    "The game thread did not start the console command catalog read before the deadline.");
            }
        }

        private HttpResponseMessage Unavailable(string code, string detail) =>
            ApiProblemDetailsFactory.CreateResponse(
                Request,
                HttpStatusCode.ServiceUnavailable,
                code,
                detail);
    }
}
