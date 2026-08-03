using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.DependencyInjection;
using LSTY.SevenDPanel.Application.Announcements;
using LSTY.SevenDPanel.Application.Backups;
using LSTY.SevenDPanel.Application.Jobs;
using LSTY.SevenDPanel.Application.Schedules;
using LSTY.SevenDPanel.Domain.Backups;
using LSTY.SevenDPanel.Domain.Jobs;
using LSTY.SevenDPanel.Domain.Schedules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Owin;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace LSTY.SevenDPanel.Tests.Web
{
    [Trait("Capability", "Operations")]
    [Trait("Boundary", "Web")]
    public sealed class JobsBackupsSchedulesApiTests
    {
        [Fact]
        public async Task Owner_can_list_get_and_cancel_a_queued_job()
        {
            using var host = CreateHost("Owner", JobStatus.Queued);

            using var list = await host.Client.GetAsync("api/v1/jobs?pageSize=10");
            var listJson = JObject.Parse(await list.Content.ReadAsStringAsync());
            var id = (string?)listJson["items"]?[0]?["id"];
            using var get = await host.Client.GetAsync("api/v1/jobs/" + id);
            using var cancel = await host.Client.PostAsync(
                "api/v1/jobs/" + id + "/cancel",
                new StringContent(string.Empty));
            var cancelJson = JObject.Parse(await cancel.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            Assert.Equal("WorldBackup", (string?)listJson["items"]?[0]?["kind"]);
            Assert.Equal("Queued", (string?)listJson["items"]?[0]?["status"]);
            Assert.Equal(HttpStatusCode.OK, get.StatusCode);
            Assert.Equal(HttpStatusCode.Accepted, cancel.StatusCode);
            Assert.Equal("Cancelled", (string?)cancelJson["status"]);
        }

        [Theory]
        [InlineData(null, HttpStatusCode.Unauthorized)]
        [InlineData("Admin", HttpStatusCode.Forbidden)]
        [InlineData("Viewer", HttpStatusCode.Forbidden)]
        public async Task Jobs_are_owner_only(string? role, HttpStatusCode expected)
        {
            using var host = CreateHost(role, JobStatus.Queued);
            using var response = await host.Client.GetAsync("api/v1/jobs");
            Assert.Equal(expected, response.StatusCode);
        }

        [Fact]
        public async Task Running_job_cancel_returns_stable_problem_details()
        {
            using var host = CreateHost("Owner", JobStatus.Running);
            using var response = await host.Client.PostAsync(
                "api/v1/jobs/" + host.Store.Current.Id + "/cancel",
                new StringContent(string.Empty));
            var problem = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("job_not_cancellable", (string?)problem["code"]);
        }

        [Fact]
        public async Task Owner_can_create_a_typed_restart_schedule()
        {
            using var host = CreateHost("Owner", JobStatus.Queued);
            using var response = await host.Client.PostAsync(
                "api/v1/schedules",
                new StringContent(
                    "{\"name\":\"nightly\",\"cronExpression\":\"0 3 * * *\"," +
                    "\"timeZoneId\":\"UTC\",\"enabled\":true," +
                    "\"concurrencyPolicy\":\"SkipIfRunning\"," +
                    "\"kind\":\"ScheduledRestart\",\"countdownSeconds\":60}",
                    Encoding.UTF8,
                    "application/json"));
            var json = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal("ScheduledRestart", (string?)json["kind"]);
            Assert.Equal(60, (int?)json["countdownSeconds"]);
        }

        [Theory]
        [InlineData("Owner", HttpStatusCode.Accepted)]
        [InlineData("Admin", HttpStatusCode.Accepted)]
        [InlineData("Viewer", HttpStatusCode.Forbidden)]
        public async Task Immediate_announcement_allows_owner_and_admin_only(
            string role,
            HttpStatusCode expected)
        {
            using var host = CreateHost(role, JobStatus.Queued);
            using var response = await host.Client.PostAsync(
                "api/v1/announcements",
                new StringContent(
                    "{\"messageText\":\"Server restarts soon\"}",
                    Encoding.UTF8,
                    "application/json"));

            Assert.Equal(expected, response.StatusCode);
        }

        [Fact]
        public async Task Owner_can_list_and_update_fixed_backup_policies_with_expected_version()
        {
            using var host = CreateHost("Owner", JobStatus.Queued);

            using var list = await host.Client.GetAsync("api/v1/backups/policies");
            var policies = JArray.Parse(await list.Content.ReadAsStringAsync());
            using var update = await host.Client.PutAsync(
                "api/v1/backups/policies/World",
                new StringContent(
                    "{\"enabled\":true,\"cronExpression\":\"0 4 * * *\"," +
                    "\"timeZoneId\":\"UTC\",\"backupRootId\":\"primary\"," +
                    "\"retentionCount\":5,\"retentionDays\":14," +
                    "\"compressionEnabled\":true,\"expectedRowVersion\":0}",
                    Encoding.UTF8,
                    "application/json"));
            var saved = JObject.Parse(await update.Content.ReadAsStringAsync());
            using var stale = await host.Client.PutAsync(
                "api/v1/backups/policies/World",
                new StringContent(
                    "{\"enabled\":false,\"cronExpression\":\"0 5 * * *\"," +
                    "\"timeZoneId\":\"UTC\",\"backupRootId\":\"primary\"," +
                    "\"retentionCount\":3,\"retentionDays\":7," +
                    "\"compressionEnabled\":true,\"expectedRowVersion\":0}",
                    Encoding.UTF8,
                    "application/json"));
            var problem = JObject.Parse(await stale.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            Assert.Equal(3, policies.Count);
            Assert.Equal("World", (string?)policies[0]?["kind"]);
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);
            Assert.True((bool?)saved["enabled"]);
            Assert.Equal(1, (long?)saved["rowVersion"]);
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal("backup_policy_row_version_conflict", (string?)problem["code"]);
        }

        private static Host CreateHost(string? role, JobStatus status)
        {
            var store = new Store(status);
            var services = new ServiceCollection();
            services.AddSingleton<IJobStore>(store);
            services.AddSingleton(new JobService(store, () => DateTimeOffset.UtcNow));
            var scheduleStore = new ScheduleStore();
            services.AddSingleton<IScheduleStore>(scheduleStore);
            services.AddSingleton<ScheduleService>();
            services.AddSingleton<IAnnouncementGateway, AnnouncementGateway>();
            services.AddSingleton<AnnouncementService>();
            services.AddSingleton<IBackupPolicyStore, PolicyStore>();
            services.AddSingleton(provider => new BackupPolicyService(
                provider.GetRequiredService<IBackupPolicyStore>(),
                new[] { "primary" }));
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

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Web")]

        private sealed class Host : IDisposable
        {
            private readonly ServiceProvider provider;
            private readonly HttpConfiguration configuration;

            public Host(ServiceProvider provider, HttpConfiguration configuration, Store store)
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
            public Store Store { get; }

            public void Dispose()
            {
                Client.Dispose();
                configuration.Dispose();
                provider.Dispose();
            }
        }

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Web")]

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

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Web")]

        public sealed class Store : IJobStore
        {
            public Store(JobStatus status)
            {
                Current = new JobRecord(
                    Guid.NewGuid(),
                    JobKind.WorldBackup,
                    status,
                    "owner-1",
                    null,
                    "key",
                    "correlation",
                    DateTimeOffset.UtcNow.AddMinutes(-1),
                    status == JobStatus.Queued ? null : DateTimeOffset.UtcNow,
                    null,
                    null,
                    null,
                    null,
                    1);
            }

            public JobRecord Current { get; private set; }
            public JobRecord Enqueue(NewJob job) => throw new NotSupportedException();
            public JobRecord? TryClaimNext(string workerId, DateTimeOffset now) => null;

            public bool TryTransition(Guid jobId, long expectedRowVersion, JobStatus expected,
                JobStatus next, JobCompletion completion)
            {
                if (Current.Id != jobId || Current.RowVersion != expectedRowVersion ||
                    Current.Status != expected) return false;
                Current = Current with
                {
                    Status = next,
                    CompletedAtUtc = completion.CompletedAtUtc,
                    RowVersion = Current.RowVersion + 1
                };
                return true;
            }

            public JobRecord Get(Guid jobId) =>
                jobId == Current.Id ? Current : throw new KeyNotFoundException();

            public PagedResult<JobRecord, JobCursor> List(JobQuery query) =>
                new PagedResult<JobRecord, JobCursor>(new[] { Current }, null);
        }

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Web")]

        private sealed class AnnouncementGateway : IAnnouncementGateway
        {
            public Task SendAsync(
                AnnouncementMessage message,
                CancellationToken cancellationToken) => Task.CompletedTask;
        }

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Web")]

        private sealed class ScheduleStore : IScheduleStore
        {
            private ScheduleRecord? current;

            public IReadOnlyList<ScheduleRecord> List() =>
                current == null ? Array.Empty<ScheduleRecord>() : new[] { current };

            public ScheduleRecord? Get(Guid scheduleId) =>
                current?.Id == scheduleId ? current : null;

            public ScheduleRecord Upsert(ScheduleDefinition definition)
            {
                current = new ScheduleRecord(
                    definition.Id,
                    definition.Name,
                    definition.CronExpression,
                    definition.TimeZoneId,
                    definition.Enabled,
                    definition.ConcurrencyPolicy,
                    definition.Action,
                    DateTimeOffset.UtcNow.AddHours(1),
                    null,
                    definition.RowVersion + 1);
                return current;
            }

            public bool Delete(Guid scheduleId, long expectedRowVersion)
            {
                if (current?.Id != scheduleId || current.RowVersion != expectedRowVersion)
                    return false;
                current = null;
                return true;
            }

            public IReadOnlyList<ScheduleRecord> ClaimDue(
                DateTimeOffset now,
                string ownerId) => Array.Empty<ScheduleRecord>();

            public void RecordOutcome(ScheduleRunOutcome outcome)
            {
            }
        }

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Web")]

        private sealed class PolicyStore : IBackupPolicyStore
        {
            private readonly Dictionary<BackupKind, BackupPolicyDefinition> policies =
                new Dictionary<BackupKind, BackupPolicyDefinition>();

            public IReadOnlyList<BackupPolicyDefinition> List() =>
                policies.Values.OrderBy(policy => (int)policy.Kind).ToArray();

            public BackupPolicyDefinition? Get(BackupKind kind) =>
                policies.TryGetValue(kind, out var policy) ? policy : null;

            public BackupPolicyDefinition Upsert(BackupPolicyDefinition definition)
            {
                if (policies.TryGetValue(definition.Kind, out var current))
                {
                    if (current.RowVersion != definition.RowVersion)
                        throw new InvalidOperationException("backup_policy_row_version_conflict");
                    definition = new BackupPolicyDefinition(
                        definition.Kind,
                        definition.Enabled,
                        definition.CronExpression,
                        definition.TimeZoneId,
                        definition.BackupRootId,
                        definition.RetentionCount,
                        definition.RetentionDays,
                        definition.CompressionEnabled,
                        definition.RowVersion + 1);
                }
                else if (definition.RowVersion != 0)
                {
                    throw new InvalidOperationException("backup_policy_row_version_conflict");
                }

                if (!policies.ContainsKey(definition.Kind))
                {
                    definition = new BackupPolicyDefinition(
                        definition.Kind,
                        definition.Enabled,
                        definition.CronExpression,
                        definition.TimeZoneId,
                        definition.BackupRootId,
                        definition.RetentionCount,
                        definition.RetentionDays,
                        definition.CompressionEnabled,
                        1);
                }

                policies[definition.Kind] = definition;
                return definition;
            }
        }
    }
}
