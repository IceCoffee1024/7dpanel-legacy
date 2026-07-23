using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Web.Http;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using LSTY.SevenDPanel.Hosting.Authentication;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    [Authorize(Roles = "Owner,Admin,Viewer")]
    [RoutePrefix("api/v1/api-keys")]
    public sealed class ApiKeysController : ApiController
    {
        private const string ApiKeysPath = "/api/v1/api-keys";

        private readonly IPanelApiKeyStore apiKeyStore;

        public ApiKeysController(IPanelApiKeyStore apiKeyStore)
        {
            this.apiKeyStore = apiKeyStore ?? throw new ArgumentNullException(nameof(apiKeyStore));
        }

        [HttpGet]
        [Route("")]
        public HttpResponseMessage Get()
        {
            if (!TryGetSubject(out var subject, out _))
            {
                return ApiProblemDetailsFactory.CreateResponse(
                    Request,
                    HttpStatusCode.Unauthorized,
                    "authentication_required",
                    "Authentication is required to list API Keys.");
            }

            return Request.CreateResponse(
                HttpStatusCode.OK,
                apiKeyStore
                    .List(subject!, DateTimeOffset.UtcNow)
                    .Select(metadata => new ApiKeyMetadataResponse(metadata))
                    .ToArray());
        }

        [HttpPost]
        [Route("")]
        public HttpResponseMessage Post(ApiKeyCreateRequest? request)
        {
            if (!TryGetSubject(out var subject, out var credentialType))
            {
                return ApiProblemDetailsFactory.CreateResponse(
                    Request,
                    HttpStatusCode.Unauthorized,
                    "authentication_required",
                    "Authentication is required to create an API Key.");
            }
            if (!string.Equals(credentialType, "access_token", StringComparison.Ordinal))
            {
                return ApiProblemDetailsFactory.CreateResponse(
                    Request,
                    HttpStatusCode.Forbidden,
                    "access_token_required",
                    "An Access Token is required to create an API Key.");
            }
            if (!ModelState.IsValid)
            {
                var isExpirationError = ModelState
                    .Where(entry => entry.Value.Errors.Count > 0)
                    .All(entry => IsExpirationField(entry.Key));

                if (isExpirationError)
                {
                    return ApiProblemDetailsFactory.CreateResponse(
                        Request,
                        HttpStatusCode.BadRequest,
                        "invalid_api_key_expiration",
                        "The API Key expiration must be a valid UTC timestamp later than its creation time.");
                }

                return ApiProblemDetailsFactory.CreateResponse(
                    Request,
                    HttpStatusCode.BadRequest,
                    "invalid_api_key_name",
                    "The API Key name must be a string containing between 1 and 80 characters.");
            }

            var now = DateTimeOffset.UtcNow;
            var result = apiKeyStore.Create(
                subject!,
                request?.Name ?? string.Empty,
                now,
                request?.ExpiresAtUtc);
            if (result.Status == ApiKeyCreateStatus.Created && result.CreatedApiKey != null)
            {
                var created = result.CreatedApiKey;
                var response = Request.CreateResponse(
                    HttpStatusCode.Created,
                    new CreatedApiKeyResponse(created.ApiKey, created.Metadata));
                response.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true };
                return response;
            }

            return CreateProblem(result.Status);
        }

        [HttpDelete]
        [Route("{keyId}")]
        public HttpResponseMessage Delete(string keyId)
        {
            if (!TryGetSubject(out var subject, out var credentialType))
            {
                return ApiProblemDetailsFactory.CreateResponse(
                    Request,
                    HttpStatusCode.Unauthorized,
                    "authentication_required",
                    "Authentication is required to revoke an API Key.");
            }
            if (!string.Equals(credentialType, "access_token", StringComparison.Ordinal))
            {
                return ApiProblemDetailsFactory.CreateResponse(
                    Request,
                    HttpStatusCode.Forbidden,
                    "access_token_required",
                    "An Access Token is required to revoke an API Key.");
            }
            if (!apiKeyStore.Revoke(subject!, keyId, DateTimeOffset.UtcNow))
            {
                return ApiProblemDetailsFactory.CreateResponse(
                    Request,
                    HttpStatusCode.NotFound,
                    "api_key_not_found",
                    "The API Key was not found.",
                    ApiKeysPath);
            }

            return Request.CreateResponse(HttpStatusCode.NoContent);
        }

        private bool TryGetSubject(out string? subject, out string? credentialType)
        {
            var identity = User?.Identity as ClaimsIdentity;
            subject = identity?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            credentialType = identity?.FindFirst(PanelClaimTypes.CredentialType)?.Value;
            return !string.IsNullOrWhiteSpace(subject) && !string.IsNullOrWhiteSpace(credentialType);
        }

        private static bool IsExpirationField(string key)
        {
            var expirationField = nameof(ApiKeyCreateRequest.ExpiresAtUtc);
            return string.Equals(key, expirationField, StringComparison.OrdinalIgnoreCase)
                || key.EndsWith("." + expirationField, StringComparison.OrdinalIgnoreCase);
        }

        private HttpResponseMessage CreateProblem(ApiKeyCreateStatus status)
        {
            switch (status)
            {
                case ApiKeyCreateStatus.InvalidName:
                    return ApiProblemDetailsFactory.CreateResponse(
                        Request,
                        HttpStatusCode.BadRequest,
                        "invalid_api_key_name",
                        "The API Key name must contain between 1 and 80 characters.");
                case ApiKeyCreateStatus.InvalidExpiration:
                    return ApiProblemDetailsFactory.CreateResponse(
                        Request,
                        HttpStatusCode.BadRequest,
                        "invalid_api_key_expiration",
                        "The API Key expiration must be later than its creation time.");
                case ApiKeyCreateStatus.CapacityReached:
                    return ApiProblemDetailsFactory.CreateResponse(
                        Request,
                        HttpStatusCode.Conflict,
                        "api_key_capacity_reached",
                        "The maximum number of active API Keys has been reached.");
                case ApiKeyCreateStatus.SubjectNotFound:
                    return ApiProblemDetailsFactory.CreateResponse(
                        Request,
                        HttpStatusCode.Unauthorized,
                        "authentication_required",
                        "The current authentication is no longer valid.");
                default:
                    return ApiProblemDetailsFactory.CreateResponse(
                        Request,
                        HttpStatusCode.InternalServerError,
                        "api_key_creation_failed",
                        "The API Key could not be created.");
            }
        }
    }

    public sealed class ApiKeyCreateRequest
    {
        public string? Name { get; set; }
        public DateTimeOffset? ExpiresAtUtc { get; set; }
    }

    public sealed class CreatedApiKeyResponse
    {
        public CreatedApiKeyResponse(string apiKey, StoredApiKey metadata)
        {
            ApiKey = apiKey;
            Id = metadata.KeyId;
            Name = metadata.Name;
            CreatedAtUtc = metadata.CreatedUtc;
            ExpiresAtUtc = metadata.ExpiresUtc;
        }

        public string ApiKey { get; }
        public string Id { get; }
        public string Name { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset? ExpiresAtUtc { get; }
    }

    public sealed class ApiKeyMetadataResponse
    {
        public ApiKeyMetadataResponse(StoredApiKey metadata)
        {
            Id = metadata.KeyId;
            DisplayPrefix = "7dp_k_" + metadata.KeyId;
            Name = metadata.Name;
            CreatedAtUtc = metadata.CreatedUtc;
            LastUsedAtUtc = metadata.LastUsedUtc;
            ExpiresAtUtc = metadata.ExpiresUtc;
            Status = metadata.Status.ToString().ToLowerInvariant();
        }

        public string Id { get; }
        public string DisplayPrefix { get; }
        public string Name { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset? LastUsedAtUtc { get; }
        public DateTimeOffset? ExpiresAtUtc { get; }
        public string Status { get; }
    }
}
