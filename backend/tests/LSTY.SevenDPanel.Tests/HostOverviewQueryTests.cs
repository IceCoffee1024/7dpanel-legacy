using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Hosting;
using LSTY.SevenDPanel.Hosting.Platform;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class HostOverviewQueryTests
    {
        [Fact]
        public async Task Query_uses_the_injected_platform_and_returns_windows_host_fields()
        {
            var platform = FakePlatform.Windows();
            var query = CreateQuery(platform, PanelOverviewOptions.Disabled);

            var snapshot = await query.GetHostOverviewAsync(TestContext.Current.CancellationToken);

            Assert.Equal(AvailabilityState.Available, snapshot.Availability);
            Assert.Equal("Windows", snapshot.OperatingSystem);
            Assert.Equal("Administrator", snapshot.CurrentSystemUser);
            Assert.StartsWith("7dp_device_", snapshot.DeviceId);
            Assert.Equal(HostAdditionalMemoryKind.WindowsVirtualAddressSpace, snapshot.AdditionalMemory!.Kind);
            Assert.Equal(16_000L, snapshot.MemoryTotalBytes);
            Assert.Equal(4_000L, snapshot.MemoryAvailableBytes);
            Assert.Equal(32_000L, snapshot.AdditionalMemory.TotalBytes);
            Assert.Equal(8_000L, snapshot.AdditionalMemory.UsedBytes);
            Assert.Equal("windows", snapshot.OsFamily);
            Assert.Equal("x64", snapshot.OperatingSystemArchitecture);
            Assert.Equal("4.8.1", snapshot.RuntimeVersion);
            Assert.Equal("AMD EPYC", snapshot.CpuModel);
            Assert.Equal(8, snapshot.LogicalCoreCount);
            Assert.Equal(3200d, snapshot.CpuFrequencyMhz);
            Assert.Equal("WIN-HOST", snapshot.DeviceName);
            Assert.Equal("Windows Server", snapshot.DeviceModel);
            Assert.Equal("server", snapshot.DeviceType);
            Assert.Equal(1337, snapshot.ProcessId);
            Assert.Equal(new DateTimeOffset(2026, 7, 25, 0, 0, 0, TimeSpan.Zero), snapshot.ProcessStartedAtUtc);
            Assert.Equal(1, platform.PlatformInfoReadCount);
        }

        [Fact]
        public async Task Query_returns_null_cpu_for_its_first_sample_and_a_delta_for_the_second()
        {
            var platform = FakePlatform.Linux();
            var query = CreateQuery(platform, PanelOverviewOptions.Disabled);

            var first = await query.GetHostOverviewAsync(TestContext.Current.CancellationToken);
            var second = await query.GetHostOverviewAsync(TestContext.Current.CancellationToken);

            Assert.Null(first.CpuUsagePercent);
            Assert.Equal(50d, second.CpuUsagePercent);
        }

        [Fact]
        public async Task Cpu_sampler_serializes_counter_reads_before_updating_the_previous_sample()
        {
            var platform = new ConcurrentReadDetectingPlatform();
            var sampler = new HostCpuSampler();
            Assert.Null(sampler.Sample(platform));

            var first = RunOnDedicatedThread(() => sampler.Sample(platform));
            Assert.True(platform.FirstConcurrentReadEntered.Wait(TimeSpan.FromSeconds(2)));
            var secondStarted = new ManualResetEventSlim(false);
            var second = RunOnDedicatedThread(() =>
            {
                secondStarted.Set();
                return sampler.Sample(platform);
            });
            Assert.True(secondStarted.Wait(TimeSpan.FromSeconds(2)));

            SpinWait.SpinUntil(() => platform.ConcurrentReadDetected.IsSet, TimeSpan.FromMilliseconds(250));
            platform.ReleaseFirstConcurrentRead.Set();
            await Task.WhenAll(first, second);

            Assert.False(platform.ConcurrentReadDetected.IsSet);
        }

        [Fact]
        public async Task Linux_memory_uses_swap_and_linux_user_without_reading_the_real_host()
        {
            var platform = FakePlatform.Linux();
            var query = CreateQuery(platform, PanelOverviewOptions.Disabled);

            var snapshot = await query.GetHostOverviewAsync(TestContext.Current.CancellationToken);

            Assert.Equal("Linux", snapshot.OperatingSystem);
            Assert.Equal("seven", snapshot.CurrentSystemUser);
            Assert.Equal(HostAdditionalMemoryKind.LinuxSwap, snapshot.AdditionalMemory!.Kind);
            Assert.Equal(4_000L, snapshot.AdditionalMemory.TotalBytes);
            Assert.Equal(3_000L, snapshot.AdditionalMemory.UsedBytes);
            Assert.Equal("linux", snapshot.OsFamily);
            Assert.Equal("x64", snapshot.OperatingSystemArchitecture);
            Assert.Equal("Mono 6.12", snapshot.RuntimeVersion);
            Assert.Equal("Intel Xeon", snapshot.CpuModel);
            Assert.Equal(4, snapshot.LogicalCoreCount);
            Assert.Equal(2400d, snapshot.CpuFrequencyMhz);
            Assert.Equal("linux-host", snapshot.DeviceName);
            Assert.Equal("KVM", snapshot.DeviceModel);
            Assert.Equal("virtual-machine", snapshot.DeviceType);
            Assert.Equal(7331, snapshot.ProcessId);
            Assert.Equal(new DateTimeOffset(2026, 7, 24, 23, 0, 0, TimeSpan.Zero), snapshot.ProcessStartedAtUtc);
            Assert.Equal(0, platform.RealHostAccessCount);
        }

        [Fact]
        public async Task Storage_keeps_all_fixed_volumes_filters_overlay_and_isolates_a_broken_volume()
        {
            var platform = FakePlatform.Linux();
            platform.Volumes = new IHostStorageVolumeSource[]
            {
                new FakeVolume("root", "/", true, false, 100L, 60L, false),
                new FakeVolume("data", "/srv/7dpanel/data", true, false, 200L, 150L, true),
                new FakeVolume("overlay", "/overlay", true, true, 1L, 1L, false),
                new FakeVolume("cdrom", "/media/cdrom", false, false, 1L, 1L, false),
                new ThrowingVolume("locked", "/srv/locked")
            };
            var query = CreateQuery(platform, PanelOverviewOptions.Disabled);

            var snapshot = await query.GetHostOverviewAsync(TestContext.Current.CancellationToken);

            Assert.Equal(3, snapshot.StorageVolumes.Count);
            Assert.Contains(snapshot.StorageVolumes, volume => volume.Name == "root" && volume.TotalBytes == 100L && volume.IsPrimaryDataVolume == false);
            Assert.Contains(snapshot.StorageVolumes, volume => volume.Name == "data" && volume.RootPath == "/srv/7dpanel/data" && volume.IsPrimaryDataVolume == true);
            Assert.Contains(snapshot.StorageVolumes, volume => volume.Name == "locked" && volume.TotalBytes == null && volume.AvailableBytes == null);
        }

        [Theory]
        [InlineData(HostPlatformFamily.Windows, "C:\\7dpanel\\data", "C:\\", "D:\\")]
        [InlineData(HostPlatformFamily.Linux, "/srv/7dpanel/data", "/srv", "/var")]
        public void Storage_marks_the_volume_containing_the_normalized_data_directory(
            HostPlatformFamily family,
            string dataDirectory,
            string primaryRoot,
            string secondaryRoot)
        {
            var platform = FakePlatform.Linux();
            platform.Volumes = new IHostStorageVolumeSource[]
            {
                new FakeVolume("primary", primaryRoot, true, false, 100L, 60L, null),
                new FakeVolume("secondary", secondaryRoot, true, false, 200L, 150L, null)
            };

            var volumes = new HostStorageSampler(dataDirectory).Sample(platform, family).ToArray();

            Assert.True(volumes.Single(volume => volume.Name == "primary").IsPrimaryDataVolume);
            Assert.False(volumes.Single(volume => volume.Name == "secondary").IsPrimaryDataVolume);
        }

        [Fact]
        public void Linux_mounts_decode_escaped_paths_and_keep_invalid_drive_metadata_isolated()
        {
            var adapter = new LinuxHostPlatformAdapter(path => path == "/proc/mounts"
                ? "/dev/sda1 /srv/data\\040with\\011tabs\\012and\\134slashes ext4 rw 0 0"
                : null);
            var platform = new StorageOnlyPlatform(adapter.ReadStorageVolumes());

            var volume = Assert.Single(new HostStorageSampler("/srv/data with\ttabs\nand\\slashes")
                .Sample(platform, HostPlatformFamily.Linux));

            Assert.Equal("/srv/data with\ttabs\nand\\slashes", volume.RootPath);
            Assert.Null(volume.TotalBytes);
            Assert.Null(volume.AvailableBytes);
        }

        [Fact]
        public void Storage_sampler_keeps_volumes_collected_before_an_enumeration_failure()
        {
            var platform = new StorageOnlyPlatform(ThrowAfterFirstVolume());

            var volume = Assert.Single(new HostStorageSampler("C:\\7dpanel\\data")
                .Sample(platform, HostPlatformFamily.Windows));

            Assert.Equal("C", volume.Name);
            Assert.Equal(100L, volume.TotalBytes);
        }

        [Fact]
        public async Task Configured_public_addresses_win_without_starting_auto_detection()
        {
            var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("198.51.100.99")
            });
            var options = PanelOverviewOptions.FromBinding("203.0.113.7", "2001:db8::7", true, "https://example.test/ip");
            var resolver = new PublicNetworkAddressResolver(options, handler, () => DateTimeOffset.UtcNow);

            var network = await resolver.ResolveAsync(TestContext.Current.CancellationToken);

            Assert.Equal("203.0.113.7", network.Ipv4);
            Assert.Equal("2001:db8::7", network.Ipv6);
            Assert.Equal(0, handler.CallCount);
        }

        [Fact]
        public async Task Auto_detection_is_disabled_by_default_and_unavailable_without_configuration()
        {
            var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("198.51.100.99")
            });
            var resolver = new PublicNetworkAddressResolver(PanelOverviewOptions.Disabled, handler, () => DateTimeOffset.UtcNow);

            var network = await resolver.ResolveAsync(TestContext.Current.CancellationToken);

            Assert.Equal(AvailabilityState.Unavailable, network.Availability);
            Assert.Equal(0, handler.CallCount);
        }

        [Fact]
        public async Task Public_resolver_caches_successes_and_returns_unavailable_when_the_endpoint_fails()
        {
            var now = new DateTimeOffset(2026, 7, 25, 0, 0, 0, TimeSpan.Zero);
            var responses = new Queue<string>(new[] { "198.51.100.42", "198.51.100.43" });
            var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responses.Dequeue())
            });
            var resolver = new PublicNetworkAddressResolver(
                PanelOverviewOptions.FromBinding(null, null, true, "https://example.test/ip"),
                handler,
                () => now);

            var first = await resolver.ResolveAsync(TestContext.Current.CancellationToken);
            now = now.AddMinutes(19).AddSeconds(59);
            var second = await resolver.ResolveAsync(TestContext.Current.CancellationToken);

            now = now.AddSeconds(1);
            var refreshed = await resolver.ResolveAsync(TestContext.Current.CancellationToken);

            Assert.Equal("198.51.100.42", first.Ipv4);
            Assert.Equal("198.51.100.42", second.Ipv4);
            Assert.Equal("198.51.100.43", refreshed.Ipv4);
            Assert.Equal(2, handler.CallCount);

            var failing = new PublicNetworkAddressResolver(
                PanelOverviewOptions.FromBinding(null, null, true, "https://example.test/ip"),
                new RecordingHandler(_ => Task.FromException<HttpResponseMessage>(new HttpRequestException())),
                () => now);
            var unavailable = await failing.ResolveAsync(TestContext.Current.CancellationToken);
            Assert.Equal(AvailabilityState.Unavailable, unavailable.Availability);
        }

        [Fact]
        public async Task Public_resolver_contains_non_http_failures_but_propagates_caller_cancellation()
        {
            var options = PanelOverviewOptions.FromBinding(null, null, true, "https://example.test/ip");
            var resolver = new PublicNetworkAddressResolver(
                options,
                new RecordingHandler(_ => Task.FromException<HttpResponseMessage>(new InvalidOperationException("invalid content"))),
                () => DateTimeOffset.UtcNow);

            var unavailable = await resolver.ResolveAsync(TestContext.Current.CancellationToken);
            Assert.Equal(AvailabilityState.Unavailable, unavailable.Availability);

            var contentFailure = new PublicNetworkAddressResolver(
                options,
                new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ThrowingContent() }),
                () => DateTimeOffset.UtcNow);
            var contentUnavailable = await contentFailure.ResolveAsync(TestContext.Current.CancellationToken);
            Assert.Equal(AvailabilityState.Unavailable, contentUnavailable.Availability);

            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolver.ResolveAsync(cancellation.Token));
            }
        }

        [Fact]
        public async Task Public_resolver_propagates_cancellation_while_a_shared_request_is_in_progress()
        {
            var handler = new DeferredHandler();
            var resolver = new PublicNetworkAddressResolver(
                PanelOverviewOptions.FromBinding(null, null, true, "https://example.test/ip"),
                handler,
                () => DateTimeOffset.UtcNow);
            using (var cancellation = new CancellationTokenSource())
            {
                var resolving = resolver.ResolveAsync(cancellation.Token);
                Assert.True(handler.RequestStarted.Wait(TimeSpan.FromSeconds(2)));

                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolving);
            }

            handler.Complete("198.51.100.48");
        }

        [Fact]
        public async Task Public_network_failure_does_not_make_the_host_snapshot_unavailable()
        {
            var platform = FakePlatform.Windows();
            var resolver = new PublicNetworkAddressResolver(
                PanelOverviewOptions.FromBinding(null, null, true, "https://example.test/ip"),
                new RecordingHandler(_ => Task.FromException<HttpResponseMessage>(new InvalidOperationException("content failure"))),
                () => DateTimeOffset.UtcNow);
            var query = new HostOverviewQuery(
                platform,
                new HostCpuSampler(),
                new HostMemorySampler(),
                new HostStorageSampler("C:\\7dpanel\\data"),
                new DeviceIdentityProvider("LSTY.SevenDPanel"),
                resolver,
                () => DateTimeOffset.UtcNow);

            var snapshot = await query.GetHostOverviewAsync(TestContext.Current.CancellationToken);

            Assert.Equal(AvailabilityState.Available, snapshot.Availability);
            Assert.Equal(AvailabilityState.Unavailable, snapshot.PublicNetwork.Availability);
        }

        [Fact]
        public async Task Non_https_endpoint_is_rejected_and_the_resolver_enforces_its_timeout()
        {
            Assert.Throws<InvalidDataException>(() =>
                PanelOverviewOptions.FromBinding(null, null, true, "http://example.test/ip"));

            var handler = new CancellablePendingHandler();
            var resolver = new PublicNetworkAddressResolver(
                PanelOverviewOptions.FromBinding(null, null, true, "https://example.test/ip"),
                handler,
                () => DateTimeOffset.UtcNow,
                TimeSpan.FromMilliseconds(1));

            var unavailable = await resolver.ResolveAsync(TestContext.Current.CancellationToken);
            Assert.Equal(AvailabilityState.Unavailable, unavailable.Availability);
            Assert.True(handler.CancellationObserved.Wait(TimeSpan.FromSeconds(2)));
        }

        [Fact]
        public async Task Public_resolver_coalesces_concurrent_auto_detection_requests()
        {
            var handler = new DeferredHandler();
            var resolver = new PublicNetworkAddressResolver(
                PanelOverviewOptions.FromBinding(null, null, true, "https://example.test/ip"),
                handler,
                () => DateTimeOffset.UtcNow);
            using (var start = new ManualResetEventSlim(false))
            using (var invoked = new CountdownEvent(5))
            {
                var requests = Enumerable.Range(0, 5).Select(_ => RunOnDedicatedThread(() =>
                {
                    start.Wait();
                    var request = resolver.ResolveAsync(TestContext.Current.CancellationToken);
                    invoked.Signal();
                    return request.GetAwaiter().GetResult();
                })).ToArray();

                start.Set();
                Assert.True(invoked.Wait(TimeSpan.FromSeconds(2)));
                Assert.True(handler.RequestStarted.Wait(TimeSpan.FromSeconds(2)));
                Assert.Equal(1, handler.CallCount);

                handler.Complete("198.51.100.47");
                var results = await Task.WhenAll(requests);
                Assert.All(results, result => Assert.Equal("198.51.100.47", result.Ipv4));
            }
        }

        private static HostOverviewQuery CreateQuery(FakePlatform platform, PanelOverviewOptions overview)
        {
            return new HostOverviewQuery(
                platform,
                new HostCpuSampler(),
                new HostMemorySampler(),
                new HostStorageSampler("/srv/7dpanel/data"),
                new DeviceIdentityProvider("LSTY.SevenDPanel"),
                new PublicNetworkAddressResolver(overview, new RecordingHandler(_ => Task.FromException<HttpResponseMessage>(new InvalidOperationException())), () => DateTimeOffset.UtcNow),
                () => new DateTimeOffset(2026, 7, 25, 0, 0, 0, TimeSpan.Zero));
        }

        private static Task<T> RunOnDedicatedThread<T>(Func<T> action)
        {
            return Task.Factory.StartNew(
                action,
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        private static IEnumerable<IHostStorageVolumeSource> ThrowAfterFirstVolume()
        {
            yield return new FakeVolume("C", "C:\\", true, false, 100L, 60L, null);
            throw new IOException("volume enumeration failed");
        }

        private sealed class FakePlatform : IHostPlatformAdapter
        {
            private readonly Queue<HostCpuCounters> cpuCounters;

            private FakePlatform(HostPlatformInfo info, HostMemorySample memory, IEnumerable<HostCpuCounters> cpuCounters)
            {
                Info = info;
                Memory = memory;
                this.cpuCounters = new Queue<HostCpuCounters>(cpuCounters);
                Volumes = Array.Empty<IHostStorageVolumeSource>();
            }

            public HostPlatformInfo Info { get; }
            public HostMemorySample Memory { get; }
            public IHostStorageVolumeSource[] Volumes { get; set; }
            public int PlatformInfoReadCount { get; private set; }
            public int RealHostAccessCount { get; private set; }

            public static FakePlatform Windows() => new FakePlatform(
                new HostPlatformInfo(
                    HostPlatformFamily.Windows, "Windows", "Windows Server", 8, "Administrator", "machine-guid", 1_000L, 10L, 20L, 30L,
                    "windows", "x64", "4.8.1", "AMD EPYC", 8, 3200d, "WIN-HOST", "Windows Server", "server", 1337,
                    new DateTimeOffset(2026, 7, 25, 0, 0, 0, TimeSpan.Zero)),
                new HostMemorySample(16_000L, 4_000L, 32_000L, 24_000L),
                new[] { new HostCpuCounters(100L, 30L), new HostCpuCounters(200L, 80L) });

            public static FakePlatform Linux() => new FakePlatform(
                new HostPlatformInfo(
                    HostPlatformFamily.Linux, "Linux", "6.12", 4, "seven", "machine-id", 1_000L, 10L, 20L, 30L,
                    "linux", "x64", "Mono 6.12", "Intel Xeon", 4, 2400d, "linux-host", "KVM", "virtual-machine", 7331,
                    new DateTimeOffset(2026, 7, 24, 23, 0, 0, TimeSpan.Zero)),
                new HostMemorySample(8_000L, 2_000L, 4_000L, 1_000L),
                new[] { new HostCpuCounters(100L, 30L), new HostCpuCounters(200L, 80L) });

            public HostPlatformInfo ReadPlatformInfo()
            {
                PlatformInfoReadCount++;
                return Info;
            }

            public HostCpuCounters ReadCpuCounters() => cpuCounters.Dequeue();
            public HostMemorySample ReadMemory() => Memory;
            public IEnumerable<IHostStorageVolumeSource> ReadStorageVolumes() => Volumes;
        }

        private sealed class FakeVolume : IHostStorageVolumeSource
        {
            public FakeVolume(string name, string rootPath, bool fixedDrive, bool overlay, long totalBytes, long availableBytes, bool? isPrimaryDataVolume)
            {
                Name = name; RootPath = rootPath; IsFixed = fixedDrive; IsOverlay = overlay; TotalBytes = totalBytes; AvailableBytes = availableBytes; IsPrimaryDataVolume = isPrimaryDataVolume;
            }
            public string Name { get; }
            public string RootPath { get; }
            public bool IsFixed { get; }
            public bool IsOverlay { get; }
            public long TotalBytes { get; }
            public long AvailableBytes { get; }
            public bool? IsPrimaryDataVolume { get; }
        }

        private sealed class ThrowingVolume : IHostStorageVolumeSource
        {
            public ThrowingVolume(string name, string rootPath) { Name = name; RootPath = rootPath; }
            public string Name { get; }
            public string RootPath { get; }
            public bool IsFixed => true;
            public bool IsOverlay => false;
            public long TotalBytes => throw new UnauthorizedAccessException();
            public long AvailableBytes => throw new UnauthorizedAccessException();
            public bool? IsPrimaryDataVolume => null;
        }

        private sealed class StorageOnlyPlatform : IHostPlatformAdapter
        {
            private readonly IEnumerable<IHostStorageVolumeSource> volumes;

            public StorageOnlyPlatform(IEnumerable<IHostStorageVolumeSource> volumes)
            {
                this.volumes = volumes;
            }

            public HostPlatformInfo ReadPlatformInfo() => FakePlatform.Linux().ReadPlatformInfo();
            public HostCpuCounters ReadCpuCounters() => throw new NotSupportedException();
            public HostMemorySample ReadMemory() => throw new NotSupportedException();
            public IEnumerable<IHostStorageVolumeSource> ReadStorageVolumes() => volumes;
        }

        private sealed class ConcurrentReadDetectingPlatform : IHostPlatformAdapter
        {
            private int readCount;
            private int activeReads;

            public ManualResetEventSlim FirstConcurrentReadEntered { get; } = new ManualResetEventSlim(false);
            public ManualResetEventSlim ReleaseFirstConcurrentRead { get; } = new ManualResetEventSlim(false);
            public ManualResetEventSlim ConcurrentReadDetected { get; } = new ManualResetEventSlim(false);

            public HostPlatformInfo ReadPlatformInfo() => FakePlatform.Windows().ReadPlatformInfo();

            public HostCpuCounters ReadCpuCounters()
            {
                var active = Interlocked.Increment(ref activeReads);
                try
                {
                    if (active > 1) ConcurrentReadDetected.Set();
                    var read = Interlocked.Increment(ref readCount);
                    if (read == 1) return new HostCpuCounters(100L, 20L);
                    if (read == 2)
                    {
                        FirstConcurrentReadEntered.Set();
                        ReleaseFirstConcurrentRead.Wait();
                        return new HostCpuCounters(200L, 70L);
                    }
                    return new HostCpuCounters(300L, 120L);
                }
                finally
                {
                    Interlocked.Decrement(ref activeReads);
                }
            }

            public HostMemorySample ReadMemory() => throw new NotSupportedException();
            public IEnumerable<IHostStorageVolumeSource> ReadStorageVolumes() => Array.Empty<IHostStorageVolumeSource>();
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> send;
            public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : this(message => Task.FromResult(send(message))) { }
            public RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) { this.send = send; }
            public int CallCount { get; private set; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                CallCount++;
                return send(request);
            }
        }

        private sealed class ThrowingContent : HttpContent
        {
            protected override Task SerializeToStreamAsync(System.IO.Stream stream, TransportContext? context)
            {
                return Task.FromException(new InvalidOperationException("content read failed"));
            }

            protected override bool TryComputeLength(out long length)
            {
                length = 0L;
                return false;
            }
        }

        private sealed class CancellablePendingHandler : HttpMessageHandler
        {
            private readonly TaskCompletionSource<HttpResponseMessage> pending = new TaskCompletionSource<HttpResponseMessage>();

            public ManualResetEventSlim CancellationObserved { get; } = new ManualResetEventSlim(false);

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                cancellationToken.Register(() =>
                {
                    CancellationObserved.Set();
                    pending.TrySetCanceled();
                });
                return pending.Task;
            }
        }

        private sealed class DeferredHandler : HttpMessageHandler
        {
            private readonly TaskCompletionSource<HttpResponseMessage> pending = new TaskCompletionSource<HttpResponseMessage>();
            private int callCount;

            public ManualResetEventSlim RequestStarted { get; } = new ManualResetEventSlim(false);
            public int CallCount => Volatile.Read(ref callCount);

            public void Complete(string address)
            {
                pending.TrySetResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(address)
                });
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref callCount);
                RequestStarted.Set();
                return pending.Task;
            }
        }
    }
}
