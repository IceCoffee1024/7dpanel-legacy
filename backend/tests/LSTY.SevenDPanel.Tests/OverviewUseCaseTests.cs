using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Operations")]
    [Trait("Boundary", "Application")]
    public sealed class OverviewUseCaseTests
    {
        [Fact]
        public async Task Available_sources_preserve_their_source_timestamps()
        {
            var fixture = new OverviewFixture();

            var overview = await fixture.UseCase.ExecuteAsync(
                OverviewAudience.Owner,
                TestContext.Current.CancellationToken);

            Assert.Equal(fixture.GameSampledAtUtc, overview.Game.SampledAtUtc);
            Assert.Equal(fixture.HostSampledAtUtc, overview.Host.SampledAtUtc);
            Assert.Equal(fixture.ActivitySampledAtUtc, overview.RecentActivity.SampledAtUtc);
            Assert.Equal(AvailabilityState.Available, overview.Availability);
        }

        [Fact]
        public async Task All_stale_partitions_produce_stale_overall_availability()
        {
            var fixture = new OverviewFixture(AvailabilityState.Stale);

            var overview = await fixture.UseCase.ExecuteAsync(
                OverviewAudience.Owner,
                TestContext.Current.CancellationToken);

            Assert.Equal(AvailabilityState.Stale, overview.Availability);
        }

        [Fact]
        public async Task Game_query_failure_keeps_other_sections_and_marks_game_unavailable()
        {
            var fixture = new OverviewFixture();
            fixture.Game.Exception = new InvalidOperationException("game adapter detail");

            var overview = await fixture.UseCase.ExecuteAsync(
                OverviewAudience.Owner,
                TestContext.Current.CancellationToken);

            Assert.Equal(AvailabilityState.Unavailable, overview.Game.Availability);
            Assert.Equal(AvailabilityState.Available, overview.Host.Availability);
            Assert.True(overview.RestartPolicy.IsConfigured);
            Assert.Single(overview.RecentActivity.Items);
        }

        [Fact]
        public async Task Stale_game_partition_does_not_contaminate_the_available_host_partition()
        {
            var fixture = new OverviewFixture();
            fixture.Game.Result = new GameOverviewSnapshot(
                AvailabilityState.Stale,
                fixture.GameSampledAtUtc,
                "7 Days to Die",
                "My Save",
                "Navezgane",
                3600L,
                "2.0",
                "Survival",
                "Warrior",
                "CN",
                "zh-CN",
                "example.test",
                26900,
                8,
                CreateRuntimeMetrics(fixture.GameSampledAtUtc));

            var overview = await fixture.UseCase.ExecuteAsync(
                OverviewAudience.Owner,
                TestContext.Current.CancellationToken);

            Assert.Equal(AvailabilityState.Stale, overview.Availability);
            Assert.Equal(AvailabilityState.Stale, overview.Game.Availability);
            Assert.Equal(AvailabilityState.Available, overview.Host.Availability);
            Assert.Equal(fixture.HostSampledAtUtc, overview.Host.SampledAtUtc);
        }

        [Fact]
        public async Task Game_query_cancellation_is_propagated()
        {
            var fixture = new OverviewFixture();
            fixture.Game.Exception = new OperationCanceledException();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                fixture.UseCase.ExecuteAsync(OverviewAudience.Owner, CancellationToken.None));
        }

        [Fact]
        public async Task Caller_cancellation_prevents_the_synchronous_restart_policy_query()
        {
            var fixture = new OverviewFixture();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                fixture.UseCase.ExecuteAsync(OverviewAudience.Owner, cancellation.Token));

            Assert.Equal(0, fixture.RestartPolicy.CallCount);
        }

        [Fact]
        public async Task Owner_receives_host_identity_public_network_and_volume_root_paths()
        {
            var fixture = new OverviewFixture();

            var overview = await fixture.UseCase.ExecuteAsync(
                OverviewAudience.Owner,
                TestContext.Current.CancellationToken);

            Assert.Equal(AvailabilityState.Available, overview.Host.IdentityAvailability);
            Assert.Equal("device-7", overview.Host.DeviceId);
            Assert.Equal("Administrator", overview.Host.CurrentSystemUser);
            Assert.Equal("203.0.113.7", overview.Host.PublicNetwork.Ipv4);
            Assert.Equal("2001:db8::7", overview.Host.PublicNetwork.Ipv6);
            Assert.Equal("C:\\", Assert.Single(overview.Host.StorageVolumes).RootPath);
        }

        [Fact]
        public async Task Non_owner_is_denied_sensitive_host_identity_but_retains_resources_and_storage()
        {
            var fixture = new OverviewFixture();

            var overview = await fixture.UseCase.ExecuteAsync(
                OverviewAudience.NonOwner,
                TestContext.Current.CancellationToken);

            Assert.Equal(AvailabilityState.Forbidden, overview.Host.IdentityAvailability);
            Assert.Null(overview.Host.DeviceId);
            Assert.Null(overview.Host.CurrentSystemUser);
            Assert.Null(overview.Host.PublicNetwork.Ipv4);
            Assert.Null(overview.Host.PublicNetwork.Ipv6);
            var volume = Assert.Single(overview.Host.StorageVolumes);
            Assert.Null(volume.RootPath);
            Assert.Equal(1_000L, volume.TotalBytes);
            Assert.Equal(500L, volume.AvailableBytes);
            Assert.Equal(10L, overview.Host.ResidentSetBytes);
            Assert.Equal(20L, overview.Host.ManagedHeapBytes);
        }

        [Fact]
        public async Task Host_contract_preserves_system_process_and_primary_volume_fields()
        {
            var fixture = new OverviewFixture();
            var source = new[]
            {
                new HostStorageVolume("C", "C:\\", 1_000L, 500L, true)
            };
            fixture.Host.Result = CreateHost(storageVolumes: source);
            source[0] = new HostStorageVolume("D", "D:\\", 2_000L, 1_000L, false);

            var overview = await fixture.UseCase.ExecuteAsync(
                OverviewAudience.Owner,
                TestContext.Current.CancellationToken);

            Assert.Equal("windows-server-2022", overview.Host.OperatingSystem);
            Assert.Equal("Windows", overview.Host.OsFamily);
            Assert.Equal("10.0.20348", overview.Host.OperatingSystemVersion);
            Assert.Equal("x64", overview.Host.OperatingSystemArchitecture);
            Assert.Equal(".NET Framework 4.8.1", overview.Host.RuntimeVersion);
            Assert.Equal("AMD EPYC", overview.Host.CpuModel);
            Assert.Equal(8, overview.Host.LogicalCoreCount);
            Assert.Equal(3400.0, overview.Host.CpuFrequencyMhz);
            Assert.Equal("panel-host", overview.Host.DeviceName);
            Assert.Equal("Virtual Machine", overview.Host.DeviceModel);
            Assert.Equal("virtual-machine", overview.Host.DeviceType);
            Assert.Equal(4242, overview.Host.ProcessId);
            Assert.Equal(fixture.HostSampledAtUtc.AddHours(-2), overview.Host.ProcessStartedAtUtc);
            Assert.Equal(7200L, overview.Host.ProcessUptimeSeconds);
            Assert.Equal(10L, overview.Host.ResidentSetBytes);
            Assert.Equal(20L, overview.Host.ManagedHeapBytes);
            var volume = Assert.Single(overview.Host.StorageVolumes);
            Assert.Equal("C", volume.Name);
            Assert.True(volume.IsPrimaryDataVolume);
        }

        [Fact]
        public void Game_model_does_not_expose_disallowed_legacy_properties()
        {
            var names = typeof(GameOverviewSnapshot)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(property => property.Name)
                .ToArray();

            Assert.DoesNotContain("GameName", names);
            Assert.DoesNotContain("MapName", names);
            Assert.DoesNotContain("UnityHeapBytes", names);
            Assert.DoesNotContain("ServerUptimeSeconds", names);
        }

        [Fact]
        public async Task Attention_codes_are_stable_and_do_not_contain_exception_text()
        {
            var fixture = new OverviewFixture();
            fixture.Game.Exception = new InvalidOperationException("secret game adapter failure");
            fixture.Host.Result = CreateHost(
                storageVolumes: new[] { new HostStorageVolume("C", "C:\\", 100L, 5L) },
                publicNetwork: new HostPublicNetwork(AvailabilityState.Unavailable, null, null));
            fixture.RestartPolicy.Result = new RestartPolicySummary(
                AvailabilityState.Available,
                false,
                null,
                null);
            var overview = await fixture.UseCase.ExecuteAsync(
                OverviewAudience.Owner,
                TestContext.Current.CancellationToken);

            var codes = overview.Attention.Select(item => item.Code).ToArray();
            Assert.Contains("game_not_ready", codes);
            Assert.Contains("disk_space_low", codes);
            Assert.Contains("restart_script_not_configured", codes);
            Assert.Contains("public_ip_unavailable", codes);
            Assert.DoesNotContain(codes, code => code.IndexOf("secret", StringComparison.Ordinal) >= 0);
        }

        [Fact]
        public void Snapshots_copy_source_collections()
        {
            var source = new[]
            {
                new HostStorageVolume("C", "C:\\", 1_000L, 500L)
            };
            var host = CreateHost(storageVolumes: source);
            source[0] = new HostStorageVolume("D", "D:\\", 2_000L, 1_000L);

            Assert.Equal("C", Assert.Single(host.StorageVolumes).Name);
            Assert.Equal("C:\\", host.StorageVolumes[0].RootPath);
        }

        [Fact]
        public void Recent_activity_contract_uses_a_stable_key_and_defensively_copies_arguments()
        {
            var occurredAtUtc = new DateTimeOffset(2026, 7, 25, 8, 0, 0, TimeSpan.Zero);
            var sourceArguments = new Dictionary<string, string>
            {
                ["operationCode"] = "restart"
            };

            var item = new RecentActivityItem(
                occurredAtUtc,
                "server_operation_failed",
                sourceArguments);
            sourceArguments["operationCode"] = "shutdown";

            Assert.Equal("server_operation_failed", item.MessageKey);
            Assert.Equal("restart", item.MessageArguments["operationCode"]);
            Assert.Equal(item.MessageKey, item.Code);
            Assert.Null(item.Summary);
            Assert.Throws<NotSupportedException>(() =>
                ((IDictionary<string, string>)item.MessageArguments).Add("failureCode", "server_unavailable"));
        }

        [Fact]
        public void Recent_activity_snapshot_exposes_read_metadata_separately_from_activity_time()
        {
            var occurredAtUtc = new DateTimeOffset(2026, 7, 25, 8, 0, 0, TimeSpan.Zero);
            var sampledAtUtc = occurredAtUtc.AddMinutes(1);
            var snapshot = new RecentActivitySnapshot(
                AvailabilityState.Available,
                sampledAtUtc,
                totalCount: 12,
                latestOccurredAtUtc: occurredAtUtc,
                new[]
                {
                    new RecentActivityItem(
                        occurredAtUtc,
                        "panel_login_succeeded",
                        Enumerable.Empty<KeyValuePair<string, string>>())
                });

            Assert.Equal(12, snapshot.TotalCount);
            Assert.Equal(occurredAtUtc, snapshot.LatestOccurredAtUtc);
            Assert.Equal(sampledAtUtc, snapshot.SampledAtUtc);
        }

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Application")]

        private sealed class OverviewFixture
        {
            public OverviewFixture(AvailabilityState availability = AvailabilityState.Available)
            {
                GameSampledAtUtc = new DateTimeOffset(2026, 7, 25, 8, 0, 0, TimeSpan.Zero);
                HostSampledAtUtc = GameSampledAtUtc.AddSeconds(1);
                ActivitySampledAtUtc = GameSampledAtUtc.AddSeconds(2);
                Game = new RecordingGameQuery(new GameOverviewSnapshot(
                    availability,
                    GameSampledAtUtc,
                    "7 Days to Die",
                    "My Save",
                    "Navezgane",
                    3600L,
                    "2.0",
                    "Survival",
                    "Warrior",
                    "CN",
                    "zh-CN",
                    "example.test",
                    26900,
                    8,
                    CreateRuntimeMetrics(GameSampledAtUtc)));
                Host = new RecordingHostQuery(CreateHost(HostSampledAtUtc, availability: availability));
                RestartPolicy = new RecordingRestartPolicyQuery(new RestartPolicySummary(
                    availability,
                    true,
                    "daily at 04:00",
                    GameSampledAtUtc.AddDays(1)));
                RecentActivity = new RecordingRecentActivityQuery(new RecentActivitySnapshot(
                    availability,
                    ActivitySampledAtUtc,
                    new[]
                    {
                        new RecentActivityItem(GameSampledAtUtc, "restart_policy_updated", "Restart policy updated")
                    }));
                UseCase = new GetOverviewUseCase(Game, Host, RestartPolicy, RecentActivity);
            }

            public DateTimeOffset GameSampledAtUtc { get; }

            public DateTimeOffset HostSampledAtUtc { get; }

            public DateTimeOffset ActivitySampledAtUtc { get; }

            public RecordingGameQuery Game { get; }

            public RecordingHostQuery Host { get; }

            public RecordingRestartPolicyQuery RestartPolicy { get; }

            public RecordingRecentActivityQuery RecentActivity { get; }

            public GetOverviewUseCase UseCase { get; }
        }

        private static HostOverviewSnapshot CreateHost(
            DateTimeOffset? sampledAtUtc = null,
            IEnumerable<HostStorageVolume>? storageVolumes = null,
            HostPublicNetwork? publicNetwork = null,
            AvailabilityState availability = AvailabilityState.Available)
        {
            var hostSampledAtUtc = sampledAtUtc ?? new DateTimeOffset(2026, 7, 25, 8, 0, 1, TimeSpan.Zero);
            return new HostOverviewSnapshot(
                availability,
                AvailabilityState.Available,
                hostSampledAtUtc,
                7200L,
                10L,
                20L,
                30L,
                15.5,
                "windows-server-2022",
                "10.0.20348",
                8,
                100L,
                40L,
                new HostAdditionalMemory(HostAdditionalMemoryKind.WindowsVirtualAddressSpace, 200L, 50L),
                storageVolumes ?? new[] { new HostStorageVolume("C", "C:\\", 1_000L, 500L, true) },
                publicNetwork ?? new HostPublicNetwork(AvailabilityState.Available, "203.0.113.7", "2001:db8::7"),
                "device-7",
                "Administrator",
                "Windows",
                "x64",
                ".NET Framework 4.8.1",
                "AMD EPYC",
                8,
                3400.0,
                "panel-host",
                "Virtual Machine",
                "virtual-machine",
                4242,
                hostSampledAtUtc.AddHours(-2));
        }

        private static GameRuntimeMetrics CreateRuntimeMetrics(DateTimeOffset observedAtUtc) =>
            new GameRuntimeMetrics(
                new ObservedMetric<string>("Day 7, 12:00", "World.worldTime", "game-clock", observedAtUtc, null),
                new ObservedMetric<bool?>(false, "World.aiDirector.BloodMoonComponent.BloodMoonActive", "boolean", observedAtUtc, null),
                new ObservedMetric<double?>(58.5, "GameManager.frameTime", "frames/second", observedAtUtc, null),
                new ObservedMetric<int?>(3, "World.Players.Count", "count", observedAtUtc, null),
                new ObservedMetric<int?>(12, "GameManager.persistentPlayerCount", "count", observedAtUtc, null),
                new ObservedMetric<int?>(4, "World.Entities", "count", observedAtUtc, null),
                new ObservedMetric<int?>(9, "World.Entities", "count", observedAtUtc, null),
                new ObservedMetric<int?>(25, "World.Entities", "count", observedAtUtc, null),
                new ObservedMetric<int?>(144, "Chunk.InstanceCount", "count", observedAtUtc, null),
                new ObservedMetric<int?>(6, "World.Entities", "count", observedAtUtc, null),
                new ObservedMetric<long?>(123456L, "GC.GetTotalMemory(false)", "bytes", observedAtUtc, null));

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Application")]

        private sealed class RecordingGameQuery : IGameOverviewQuery
        {
            public RecordingGameQuery(GameOverviewSnapshot result)
            {
                Result = result;
            }

            public Exception? Exception { get; set; }

            public GameOverviewSnapshot Result { get; set; }

            public Task<GameOverviewSnapshot> GetGameOverviewAsync(CancellationToken cancellationToken)
            {
                return Exception == null
                    ? Task.FromResult(Result)
                    : Task.FromException<GameOverviewSnapshot>(Exception);
            }
        }

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Application")]

        private sealed class RecordingHostQuery : IHostOverviewQuery
        {
            public RecordingHostQuery(HostOverviewSnapshot result)
            {
                Result = result;
            }

            public HostOverviewSnapshot Result { get; set; }

            public Task<HostOverviewSnapshot> GetHostOverviewAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(Result);
            }
        }

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Application")]

        private sealed class RecordingRestartPolicyQuery : IRestartPolicyQuery
        {
            public RecordingRestartPolicyQuery(RestartPolicySummary result)
            {
                Result = result;
            }

            public RestartPolicySummary Result { get; set; }

            public int CallCount { get; private set; }

            public RestartPolicySummary Query()
            {
                CallCount++;
                return Result;
            }
        }

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Application")]

        private sealed class RecordingRecentActivityQuery : IRecentActivityQuery
        {
            public RecordingRecentActivityQuery(RecentActivitySnapshot result)
            {
                Result = result;
            }

            public RecentActivitySnapshot Result { get; }

            public Task<RecentActivitySnapshot> GetRecentActivityAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(Result);
            }
        }
    }
}
