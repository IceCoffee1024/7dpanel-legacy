using System;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using System.Web.Http.Description;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using LSTY.SevenDPanel.Application.ServerConfiguration;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    [Authorize(Roles = "Owner")]
    [RoutePrefix("api/v1/server-configuration")]
    public sealed class ServerConfigurationController : ApiController
    {
        private const string ConfigurationPath = "/api/v1/server-configuration";
        private readonly GetServerConfigurationUseCase getConfiguration;
        private readonly UpdateServerConfigurationUseCase updateConfiguration;

        public ServerConfigurationController(
            GetServerConfigurationUseCase getConfiguration,
            UpdateServerConfigurationUseCase updateConfiguration)
        {
            this.getConfiguration = getConfiguration ?? throw new ArgumentNullException(nameof(getConfiguration));
            this.updateConfiguration = updateConfiguration ?? throw new ArgumentNullException(nameof(updateConfiguration));
        }

        [HttpGet]
        [Route("")]
        [ResponseType(typeof(ServerConfigurationSnapshotResponse))]
        public HttpResponseMessage Get()
        {
            try
            {
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new ServerConfigurationSnapshotResponse(getConfiguration.Execute()));
            }
            catch (Exception exception) when (exception is System.IO.IOException
                || exception is UnauthorizedAccessException
                || exception is System.Xml.XmlException)
            {
                return Problem(
                    HttpStatusCode.InternalServerError,
                    "configuration_read_failed",
                    "The server configuration could not be read.");
            }
        }

        [HttpPut]
        [Route("{key}")]
        [ResponseType(typeof(ServerConfigurationUpdateResponse))]
        public HttpResponseMessage Put(string key, UpdateServerConfigurationHttpRequest? request)
        {
            if (!ModelState.IsValid || request == null || request.Value == null || string.IsNullOrWhiteSpace(request.Version))
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);

            ServerConfigurationUpdateResult result;
            try
            {
                result = updateConfiguration.Execute(
                    new UpdateServerConfigurationRequest(key, request.Value, request.Version!));
            }
            catch (Exception exception) when (exception is System.IO.IOException
                || exception is UnauthorizedAccessException
                || exception is System.Xml.XmlException)
            {
                return Problem(
                    HttpStatusCode.InternalServerError,
                    "configuration_write_failed",
                    "The server configuration could not be written.");
            }

            switch (result.Status)
            {
                case ServerConfigurationUpdateStatus.Updated:
                    return Request.CreateResponse(HttpStatusCode.OK, new ServerConfigurationUpdateResponse(result));
                case ServerConfigurationUpdateStatus.UnknownField:
                    return Problem(HttpStatusCode.BadRequest, "configuration_field_unknown", "The configuration field is not supported.");
                case ServerConfigurationUpdateStatus.ReadOnly:
                    return Problem(HttpStatusCode.Forbidden, "configuration_field_read_only", "The configuration field is read-only.");
                case ServerConfigurationUpdateStatus.InvalidValue:
                    return Problem(HttpStatusCode.BadRequest, "configuration_value_invalid", "The configuration value is invalid.");
                case ServerConfigurationUpdateStatus.Conflict:
                    return Problem(HttpStatusCode.Conflict, "configuration_version_conflict", "The configuration changed; refresh before saving again.");
                default:
                    return Problem(HttpStatusCode.InternalServerError, "configuration_write_failed", "The server configuration could not be written.");
            }
        }

        private HttpResponseMessage Problem(HttpStatusCode status, string code, string detail)
        {
            return ApiProblemDetailsFactory.CreateResponse(Request, status, code, detail, ConfigurationPath);
        }
    }
}
