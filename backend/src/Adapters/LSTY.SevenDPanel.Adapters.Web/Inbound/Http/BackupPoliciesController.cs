using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using System.Web.Http.Description;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using LSTY.SevenDPanel.Application.Backups;
using LSTY.SevenDPanel.Domain.Backups;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    [OwnerAuthorize]
    [RoutePrefix("api/v1/backups/policies")]
    public sealed class BackupPoliciesController : ApiController
    {
        private const string InvalidPolicyError = "backup_policy_invalid";
        private const string RowVersionConflictError = "backup_policy_row_version_conflict";
        private readonly BackupPolicyService policies;

        public BackupPoliciesController(BackupPolicyService policies)
        {
            this.policies = policies ?? throw new ArgumentNullException(nameof(policies));
        }

        [HttpGet]
        [Route("")]
        [ResponseType(typeof(BackupPolicyHttpResponse[]))]
        public HttpResponseMessage List() => Request.CreateResponse(
            HttpStatusCode.OK,
            policies.List().Select(policy => new BackupPolicyHttpResponse(policy)).ToArray());

        [HttpPut]
        [Route("{kind}")]
        [ResponseType(typeof(BackupPolicyHttpResponse))]
        public HttpResponseMessage Update(string kind, BackupPolicyWriteHttpRequest? request)
        {
            if (!ModelState.IsValid || request == null || !TryParseKind(kind, out var parsedKind))
                return InvalidRequest();

            try
            {
                var saved = policies.Save(new BackupPolicyDefinition(
                    parsedKind,
                    request.Enabled,
                    request.CronExpression ?? string.Empty,
                    request.TimeZoneId ?? string.Empty,
                    request.BackupRootId ?? string.Empty,
                    request.RetentionCount,
                    request.RetentionDays,
                    request.CompressionEnabled,
                    request.ExpectedRowVersion));
                return Request.CreateResponse(HttpStatusCode.OK, new BackupPolicyHttpResponse(saved));
            }
            catch (InvalidOperationException exception)
                when (exception.Message == RowVersionConflictError)
            {
                return Problem(HttpStatusCode.Conflict, RowVersionConflictError,
                    "The backup policy was changed by another request.");
            }
            catch (ArgumentOutOfRangeException)
            {
                return InvalidRequest();
            }
            catch (ArgumentException)
            {
                return InvalidRequest();
            }
        }

        private static bool TryParseKind(string? value, out BackupKind kind)
        {
            switch (value)
            {
                case "World":
                    kind = BackupKind.World;
                    return true;
                case "PanelDatabase":
                    kind = BackupKind.PanelDatabase;
                    return true;
                case "ServerConfiguration":
                    kind = BackupKind.ServerConfiguration;
                    return true;
                default:
                    kind = default;
                    return false;
            }
        }

        private HttpResponseMessage InvalidRequest() =>
            Problem(HttpStatusCode.BadRequest, InvalidPolicyError, "The backup policy request is invalid.");

        private HttpResponseMessage Problem(HttpStatusCode status, string code, string detail) =>
            ApiProblemDetailsFactory.CreateResponse(Request, status, code, detail);
    }

    public sealed class BackupPolicyWriteHttpRequest
    {
        public bool Enabled { get; set; }
        public string? CronExpression { get; set; }
        public string? TimeZoneId { get; set; }
        public string? BackupRootId { get; set; }
        public int RetentionCount { get; set; }
        public int RetentionDays { get; set; }
        public bool CompressionEnabled { get; set; }
        public long ExpectedRowVersion { get; set; }
    }

    public sealed class BackupPolicyHttpResponse
    {
        public BackupPolicyHttpResponse(BackupPolicyDefinition policy)
        {
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            Kind = policy.Kind.ToString();
            Enabled = policy.Enabled;
            CronExpression = policy.CronExpression;
            TimeZoneId = policy.TimeZoneId;
            BackupRootId = policy.BackupRootId;
            RetentionCount = policy.RetentionCount;
            RetentionDays = policy.RetentionDays;
            CompressionEnabled = policy.CompressionEnabled;
            RowVersion = policy.RowVersion;
        }

        public string Kind { get; }
        public bool Enabled { get; }
        public string CronExpression { get; }
        public string TimeZoneId { get; }
        public string BackupRootId { get; }
        public int RetentionCount { get; }
        public int RetentionDays { get; }
        public bool CompressionEnabled { get; }
        public long RowVersion { get; }
    }
}
