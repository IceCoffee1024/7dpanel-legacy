using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using System.Web.Http;
using Dapper;
using DbUp;
using LSTY.SevenDPanel.Adapters.Local.Backups;
using LSTY.SevenDPanel.Adapters.Local.Jobs;
using LSTY.SevenDPanel.Adapters.Local.Restore;
using LSTY.SevenDPanel.Adapters.Local.Schedules;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Backups;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Jobs;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Schedules;
using LSTY.SevenDPanel.Adapters.SevenDays.Announcements;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Backups;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.ConsoleCommands;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.ServerOperations;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.DependencyInjection;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Application.Announcements;
using LSTY.SevenDPanel.Application.Backups;
using LSTY.SevenDPanel.Application.ConsoleCommands;
using LSTY.SevenDPanel.Application.Discord;
using LSTY.SevenDPanel.Application.Jobs;
using LSTY.SevenDPanel.Application.Schedules;
using LSTY.SevenDPanel.DependencyInjection;
using LSTY.SevenDPanel.Domain.Jobs;
using LSTY.SevenDPanel.Hosting;
using LSTY.SevenDPanel.Adapters.Local.ServerOperations;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LSTY.SevenDPanel.Tests.Bootstrap
{
    [Trait("Capability", "Platform")]
    [Trait("Boundary", "Bootstrap")]
    public sealed class JobsAndSchedulingCompositionTests
    {
        [Fact]
        public async Task Production_discord_interaction_composition_uses_the_persisted_public_key_and_three_argument_controller()
        {
            const string interactionPublicKey =
                "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a";
            using var fixture = new RuntimeFixture();
            var provider = fixture.Provider;
            var store = provider.GetRequiredService<IDiscordIntegrationStore>();
            Assert.NotNull(provider.GetRequiredService<IDiscordInteractionSignatureVerifier>());
            store.SetSecret(new DiscordSecretValue(
                "interactionPublicKey",
                interactionPublicKey,
                "test",
                DateTimeOffset.UtcNow));

            var resolver = new MicrosoftDependencyResolver(provider);
            using var scope = resolver.BeginScope();
            var controller = Assert.IsType<DiscordIntegrationController>(
                scope.GetService(typeof(DiscordIntegrationController)));
            var deferredSinkField = typeof(DiscordIntegrationController).GetField(
                "deferredInteractionSink",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.NotNull(deferredSinkField.GetValue(controller));

            using var configuration = new HttpConfiguration
            {
                DependencyResolver = new MicrosoftDependencyResolver(provider)
            };
            configuration.MapHttpAttributeRoutes();
            configuration.EnsureInitialized();
            using var client = new HttpClient(new HttpServer(configuration))
            {
                BaseAddress = new Uri("http://localhost/")
            };
            using var response = await client.PostAsync(
                "api/v1/integrations/discord/interactions",
                new StringContent("{\"type\":1}", System.Text.Encoding.UTF8, "application/json"),
                TestContext.Current.CancellationToken);
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.DoesNotContain("discord_interaction_verification_unavailable", body, StringComparison.Ordinal);
            Assert.DoesNotContain(interactionPublicKey, body, StringComparison.Ordinal);
        }

        [Fact]
        public void Application_ports_resolve_one_production_singleton()
        {
            using var fixture = new RuntimeFixture();
            var provider = fixture.Provider;

            AssertPort<IJobStore, SqliteJobStore>(provider);
            AssertPort<IJobSubmissionStore, SqliteJobPayloadStore>(provider);
            AssertPort<IJobPayloadReader, SqliteJobPayloadStore>(provider);
            AssertPort<IBackupCatalog, SqliteBackupCatalog>(provider);
            AssertPort<IBackupArchiveStorage, FileSystemBackupArchiveStore>(provider);
            AssertPort<IPendingRestoreMarkerStore, JsonPendingRestoreStore>(provider);
            AssertPort<IRestoreResultMergeStore, SqliteRestoreResultMergeStore>(provider);
            AssertPort<IWorldSaveGateway, SevenDaysWorldSaveGateway>(provider);
            AssertPort<IScheduleStore, SqliteScheduleStore>(provider);
            AssertPort<IConsoleCommandGateway, SevenDaysConsoleCommandService>(provider);
            AssertPort<IRestartScriptLauncher, RestartScriptLauncher>(provider);
            AssertPort<IAnnouncementGateway, SevenDaysAnnouncementGateway>(provider);
        }

        [Fact]
        public void Workers_restore_and_explicit_handlers_are_root_singletons()
        {
            using var fixture = new RuntimeFixture();
            var provider = fixture.Provider;

            AssertSingleton<BackgroundWorkConsumer>(provider);
            AssertSingleton<BackgroundWorkerJobStore>(provider);
            AssertSingleton<BackgroundScheduler>(provider);
            AssertSingleton<PendingRestoreApplier>(provider);
            AssertSingleton<RestoreResultReconciler>(provider);
            AssertSingleton<CreateWorldBackup>(provider);
            AssertSingleton<CreatePanelDatabaseBackup>(provider);
            AssertSingleton<CreateServerConfigurationBackup>(provider);
            AssertSingleton<PanelDatabaseBackupJobHandler>(provider);
            AssertSingleton<ServerConfigurationBackupJobHandler>(provider);
            AssertSingleton<ScheduledJobExecutor>(provider);
            AssertSingleton<SevenDaysConsoleCommandService>(provider);
            AssertSingleton<RestartScriptLauncher>(provider);
            AssertSingleton<SevenDaysAnnouncementGateway>(provider);

            var worker = provider.GetRequiredService<BackgroundWorkConsumer>();
            var jobsField = typeof(BackgroundWorkConsumer).GetField(
                "jobs",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(jobsField);
            Assert.Same(
                provider.GetRequiredService<BackgroundWorkerJobStore>(),
                jobsField!.GetValue(worker));
        }

        [Fact]
        public void Application_services_and_runtime_decorator_are_root_singletons()
        {
            using var fixture = new RuntimeFixture();
            var provider = fixture.Provider;

            AssertSingleton<JobService>(provider);
            AssertSingleton<BackupCatalogService>(provider);
            AssertSingleton<StageRestore>(provider);
            AssertSingleton<ScheduleService>(provider);
            AssertSingleton<AnnouncementService>(provider);
            AssertSingleton<PendingRestoreStartupStep>(provider);
            AssertSingleton<JobsAndSchedulingRuntime>(provider);
            Assert.Same(
                provider.GetRequiredService<JobsAndSchedulingRuntime>(),
                provider.GetRequiredService<IModRuntime>());
        }

        [Fact]
        public void Composition_does_not_migrate_the_database_before_runtime_start()
        {
            using var fixture = new RuntimeFixture();

            Assert.False(File.Exists(fixture.DatabasePath));
        }

        [Fact]
        public void Background_worker_store_recovers_running_and_skips_restore_until_staging()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "7dpanel-restore-claim-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var connections = new SqliteConnectionFactory(
                    Path.Combine(directory, "7dpanel.db"));
                new SqliteDatabaseBootstrapper(connections, _ => { }).Upgrade();
                var jobs = new SqliteJobStore(connections);
                var workerJobs = new BackgroundWorkerJobStore(jobs, connections);
                var now = new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero);
                var stale = jobs.Enqueue(new NewJob(
                    JobKind.WorldBackup,
                    "owner",
                    null,
                    "stale-running",
                    null,
                    now));
                var previouslyClaimed = jobs.TryClaimNext("old-worker", now);
                Assert.NotNull(previouslyClaimed);
                Assert.Equal(stale.Id, previouslyClaimed!.Id);
                var restore = jobs.Enqueue(new NewJob(
                    JobKind.Restore,
                    "owner",
                    null,
                    "restore-race",
                    null,
                    now));
                var backup = jobs.Enqueue(new NewJob(
                    JobKind.WorldBackup,
                    "owner",
                    null,
                    "world-after-restore",
                    null,
                    now.AddTicks(1)));

                var interrupted = workerJobs.InterruptRunningJobs(
                    now.AddMilliseconds(500));

                var claimed = workerJobs.TryClaimNext(
                    "worker",
                    now.AddSeconds(1));

                Assert.Equal(1, interrupted);
                Assert.Equal(JobStatus.Interrupted, jobs.Get(stale.Id).Status);
                Assert.NotNull(claimed);
                Assert.Equal(backup.Id, claimed!.Id);
                Assert.Equal(JobStatus.Running, claimed.Status);
                Assert.Equal(JobStatus.Queued, jobs.Get(restore.Id).Status);
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void Background_worker_store_claims_only_explicitly_supported_kinds()
        {
            using var database = new TemporaryWorkerDatabase();
            var jobs = new SqliteJobStore(database.ConnectionFactory);
            var workerJobs = new BackgroundWorkerJobStore(jobs, database.ConnectionFactory);
            var now = new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero);
            var supportedKinds = new[]
            {
                JobKind.WorldBackup,
                JobKind.PanelDatabaseBackup,
                JobKind.ServerConfigurationBackup,
                JobKind.ScheduledConsoleCommand,
                JobKind.ScheduledRestart,
                JobKind.ScheduledAnnouncement,
                JobKind.WorldOperation
            };
            var scheduleIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
            InsertSchedule(
                database.ConnectionFactory,
                scheduleIds[0],
                JobKind.ScheduledConsoleCommand);
            InsertSchedule(
                database.ConnectionFactory,
                scheduleIds[1],
                JobKind.ScheduledRestart);
            InsertSchedule(
                database.ConnectionFactory,
                scheduleIds[2],
                JobKind.ScheduledAnnouncement);
            var supported = supportedKinds
                .Select((kind, index) => jobs.Enqueue(new NewJob(
                    kind,
                    "owner",
                    SourceScheduleId(kind, scheduleIds),
                    "supported-" + index,
                    null,
                    now)))
                .ToArray();
            var restore = jobs.Enqueue(new NewJob(
                JobKind.Restore,
                "owner",
                null,
                "restore-staging",
                null,
                now));
            var futureId = Guid.NewGuid();

            using (var connection = database.ConnectionFactory.Open())
            {
                connection.Execute("PRAGMA ignore_check_constraints = ON;");
                connection.Execute(
                    @"INSERT INTO jobs (
                          id, kind, status, actor_subject, idempotency_key,
                          created_at_utc, row_version)
                      VALUES (@Id, 'FutureJobKind', 'Queued', 'owner',
                          'future-kind', @CreatedAtUtc, 0);",
                    new
                    {
                        Id = futureId.ToString("D"),
                        CreatedAtUtc = now.ToUnixTimeMilliseconds()
                    });
            }

            var claimedIds = supported
                .Select(_ => workerJobs.TryClaimNext("worker", now.AddSeconds(1)))
                .Select(claimed =>
                {
                    Assert.NotNull(claimed);
                    Assert.Equal(JobStatus.Running, claimed!.Status);
                    return claimed.Id;
                })
                .ToArray();

            Assert.Equal(supported.Length, claimedIds.Length);
            Assert.All(supported, job => Assert.Contains(job.Id, claimedIds));
            Assert.Null(workerJobs.TryClaimNext("worker", now.AddSeconds(1)));
            Assert.Equal(JobStatus.Queued, jobs.Get(restore.Id).Status);
            using (var connection = database.ConnectionFactory.Open())
            {
                Assert.Equal(
                    "Queued",
                    connection.ExecuteScalar<string>(
                        "SELECT status FROM jobs WHERE id = @Id;",
                        new { Id = futureId.ToString("D") }));
            }
        }

        [Fact]
        public void Upgrade_from_009_to_010_preserves_restore_payloads_without_an_artifact_foreign_key()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "7dpanel-jobs-009-010-tests",
                Guid.NewGuid().ToString("N"));
            var connections = new SqliteConnectionFactory(
                Path.Combine(directory, "7dpanel.db"));
            try
            {
                UpgradeThrough(connections, 9);
                using (var after009 = connections.Open())
                {
                    Assert.DoesNotContain(
                        "backup_id",
                        after009.Query<string>(
                            "SELECT \"from\" FROM pragma_foreign_key_list('restore_job_payloads');"));
                    after009.Execute(
                        @"INSERT INTO jobs (
                              id, kind, status, idempotency_key,
                              created_at_utc, row_version)
                          VALUES (
                              'restore-before-010', 'Restore', 'PendingRestart',
                              'restore-before-010', 1785024000000, 0);
                          INSERT INTO restore_job_payloads (
                              job_id, backup_id, backup_kind, restart_after_stage)
                          VALUES (
                              'restore-before-010', 'artifact-restored-after-crash',
                              'World', 1);"
                    );
                }

                UpgradeThrough(connections, 10);

                using var upgraded = connections.Open();
                Assert.Equal(
                    1,
                    upgraded.ExecuteScalar<int>(
                        "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'player_sessions';"));
                Assert.Equal(
                    "artifact-restored-after-crash",
                    upgraded.ExecuteScalar<string>(
                        "SELECT backup_id FROM restore_job_payloads WHERE job_id = 'restore-before-010';"));
                var appliedScripts = upgraded.Query<string>(
                    "SELECT ScriptName FROM SchemaVersions;").ToArray();
                Assert.Contains(
                    appliedScripts,
                    script => script.EndsWith(
                        "009_JobsBackupsSchedules.sql",
                        StringComparison.OrdinalIgnoreCase));
                Assert.Contains(
                    appliedScripts,
                    script => script.EndsWith(
                        "010_PlayerEvidenceActions.sql",
                        StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(
                    appliedScripts,
                    script => script.EndsWith(
                        "011_RestoreResultMerge.sql",
                        StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                connections.Dispose();
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        private static Guid? SourceScheduleId(JobKind kind, Guid[] scheduleIds)
        {
            switch (kind)
            {
                case JobKind.ScheduledConsoleCommand:
                    return scheduleIds[0];
                case JobKind.ScheduledRestart:
                    return scheduleIds[1];
                case JobKind.ScheduledAnnouncement:
                    return scheduleIds[2];
                default:
                    return null;
            }
        }

        private static void InsertSchedule(
            SqliteConnectionFactory connectionFactory,
            Guid id,
            JobKind kind)
        {
            using var connection = connectionFactory.Open();
            connection.Execute(
                @"INSERT INTO schedules (
                      id, kind, name, cron_expression, time_zone_id, enabled,
                      concurrency_policy, command_text, countdown_seconds, message_text,
                      next_occurrence_utc, row_version)
                  VALUES (@Id, @Kind, @Name, '* * * * *', 'UTC', 1, 'QueueOne',
                      @CommandText, @CountdownSeconds, @MessageText, @NextUtc, 0);",
                new
                {
                    Id = id.ToString("D"),
                    Kind = kind.ToString(),
                    Name = kind.ToString(),
                    CommandText = kind == JobKind.ScheduledConsoleCommand ? "say test" : null,
                    CountdownSeconds = kind == JobKind.ScheduledRestart ? (int?)60 : null,
                    MessageText = kind == JobKind.ScheduledAnnouncement ? "test announcement" : null,
                    NextUtc = 0L
                });
        }

        private static void AssertPort<TPort, TImplementation>(IServiceProvider provider)
            where TPort : class
            where TImplementation : class, TPort
        {
            var registrations = provider.GetServices<TPort>().ToArray();
            var registration = Assert.Single(registrations);
            var implementation = provider.GetRequiredService<TImplementation>();

            Assert.IsType<TImplementation>(registration);
            Assert.Same(implementation, registration);
            Assert.Same(implementation, provider.GetRequiredService<TImplementation>());
        }

        private static void AssertSingleton<TService>(IServiceProvider provider)
            where TService : class
        {
            var registration = Assert.Single(provider.GetServices<TService>());
            Assert.Same(registration, provider.GetRequiredService<TService>());
            Assert.Same(registration, provider.GetRequiredService<TService>());
        }

        private static void UpgradeThrough(
            SqliteConnectionFactory connectionFactory,
            int finalVersion)
        {
            var directory = Path.GetDirectoryName(connectionFactory.DatabasePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            var result = DeployChanges.To
                .SqliteDatabase(connectionFactory.ConnectionString)
                .WithScriptsEmbeddedInAssembly(
                    typeof(SqliteDatabaseBootstrapper).Assembly,
                    resourceName =>
                        resourceName.EndsWith(
                            ".sql",
                            StringComparison.OrdinalIgnoreCase) &&
                        Enumerable.Range(1, finalVersion).Any(
                            version => resourceName.IndexOf(
                                $".Migrations.{version:D3}_",
                                StringComparison.OrdinalIgnoreCase) >= 0))
                .WithTransactionPerScript()
                .Build()
                .PerformUpgrade();
            Assert.True(result.Successful, result.Error?.ToString());
        }

        [Trait("Capability", "Platform")]

        [Trait("Boundary", "Bootstrap")]

        private sealed class TemporaryWorkerDatabase : IDisposable
        {
            private readonly string directory = Path.Combine(
                Path.GetTempPath(),
                "7dpanel-worker-claim-tests",
                Guid.NewGuid().ToString("N"));

            public TemporaryWorkerDatabase()
            {
                Directory.CreateDirectory(directory);
                ConnectionFactory = new SqliteConnectionFactory(
                    Path.Combine(directory, "panel.db"));
                new SqliteDatabaseBootstrapper(ConnectionFactory, _ => { }).Upgrade();
            }

            public SqliteConnectionFactory ConnectionFactory { get; }

            public void Dispose()
            {
                ConnectionFactory.Dispose();
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [Trait("Capability", "Platform")]

        [Trait("Boundary", "Bootstrap")]

        private sealed class RuntimeFixture : IDisposable
        {
            private static Assembly? syntheticGameAssembly;
            private readonly string dataDirectory;
            private readonly ServiceProviderRuntime runtime;

            public RuntimeFixture()
            {
                dataDirectory = Path.Combine(
                    Path.GetTempPath(),
                    "7dpanel-jobs-composition-tests",
                    Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dataDirectory);
                DatabasePath = Path.Combine(dataDirectory, "7dpanel.db");
                InitializeGameData(dataDirectory);
                runtime = PanelServiceProviderFactory.CreateRuntime(
                    PanelHostOptions.FromBinding(
                        18080,
                        "127.0.0.1",
                        "http",
                        serverConfigurationPath: Path.Combine(dataDirectory, "serverconfig.xml")),
                    dataDirectory,
                    null,
                    _ => { });
                var providerField = typeof(ServiceProviderRuntime).GetField(
                    "serviceProvider",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(providerField);
                Provider = Assert.IsAssignableFrom<IServiceProvider>(
                    providerField.GetValue(runtime));
            }

            public IServiceProvider Provider { get; }
            public string DatabasePath { get; }

            private static void InitializeGameData(string directory)
            {
                var gameAssembly = LoadGameAssembly();
                var gameIo = gameAssembly.GetType("GameIO");
                var gamePrefs = gameAssembly.GetType("GamePrefs");
                var preferenceType = gameAssembly.GetType("EnumGamePrefs");
                Assert.NotNull(gameIo);
                Assert.NotNull(gamePrefs);
                Assert.NotNull(preferenceType);

                var initializeMethod = gameIo!.GetMethod(
                    "InitializeUserDataPaths",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(string) },
                    null);
                Assert.NotNull(initializeMethod);
                initializeMethod.Invoke(
                    null,
                    new object[] { Path.Combine(directory, "game-data") });
                SetGamePreference(
                    gamePrefs!,
                    preferenceType!,
                    "GameWorld",
                    "Navezgane");
                SetGamePreference(
                    gamePrefs,
                    preferenceType,
                    "GameName",
                    "jobs-composition");
                SetGamePreference(
                    gamePrefs,
                    preferenceType,
                    "GameVersion",
                    "v3.0.1-b4");
                SetGamePreference(
                    gamePrefs,
                    preferenceType,
                    "GameSaveStorageType",
                    0);
            }

            private static Assembly LoadGameAssembly()
            {
                var loaded = AppDomain.CurrentDomain.GetAssemblies()
                    .SingleOrDefault(assembly =>
                        assembly.GetName().Name == "Assembly-CSharp");
                if (loaded != null) return loaded;
                syntheticGameAssembly = CreateGameAssembly();
                AppDomain.CurrentDomain.AssemblyResolve += ResolveSyntheticGameAssembly;
                return syntheticGameAssembly;
            }

            private static Assembly? ResolveSyntheticGameAssembly(
                object? sender,
                ResolveEventArgs arguments) =>
                arguments.Name.StartsWith(
                    "Assembly-CSharp,",
                    StringComparison.Ordinal) ? syntheticGameAssembly : null;

            private static Assembly CreateGameAssembly()
            {
                var assembly = AppDomain.CurrentDomain.DefineDynamicAssembly(
                    new AssemblyName("Assembly-CSharp")
                    {
                        Version = new Version(0, 0, 0, 0)
                    },
                    AssemblyBuilderAccess.Run);
                var module = assembly.DefineDynamicModule("Assembly-CSharp");
                var preferences = module.DefineEnum(
                    "EnumGamePrefs",
                    TypeAttributes.Public,
                    typeof(int));
                preferences.DefineLiteral("GameName", 31);
                preferences.DefineLiteral("GameWorld", 33);
                preferences.DefineLiteral("GameVersion", 34);
                preferences.DefineLiteral("GameSaveStorageType", 294);
                var preferenceType = preferences.CreateType();

                DefineGameIo(module);
                DefineGamePrefs(module, preferenceType);
                return assembly;
            }

            private static void DefineGameIo(ModuleBuilder module)
            {
                var gameIo = module.DefineType(
                    "GameIO",
                    TypeAttributes.Public | TypeAttributes.Abstract |
                    TypeAttributes.Sealed);
                var saveDirectory = gameIo.DefineField(
                    "saveDirectory",
                    typeof(string),
                    FieldAttributes.Private | FieldAttributes.Static);
                var initialize = gameIo.DefineMethod(
                    "InitializeUserDataPaths",
                    MethodAttributes.Public | MethodAttributes.Static,
                    typeof(void),
                    new[] { typeof(string) });
                var initializeIl = initialize.GetILGenerator();
                initializeIl.Emit(OpCodes.Ldarg_0);
                initializeIl.Emit(OpCodes.Stsfld, saveDirectory);
                initializeIl.Emit(OpCodes.Ret);
                var getSaveDirectory = gameIo.DefineMethod(
                    "GetSaveGameDir",
                    MethodAttributes.Public | MethodAttributes.Static,
                    typeof(string),
                    Type.EmptyTypes);
                var getSaveDirectoryIl = getSaveDirectory.GetILGenerator();
                getSaveDirectoryIl.Emit(OpCodes.Ldsfld, saveDirectory);
                getSaveDirectoryIl.Emit(OpCodes.Ret);
                gameIo.CreateType();
            }

            private static void DefineGamePrefs(
                ModuleBuilder module,
                Type preferenceType)
            {
                var gamePrefs = module.DefineType(
                    "GamePrefs",
                    TypeAttributes.Public | TypeAttributes.Abstract |
                    TypeAttributes.Sealed);
                var world = gamePrefs.DefineField(
                    "world",
                    typeof(string),
                    FieldAttributes.Private | FieldAttributes.Static);
                var name = gamePrefs.DefineField(
                    "name",
                    typeof(string),
                    FieldAttributes.Private | FieldAttributes.Static);
                var version = gamePrefs.DefineField(
                    "version",
                    typeof(string),
                    FieldAttributes.Private | FieldAttributes.Static);
                DefineStringPreferenceSetter(
                    gamePrefs,
                    preferenceType,
                    new[] { (31, name), (33, world), (34, version) });
                var setInteger = gamePrefs.DefineMethod(
                    "Set",
                    MethodAttributes.Public | MethodAttributes.Static,
                    typeof(void),
                    new[] { preferenceType, typeof(int) });
                setInteger.GetILGenerator().Emit(OpCodes.Ret);
                DefineStringPreferenceGetter(
                    gamePrefs,
                    preferenceType,
                    new[] { (31, name), (33, world), (34, version) });
                gamePrefs.CreateType();
            }

            private static void DefineStringPreferenceSetter(
                TypeBuilder gamePrefs,
                Type preferenceType,
                (int Key, FieldBuilder Value)[] fields)
            {
                var method = gamePrefs.DefineMethod(
                    "Set",
                    MethodAttributes.Public | MethodAttributes.Static,
                    typeof(void),
                    new[] { preferenceType, typeof(string) });
                var il = method.GetILGenerator();
                foreach (var field in fields)
                {
                    var next = il.DefineLabel();
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldc_I4, field.Key);
                    il.Emit(OpCodes.Bne_Un_S, next);
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Stsfld, field.Value);
                    il.Emit(OpCodes.Ret);
                    il.MarkLabel(next);
                }
                il.Emit(OpCodes.Ret);
            }

            private static void DefineStringPreferenceGetter(
                TypeBuilder gamePrefs,
                Type preferenceType,
                (int Key, FieldBuilder Value)[] fields)
            {
                var method = gamePrefs.DefineMethod(
                    "GetString",
                    MethodAttributes.Public | MethodAttributes.Static,
                    typeof(string),
                    new[] { preferenceType });
                var il = method.GetILGenerator();
                foreach (var field in fields)
                {
                    var next = il.DefineLabel();
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldc_I4, field.Key);
                    il.Emit(OpCodes.Bne_Un_S, next);
                    il.Emit(OpCodes.Ldsfld, field.Value);
                    il.Emit(OpCodes.Ret);
                    il.MarkLabel(next);
                }
                il.Emit(OpCodes.Ldstr, string.Empty);
                il.Emit(OpCodes.Ret);
            }

            private static void SetGamePreference(
                Type gamePrefs,
                Type preferenceType,
                string preferenceName,
                object value)
            {
                var preference = Enum.Parse(preferenceType, preferenceName);
                var setMethod = gamePrefs.GetMethod(
                    "Set",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { preferenceType, value.GetType() },
                    null);
                Assert.NotNull(setMethod);
                setMethod!.Invoke(null, new[] { preference, value });
            }

            public void Dispose()
            {
                try { runtime.Dispose(); }
                finally
                {
                    if (Directory.Exists(dataDirectory))
                        Directory.Delete(dataDirectory, recursive: true);
                }
            }
        }
    }
}
