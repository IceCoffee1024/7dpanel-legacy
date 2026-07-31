using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Web.Http;
using System.Web.Http.Description;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using LSTY.SevenDPanel.Application.Backups;
using LSTY.SevenDPanel.Application.Jobs;
using LSTY.SevenDPanel.Domain.Backups;
using LSTY.SevenDPanel.Domain.Jobs;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    [OwnerAuthorize]
    [RoutePrefix("api/v1/backups")]
    public sealed class BackupsController : ApiController
    {
        private const string BackupNotFoundError = "backup_not_found";
        private readonly CreateWorldBackup createWorld;
        private readonly CreatePanelDatabaseBackup createPanelDatabase;
        private readonly CreateServerConfigurationBackup createServerConfiguration;
        private readonly BackupCatalogService backups;
        private readonly IJobSubmissionStore submissions;
        private readonly StageRestore stageRestore;

        public BackupsController(
            CreateWorldBackup createWorld,
            CreatePanelDatabaseBackup createPanelDatabase,
            CreateServerConfigurationBackup createServerConfiguration,
            BackupCatalogService backups,
            IJobSubmissionStore submissions,
            StageRestore stageRestore)
        {
            this.createWorld = createWorld ?? throw new ArgumentNullException(nameof(createWorld));
            this.createPanelDatabase = createPanelDatabase ??
                throw new ArgumentNullException(nameof(createPanelDatabase));
            this.createServerConfiguration = createServerConfiguration ??
                throw new ArgumentNullException(nameof(createServerConfiguration));
            this.backups = backups ?? throw new ArgumentNullException(nameof(backups));
            this.submissions = submissions ?? throw new ArgumentNullException(nameof(submissions));
            this.stageRestore = stageRestore ?? throw new ArgumentNullException(nameof(stageRestore));
        }

        [HttpPost]
        [Route("world")]
        [ResponseType(typeof(JobHttpResponse))]
        public HttpResponseMessage CreateWorld(CreateWorldBackupHttpRequest? body)
        {
            try
            {
                var request = body ?? throw new ArgumentException("backup_request_invalid");
                var job = createWorld.Execute(
                    RequireActor(),
                    RequireText(request.WorldName),
                    RequireText(request.IdempotencyKey),
                    Normalize(request.CorrelationId));
                return Request.CreateResponse(HttpStatusCode.Accepted, new JobHttpResponse(job));
            }
            catch (ArgumentException)
            {
                return InvalidRequest("backup_request_invalid");
            }
            catch (InvalidOperationException exception)
            {
                return SubmissionConflict(exception);
            }
        }

        [HttpPost]
        [Route("panel-database")]
        [ResponseType(typeof(JobHttpResponse))]
        public HttpResponseMessage CreatePanelDatabase(CreateBackupHttpRequest? body) =>
            CreateSimple(body, createPanelDatabase.Execute);

        [HttpPost]
        [Route("server-configuration")]
        [ResponseType(typeof(JobHttpResponse))]
        public HttpResponseMessage CreateServerConfiguration(CreateBackupHttpRequest? body) =>
            CreateSimple(body, createServerConfiguration.Execute);

        [HttpGet]
        [Route("")]
        [ResponseType(typeof(BackupPageHttpResponse))]
        public HttpResponseMessage List(
            int pageSize = 50,
            string? kind = null,
            string? cursor = null)
        {
            try
            {
                if (pageSize < 1 || pageSize > 100) throw new FormatException();
                BackupKind? parsedKind = null;
                if (!string.IsNullOrWhiteSpace(kind))
                {
                    if (!Enum.TryParse(kind, false, out BackupKind value) ||
                        !Enum.IsDefined(typeof(BackupKind), value))
                    {
                        throw new FormatException();
                    }
                    parsedKind = value;
                }
                var parsedCursor = string.IsNullOrWhiteSpace(cursor)
                    ? null
                    : BackupCursorCodec.Decode(cursor!);
                var page = backups.List(new BackupQuery(pageSize, parsedKind, parsedCursor));
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new BackupPageHttpResponse(
                        page.Items,
                        page.NextCursor == null ? null : BackupCursorCodec.Encode(page.NextCursor)));
            }
            catch (FormatException exception)
            {
                return InvalidRequest(exception.Message == "invalid_backup_cursor"
                    ? "invalid_backup_cursor"
                    : "invalid_backup_query");
            }
            catch
            {
                return Problem(HttpStatusCode.ServiceUnavailable, "backups_unavailable",
                    "Backups are temporarily unavailable.");
            }
        }

        [HttpGet]
        [Route("{backupId:guid}/download")]
        public HttpResponseMessage Download(Guid backupId)
        {
            try
            {
                var download = backups.PrepareDownload(backupId);
                var response = Request.CreateResponse(HttpStatusCode.OK);
                response.Content = new StreamContent(download.Content);
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
                response.Content.Headers.ContentLength = download.ContentLength;
                response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
                {
                    FileName = download.AttachmentFileName
                };
                return response;
            }
            catch (KeyNotFoundException)
            {
                return NotFoundProblem();
            }
            catch (BackupCatalogException exception)
            {
                return CatalogProblem(exception);
            }
        }

        [HttpDelete]
        [Route("{backupId:guid}")]
        public HttpResponseMessage Delete(Guid backupId)
        {
            try
            {
                backups.Delete(backupId);
                return Request.CreateResponse(HttpStatusCode.NoContent);
            }
            catch (KeyNotFoundException)
            {
                return NotFoundProblem();
            }
            catch (BackupCatalogException exception)
            {
                return CatalogProblem(exception);
            }
        }

        [HttpPost]
        [Route("{backupId:guid}/restore")]
        [ResponseType(typeof(JobHttpResponse))]
        public HttpResponseMessage Restore(Guid backupId, RestoreBackupHttpRequest? body)
        {
            try
            {
                var request = body ?? throw new ArgumentException("restore_request_invalid");
                if (!request.StrongConfirmed)
                    throw new StageRestoreException(
                        StageRestore.StrongConfirmationRequiredError);
                var artifact = backups.Get(backupId);
                var now = DateTimeOffset.UtcNow;
                var payload = new RestorePayload(backupId, artifact.Kind, request.RestartAfterStage);
                var queued = submissions.Enqueue(
                    new NewJob(
                        JobKind.Restore,
                        RequireActor(),
                        null,
                        RequireText(request.IdempotencyKey),
                        Normalize(request.CorrelationId),
                        now),
                    payload);
                var staged = stageRestore.Execute(queued.Id, payload, request.StrongConfirmed);
                return Request.CreateResponse(HttpStatusCode.Accepted, new JobHttpResponse(staged));
            }
            catch (KeyNotFoundException)
            {
                return NotFoundProblem();
            }
            catch (StageRestoreException exception)
            {
                return RestoreProblem(exception.ErrorCode);
            }
            catch (ArgumentException)
            {
                return InvalidRequest("restore_request_invalid");
            }
            catch (InvalidOperationException exception)
            {
                return SubmissionConflict(exception);
            }
        }

        private HttpResponseMessage CreateSimple(
            CreateBackupHttpRequest? body,
            Func<CreateBackupRequest, JobRecord> execute)
        {
            try
            {
                var request = body ?? throw new ArgumentException("backup_request_invalid");
                var job = execute(new CreateBackupRequest(
                    RequireActor(),
                    RequireText(request.IdempotencyKey),
                    Normalize(request.CorrelationId),
                    DateTimeOffset.UtcNow));
                return Request.CreateResponse(HttpStatusCode.Accepted, new JobHttpResponse(job));
            }
            catch (ArgumentException)
            {
                return InvalidRequest("backup_request_invalid");
            }
            catch (InvalidOperationException exception)
            {
                return SubmissionConflict(exception);
            }
        }

        private HttpResponseMessage CatalogProblem(BackupCatalogException exception)
        {
            var status = exception.ErrorCode == BackupCatalogService.BackupInUseError ||
                         exception.ErrorCode == BackupCatalogService.IntegrityFailedError
                ? HttpStatusCode.Conflict
                : HttpStatusCode.ServiceUnavailable;
            return Problem(status, exception.ErrorCode, "The backup operation could not be completed.");
        }

        private HttpResponseMessage RestoreProblem(string errorCode)
        {
            if (errorCode == StageRestore.BackupNotFoundError) return NotFoundProblem();
            if (errorCode == StageRestore.StrongConfirmationRequiredError)
            {
                return Problem(
                    (HttpStatusCode)422,
                    errorCode,
                    "Explicit strong confirmation is required to stage a restore.");
            }
            return Problem(
                HttpStatusCode.Conflict,
                errorCode,
                "The restore could not be staged.");
        }

        private HttpResponseMessage SubmissionConflict(InvalidOperationException exception) =>
            Problem(
                HttpStatusCode.Conflict,
                exception.Message == "job_idempotency_conflict"
                    ? "job_idempotency_conflict"
                    : "job_submission_conflict",
                "The backup job conflicts with an existing request.");

        private HttpResponseMessage NotFoundProblem() =>
            Problem(HttpStatusCode.NotFound, BackupNotFoundError, "The backup was not found.");

        private HttpResponseMessage InvalidRequest(string code) =>
            Problem(HttpStatusCode.BadRequest, code, "The backup request is invalid.");

        private HttpResponseMessage Problem(HttpStatusCode status, string code, string detail) =>
            ApiProblemDetailsFactory.CreateResponse(Request, status, code, detail);

        private string RequireActor()
        {
            var identity = User?.Identity as ClaimsIdentity;
            var actor = identity?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return RequireText(actor);
        }

        private static string RequireText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty value is required.");
            return value!.Trim();
        }

        private static string? Normalize(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
    }
}
