using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.DependencyInjection;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Application.Backups;
using LSTY.SevenDPanel.Application.Jobs;
using LSTY.SevenDPanel.Domain.Backups;
using LSTY.SevenDPanel.Domain.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Owin;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace LSTY.SevenDPanel.Tests.Web
{
    public sealed class JobsBackupsSchedulesAuthorizationTests
    {
        [Fact]
        public async Task Owner_can_create_list_download_and_delete_a_verified_backup()
        {
            using var host = CreateHost("Owner");

            using var create = await host.Client.PostAsync(
                "api/v1/backups/world",
                Json("{\"worldName\":\"Navezgane\",\"idempotencyKey\":\"backup-1\"}"));
            using var list = await host.Client.GetAsync("api/v1/backups?pageSize=10");
            var listJson = JObject.Parse(await list.Content.ReadAsStringAsync());
            var backupId = (string?)listJson["items"]?[0]?["id"];
            using var download = await host.Client.GetAsync(
                "api/v1/backups/" + backupId + "/download");
            var content = await download.Content.ReadAsByteArrayAsync();
            using var delete = await host.Client.DeleteAsync("api/v1/backups/" + backupId);

            Assert.Equal(HttpStatusCode.Accepted, create.StatusCode);
            Assert.Equal("WorldBackup", (string?)JObject.Parse(
                await create.Content.ReadAsStringAsync())["kind"]);
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            Assert.Equal("World", (string?)listJson["items"]?[0]?["kind"]);
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.Equal("application/zip", download.Content.Headers.ContentType?.MediaType);
            Assert.Equal(host.Store.ArchiveBytes, content);
            Assert.NotNull(download.Content.Headers.ContentDisposition?.FileName);
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        }

        [Fact]
        public async Task Owner_can_stage_restore_and_receives_pending_restart_job()
        {
            using var host = CreateHost("Owner");

            using var response = await host.Client.PostAsync(
                "api/v1/backups/" + host.Store.Artifact.Id + "/restore",
                Json("{\"idempotencyKey\":\"restore-1\",\"restartAfterStage\":false,\"strongConfirmed\":true}"));
            var json = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Equal("Restore", (string?)json["kind"]);
            Assert.Equal("PendingRestart", (string?)json["status"]);
            Assert.True(host.Store.MarkerCreated);
        }

        [Fact]
        public async Task Restore_without_strong_confirmation_is_rejected_before_enqueue()
        {
            using var host = CreateHost("Owner");

            using var response = await host.Client.PostAsync(
                "api/v1/backups/" + host.Store.Artifact.Id + "/restore",
                Json("{\"idempotencyKey\":\"restore-unconfirmed\",\"restartAfterStage\":false}"));
            var json = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal((HttpStatusCode)422, response.StatusCode);
            Assert.Equal(StageRestore.StrongConfirmationRequiredError, (string?)json["code"]);
            Assert.Equal(0, host.Store.JobCount);
            Assert.False(host.Store.MarkerCreated);
        }

        [Theory]
        [InlineData(null, HttpStatusCode.Unauthorized)]
        [InlineData("Admin", HttpStatusCode.Forbidden)]
        [InlineData("Viewer", HttpStatusCode.Forbidden)]
        public async Task Backups_are_owner_only(string? role, HttpStatusCode expected)
        {
            using var host = CreateHost(role);
            using var response = await host.Client.GetAsync("api/v1/backups");
            Assert.Equal(expected, response.StatusCode);
        }

        [Fact]
        public async Task Missing_backup_returns_stable_problem_details()
        {
            using var host = CreateHost("Owner");
            using var response = await host.Client.GetAsync(
                "api/v1/backups/" + Guid.NewGuid() + "/download");
            var json = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("backup_not_found", (string?)json["code"]);
        }

        private static StringContent Json(string value) =>
            new StringContent(value, Encoding.UTF8, "application/json");

        private static Host CreateHost(string? role)
        {
            var store = new BackupStore();
            var services = new ServiceCollection();
            services.AddSingleton(store);
            services.AddSingleton<IJobStore>(store);
            services.AddSingleton<IJobSubmissionStore>(store);
            services.AddSingleton<IJobPayloadReader>(store);
            services.AddSingleton<IBackupCatalog>(store);
            services.AddSingleton<IBackupArchiveStorage>(store);
            services.AddSingleton<IPendingRestoreMarkerStore>(store);
            services.AddSingleton<IRestartScriptLauncher>(store);
            services.AddSingleton(serviceProvider => new CreateWorldBackup(
                serviceProvider.GetRequiredService<IJobSubmissionStore>(),
                () => BackupStore.Now));
            services.AddSingleton<CreatePanelDatabaseBackup>();
            services.AddSingleton<CreateServerConfigurationBackup>();
            services.AddSingleton<BackupCatalogService>();
            services.AddSingleton(serviceProvider => new StageRestore(
                serviceProvider.GetRequiredService<IBackupCatalog>(),
                serviceProvider.GetRequiredService<IJobStore>(),
                serviceProvider.GetRequiredService<IPendingRestoreMarkerStore>(),
                serviceProvider.GetRequiredService<IRestartScriptLauncher>(),
                () => BackupStore.Now));
            var provider = services.BuildServiceProvider();
            var configuration = new HttpConfiguration
            {
                DependencyResolver = new MicrosoftDependencyResolver(provider)
            };
            configuration.MapHttpAttributeRoutes();
            configuration.Formatters.Remove(configuration.Formatters.XmlFormatter);
            configuration.Formatters.JsonFormatter.SerializerSettings.ContractResolver =
                new CamelCasePropertyNamesContractResolver();
            configuration.MessageHandlers.Add(new PrincipalHandler(role));
            configuration.EnsureInitialized();
            return new Host(provider, configuration, store);
        }

        private sealed class Host : IDisposable
        {
            private readonly ServiceProvider provider;
            private readonly HttpConfiguration configuration;

            public Host(ServiceProvider provider, HttpConfiguration configuration, BackupStore store)
            {
                this.provider = provider;
                this.configuration = configuration;
                Store = store;
                Client = new HttpClient(new HttpServer(configuration))
                {
                    BaseAddress = new Uri("http://localhost/")
                };
            }

            public HttpClient Client { get; }
            public BackupStore Store { get; }

            public void Dispose()
            {
                Client.Dispose();
                configuration.Dispose();
                provider.Dispose();
            }
        }

        private sealed class PrincipalHandler : DelegatingHandler
        {
            private readonly string? role;
            public PrincipalHandler(string? role) => this.role = role;

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                var identity = role == null
                    ? new ClaimsIdentity()
                    : new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, "owner-1"),
                        new Claim(ClaimTypes.Role, role)
                    }, "Test");
                var principal = new ClaimsPrincipal(identity);
                var owin = new OwinContext();
                owin.Authentication.User = principal;
                request.SetOwinContext(owin);
                request.GetRequestContext().Principal = principal;
                return base.SendAsync(request, cancellationToken);
            }
        }

        public sealed class BackupStore : IJobStore, IJobSubmissionStore, IJobPayloadReader,
            IBackupCatalog, IBackupArchiveStorage, IPendingRestoreMarkerStore,
            IRestartScriptLauncher
        {
            public static readonly DateTimeOffset Now =
                new DateTimeOffset(2026, 7, 27, 2, 0, 0, TimeSpan.Zero);
            private readonly List<JobRecord> jobs = new List<JobRecord>();
            private readonly Dictionary<Guid, RestorePayload> restorePayloads =
                new Dictionary<Guid, RestorePayload>();
            private bool artifactExists = true;

            public BackupStore()
            {
                ArchiveBytes = Encoding.UTF8.GetBytes("verified-backup");
                Artifact = new BackupArtifact(
                    Guid.NewGuid(),
                    BackupKind.World,
                    "backup-root",
                    "opaque.zip",
                    ArchiveBytes.LongLength,
                    Sha256(ArchiveBytes),
                    "Navezgane",
                    "3.0.1-b4",
                    "Verified",
                    Now.AddDays(-1),
                    Guid.NewGuid(),
                    StageRestore.SupportedManifestVersion);
            }

            public int JobCount => jobs.Count;

            public byte[] ArchiveBytes { get; }
            public BackupArtifact Artifact { get; }
            public bool MarkerCreated { get; private set; }

            public JobRecord Enqueue(NewJob job) => Add(job);
            public JobRecord Enqueue(NewJob job, WorldBackupPayload payload) => Add(job);
            public JobRecord Enqueue(NewJob job, PanelDatabaseBackupPayload payload) => Add(job);
            public JobRecord Enqueue(NewJob job, ServerConfigurationBackupPayload payload) => Add(job);
            public JobRecord Enqueue(NewJob job, RestorePayload payload)
            {
                var record = Add(job);
                restorePayloads.Add(record.Id, payload);
                return record;
            }
            public JobRecord Enqueue(NewJob job, ScheduledConsoleCommandPayload payload) => Add(job);
            public JobRecord Enqueue(NewJob job, ScheduledRestartPayload payload) => Add(job);
            public JobRecord Enqueue(NewJob job, ScheduledAnnouncementPayload payload) => Add(job);

            public JobRecord? TryClaimNext(string workerId, DateTimeOffset now) => null;

            public bool TryTransition(Guid jobId, long expectedRowVersion, JobStatus expected,
                JobStatus next, JobCompletion completion)
            {
                var index = jobs.FindIndex(job => job.Id == jobId);
                if (index < 0 || jobs[index].RowVersion != expectedRowVersion ||
                    jobs[index].Status != expected) return false;
                jobs[index] = jobs[index] with
                {
                    Status = next,
                    CompletedAtUtc = next == JobStatus.PendingRestart
                        ? null
                        : completion.CompletedAtUtc,
                    Progress = completion.Progress,
                    ErrorCode = completion.ErrorCode,
                    RowVersion = jobs[index].RowVersion + 1
                };
                return true;
            }

            public JobRecord Get(Guid jobId) => jobs.Single(job => job.Id == jobId);

            public PagedResult<JobRecord, JobCursor> List(JobQuery query) =>
                new PagedResult<JobRecord, JobCursor>(jobs
                    .Where(job => query.Kind == null || job.Kind == query.Kind)
                    .Where(job => query.Status == null || job.Status == query.Status)
                    .ToArray(), null);

            public BackupArtifact Add(CompletedBackup backup) => throw new NotSupportedException();
            BackupArtifact IBackupCatalog.Get(Guid backupId) =>
                artifactExists && backupId == Artifact.Id
                    ? Artifact
                    : throw new KeyNotFoundException();
            public PagedResult<BackupArtifact, BackupCursor> List(BackupQuery query) =>
                new PagedResult<BackupArtifact, BackupCursor>(
                    artifactExists ? new[] { Artifact } : Array.Empty<BackupArtifact>(), null);
            public bool Delete(Guid backupId)
            {
                if (!artifactExists || backupId != Artifact.Id) return false;
                artifactExists = false;
                return true;
            }

            public Stream OpenRead(BackupArtifact artifact) =>
                new MemoryStream(ArchiveBytes, writable: false);
            void IBackupArchiveStorage.Delete(BackupArtifact artifact)
            {
            }

            public bool TryCreateMarker(BackupArtifact artifact, JobRecord pendingRestartJob)
            {
                if (MarkerCreated) return false;
                MarkerCreated = true;
                return true;
            }

            public DateTimeOffset StartConfiguredScript() => Now;
            public WorldBackupPayload GetWorldBackup(Guid jobId) => throw new NotSupportedException();
            public PanelDatabaseBackupPayload GetPanelDatabaseBackup(Guid jobId) => throw new NotSupportedException();
            public ServerConfigurationBackupPayload GetServerConfigurationBackup(Guid jobId) => throw new NotSupportedException();
            public RestorePayload GetRestore(Guid jobId) => restorePayloads[jobId];
            public ScheduledConsoleCommandPayload GetScheduledConsoleCommand(Guid jobId) => throw new NotSupportedException();
            public ScheduledRestartPayload GetScheduledRestart(Guid jobId) => throw new NotSupportedException();
            public ScheduledAnnouncementPayload GetScheduledAnnouncement(Guid jobId) => throw new NotSupportedException();

            private JobRecord Add(NewJob job)
            {
                var record = new JobRecord(
                    Guid.NewGuid(), job.Kind, JobStatus.Queued, job.ActorSubject,
                    job.SourceScheduleId, job.IdempotencyKey, job.CorrelationId,
                    job.CreatedAtUtc, null, null, null, null, null, 0);
                jobs.Add(record);
                return record;
            }

            private static string Sha256(byte[] value)
            {
                using var sha = SHA256.Create();
                return string.Concat(sha.ComputeHash(value).Select(item => item.ToString("x2")));
            }
        }
    }
}
