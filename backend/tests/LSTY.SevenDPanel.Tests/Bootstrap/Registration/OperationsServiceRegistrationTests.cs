using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace LSTY.SevenDPanel.Tests.Bootstrap.Registration
{
    [Trait("Capability", "Operations")]
    [Trait("Boundary", "Bootstrap")]
    public sealed class OperationsServiceRegistrationTests
    {
        [Fact]
        public void Module_is_static_contract_only_and_does_not_build_or_resolve_a_provider()
        {
            var source = ReadModule();

            Assert.DoesNotContain("BuildServiceProvider", source);
            Assert.DoesNotContain(".GetService(", source);
            Assert.DoesNotContain("Task", source);
            Assert.DoesNotContain("Timer", source);
            Assert.DoesNotContain("static IServiceProvider", source);
            Assert.DoesNotContain("static ServiceProvider", source);
            Assert.DoesNotContain("CreateRuntime(", source);
            Assert.DoesNotContain("AddScoped", source);
            Assert.DoesNotContain("AddTransient", source);
            Assert.DoesNotContain("IModRuntime", source);
        }

        [Fact]
        public void Module_preserves_operations_descriptor_order_lifetimes_and_aliases()
        {
            var source = ReadModule();
            var factory = ReadFactory();

            Assert.NotEmpty(ReadRegistrationStartLines(source));
            Assert.Contains(
                "OperationsServiceRegistration.Register(services, context);",
                factory);

            AssertOrdered(source, new[]
            {
                "services.AddSingleton<FileSystemBackupArchiveStore>();",
                "services.AddSingleton<IBackupArchiveStorage>(serviceProvider =>",
                "services.AddSingleton<SqliteJobStore>();",
                "services.AddSingleton<IJobStore>(serviceProvider =>",
                "services.AddSingleton<BackgroundWorkerJobStore>();",
                "services.AddSingleton<SqliteJobPayloadStore>();",
                "services.AddSingleton<IJobSubmissionStore>(serviceProvider =>",
                "services.AddSingleton<IJobPayloadReader>(serviceProvider =>",
                "services.AddSingleton<SqliteBackupCatalog>();",
                "services.AddSingleton<IBackupCatalog>(serviceProvider =>",
                "services.AddSingleton<SqliteBackupPolicyStore>();",
                "services.AddSingleton<IBackupPolicyStore>(serviceProvider =>",
                "services.AddSingleton<SqliteWorldOperationStore>();",
                "services.AddSingleton<IWorldOperationStore>(serviceProvider =>",
                "services.AddSingleton<IWorldOperationExecutionStore>(serviceProvider =>",
                "services.AddSingleton<IWorldOperationRecoveryStore>(serviceProvider =>",
                "services.AddSingleton(serviceProvider => new SqliteWorldOperationJobBridge(",
                "services.AddSingleton<SqliteRestoreResultMergeStore>();",
                "services.AddSingleton<IRestoreResultMergeStore>(serviceProvider =>",
                "services.AddSingleton<JsonPendingRestoreStore>();",
                "services.AddSingleton<IPendingRestoreMarkerStore>(serviceProvider =>",
                "services.AddSingleton<SqliteScheduleStore>();",
                "services.AddSingleton<IScheduleStore>(serviceProvider =>",
                "services.AddSingleton<SevenDaysWorldSaveGateway>();",
                "services.AddSingleton<IWorldSaveGateway>(serviceProvider =>",
                "services.AddSingleton<SevenDaysAnnouncementGateway>();",
                "services.AddSingleton<IAnnouncementGateway>(serviceProvider =>",
                "services.AddSingleton(serviceProvider => new JobService(",
                "services.AddSingleton(serviceProvider => new CreateWorldBackup(",
                "services.AddSingleton<CreatePanelDatabaseBackup>();",
                "services.AddSingleton<CreateServerConfigurationBackup>();",
                "services.AddSingleton<BackupCatalogService>();",
                "services.AddSingleton(serviceProvider => new BackupPolicyService(",
                "services.AddSingleton<BackupRetentionService>();",
                "services.AddSingleton(serviceProvider => new BackupPolicyScheduler(",
                "services.AddSingleton(serviceProvider => new StageRestore(",
                "services.AddSingleton<ScheduleService>();",
                "services.AddSingleton<AnnouncementService>();",
                "services.AddSingleton<IWorldRestoreRuntimeEvidenceSource,",
                "services.AddSingleton<WorldRestoreTimingGate>();",
                "services.AddSingleton(serviceProvider => new PanelDatabaseBackupJobHandler(",
                "services.AddSingleton(serviceProvider => new ServerConfigurationBackupJobHandler(",
                "services.AddSingleton(serviceProvider => new ScheduledJobExecutor(",
                "services.AddSingleton(serviceProvider => new PendingRestoreApplier(",
                "services.AddSingleton(serviceProvider => new RestoreResultReconciler(",
                "services.AddSingleton<PendingRestoreStartupStep>();",
                "services.AddSingleton(serviceProvider => new BackgroundWorkConsumer(",
                "services.AddSingleton(serviceProvider => new BackgroundScheduler(",
                "services.AddSingleton<SqliteServerOperationAuditTrail>();",
                "services.AddSingleton<IServerOperationAuditTrail>(serviceProvider =>",
                "services.AddSingleton<SqliteServerOperationStore>();",
                "services.AddSingleton<IServerOperationStore>(serviceProvider =>",
                "services.AddSingleton<ServerOperationProcessInstance>();",
                "services.AddSingleton<GetServerOperationUseCase>();",
                "services.AddSingleton<ReconcileServerOperationsUseCase>();",
                "services.AddSingleton<RestartScriptLauncher>();",
                "services.AddSingleton<IRestartScriptLauncher>(serviceProvider =>",
                "services.AddSingleton<SevenDaysShutdownServerGateway>();",
                "services.AddSingleton<IShutdownServerGateway>(serviceProvider =>",
                "services.AddSingleton<RestartServerUseCase>();",
                "services.AddSingleton<ShutdownServerUseCase>();",
                "services.AddSingleton(_ => new LocalMapResourcePublisher(",
                "services.AddSingleton<IMapResourcePublisher>(serviceProvider =>",
                "services.AddSingleton<IMapTileStore>(serviceProvider => new LocalMapTileStore(",
                "services.AddSingleton<GetMapTileUseCase>();",
                "services.AddSingleton<DeleteLandClaimUseCase>();",
                "services.AddSingleton<MoveOnlinePlayerUseCase>();",
                "services.AddSingleton<MoveWorldEntityUseCase>();",
                "services.AddSingleton<SubmitMapJobUseCase>();",
                "services.AddSingleton<CopyRegionUseCase>();",
                "services.AddSingleton<FillRegionUseCase>();",
                "services.AddSingleton<ClearRegionUseCase>();",
                "services.AddSingleton<PasteRegionUseCase>();",
                "services.AddSingleton<SetBlockUseCase>();",
                "services.AddSingleton<PlacePrefabUseCase>();",
                "services.AddSingleton<RemovePrefabUseCase>();",
                "services.AddSingleton<SpawnWorldEntityUseCase>();",
                "services.AddSingleton<DeleteWorldEntityUseCase>();",
                "services.AddSingleton<CleanupWorldEntitiesUseCase>();",
                "services.AddSingleton<ReloadGameResourceUseCase>();",
                "services.AddSingleton<CollectGameGarbageUseCase>();",
                "services.AddSingleton<UndoWorldChangeSetUseCase>();",
                "services.AddSingleton(serviceProvider => new SevenDaysMapWorldOperationHandler(",
                "services.AddSingleton(serviceProvider => new SevenDaysRegionOperationHandler(",
                "services.AddSingleton(serviceProvider => new SevenDaysBlockPrefabOperationHandler(",
                "services.AddSingleton(serviceProvider => new SevenDaysEntityMaintenanceHandler(",
                "services.AddSingleton(serviceProvider => new SevenDaysRuntimeMaintenanceHandler(",
                "services.AddSingleton<SevenDaysUndoOperationHandler>();",
                "services.AddSingleton<IWorldChangeSetPreflightGateway>(serviceProvider =>",
                "services.AddSingleton(serviceProvider => new WorldOperationJobHandler(",
                "services.AddSingleton<IWorldOperationJobHandler>(serviceProvider =>",
                "services.AddSingleton(serviceProvider => new JobsAndSchedulingRuntime(",
                "services.AddSingleton(serviceProvider => new WorldOperationRuntime(",
                "services.AddSingleton(serviceProvider => new ChatCommandMixedTestRuntime(",
                "services.AddSingleton(serviceProvider => new ServerOperationRecoveryRuntime("
            });

            Assert.Contains(
                "services.AddSingleton<IWorldOperationStore>(serviceProvider =>\n                serviceProvider.GetRequiredService<SqliteWorldOperationStore>());",
                source);
            Assert.Contains(
                "services.AddSingleton<IWorldOperationJobHandler>(serviceProvider =>\n                serviceProvider.GetRequiredService<WorldOperationJobHandler>());",
                source);
        }

        private static void AssertRegistrationStartsAreOrderedSubsequence(
            IReadOnlyList<string> moduleRegistrations,
            IReadOnlyList<string> factoryRegistrations)
        {
            Assert.NotEmpty(moduleRegistrations);
            Assert.NotEmpty(factoryRegistrations);

            var factoryIndex = 0;
            foreach (var moduleRegistration in moduleRegistrations)
            {
                var matchIndex = -1;
                for (var index = factoryIndex; index < factoryRegistrations.Count; index++)
                {
                    if (string.Equals(
                            moduleRegistration,
                            factoryRegistrations[index],
                            StringComparison.Ordinal))
                    {
                        matchIndex = index;
                        break;
                    }
                }

                Assert.True(
                    matchIndex >= 0,
                    "Operations registration is not an ordered production-factory registration: " +
                    moduleRegistration);
                factoryIndex = matchIndex + 1;
            }
        }

        private static IReadOnlyList<string> ReadRegistrationStartLines(string source)
        {
            var registrations = new List<string>();
            foreach (var line in source.Split(
                         new[] { "\r\n", "\n" },
                         StringSplitOptions.None))
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("services.AddSingleton", StringComparison.Ordinal) ||
                    trimmed.StartsWith("services.AddScoped", StringComparison.Ordinal) ||
                    trimmed.StartsWith("services.AddTransient", StringComparison.Ordinal))
                {
                    registrations.Add(trimmed);
                }
            }

            return registrations;
        }

        private static void AssertOrdered(string source, IReadOnlyList<string> markers)
        {
            var previous = -1;
            foreach (var marker in markers)
            {
                var current = source.IndexOf(marker, StringComparison.Ordinal);
                Assert.True(current >= 0, "Missing registration marker: " + marker);
                Assert.True(
                    current > previous,
                    "Registration marker is out of order: " + marker);
                previous = current;
            }
        }

        private static string ReadModule()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                var candidate = Path.Combine(
                    directory.FullName,
                    "backend",
                    "src",
                    "Bootstrap",
                    "LSTY.SevenDPanel",
                    "DependencyInjection",
                    "Registration",
                    "OperationsServiceRegistration.cs");
                if (File.Exists(candidate)) return File.ReadAllText(candidate);
                directory = directory.Parent;
            }

            throw new FileNotFoundException(
                "Could not locate OperationsServiceRegistration.cs.");
        }

        private static string ReadFactory()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                var candidate = Path.Combine(
                    directory.FullName,
                    "backend",
                    "src",
                    "Bootstrap",
                    "LSTY.SevenDPanel",
                    "DependencyInjection",
                    "PanelServiceProviderFactory.cs");
                if (File.Exists(candidate)) return File.ReadAllText(candidate);
                directory = directory.Parent;
            }

            throw new FileNotFoundException(
                "Could not locate PanelServiceProviderFactory.cs.");
        }
    }
}
