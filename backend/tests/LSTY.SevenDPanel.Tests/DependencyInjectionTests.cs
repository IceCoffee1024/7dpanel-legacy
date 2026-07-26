using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Hosting;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Adapters.SevenDays.Inbound.Activity;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.ConsoleCommands;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.GameResources;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Overview;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.ServerOperations;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.ConsoleCommands;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.ConsoleLogs;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Authentication;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.DependencyInjection;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Application.ConsoleCommands;
using LSTY.SevenDPanel.DependencyInjection;
using LSTY.SevenDPanel.Hosting;
using LSTY.SevenDPanel.Hosting.Authentication;
using LSTY.SevenDPanel.Hosting.Platform;
using LSTY.SevenDPanel.Hosting.ServerEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Owin;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class DependencyInjectionTests
    {
        [Fact]
        public void Web_api_fallback_scope_reuses_scoped_services_and_disposes_once()
        {
            var disposals = 0;
            var services = new ServiceCollection();
            services.AddScoped(_ => new ScopedProbe(() => disposals++));
            using var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
            var resolver = new MicrosoftDependencyResolver(provider);

            var firstScope = resolver.BeginScope();
            var first = Assert.IsType<ScopedProbe>(firstScope.GetService(typeof(ScopedProbe)));
            Assert.Same(first, firstScope.GetService(typeof(ScopedProbe)));
            firstScope.Dispose();
            firstScope.Dispose();

            using var secondScope = resolver.BeginScope();
            var second = Assert.IsType<ScopedProbe>(secondScope.GetService(typeof(ScopedProbe)));
            Assert.NotSame(first, second);
            Assert.Equal(1, disposals);
        }

        [Fact]
        public void Web_api_scope_activates_unregistered_controllers_from_scoped_dependencies()
        {
            var services = new ServiceCollection();
            services.AddScoped<ScopedProbe>();
            using var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
            var resolver = new MicrosoftDependencyResolver(provider);

            using var scope = resolver.BeginScope();
            var controller = Assert.IsType<ScopedProbeController>(
                scope.GetService(typeof(ScopedProbeController)));

            Assert.Same(
                scope.GetService(typeof(ScopedProbe)),
                controller.Probe);
        }

        [Fact]
        public async Task Web_api_bridge_uses_the_existing_owin_scope_without_owning_it()
        {
            var disposals = 0;
            var services = new ServiceCollection();
            services.AddScoped(_ => new ScopedProbe(() => disposals++));
            using var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
            using var scope = provider.CreateScope();
            var expected = scope.ServiceProvider.GetRequiredService<ScopedProbe>();
            var terminal = new ScopeCaptureHandler(expected);
            using var bridge = new OwinScopeBridgingHandler
            {
                InnerHandler = terminal
            };
            using var invoker = new HttpMessageInvoker(bridge);
            using var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/test");
            var context = new OwinContext();
            context.Environment[ScopedServiceProviderMiddleware.EnvironmentKey] = scope;
            request.SetOwinContext(context);

            using var response = await invoker.SendAsync(request, CancellationToken.None);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Same(expected, terminal.Resolved);
            Assert.Equal(0, disposals);
            Assert.False(request.Properties.ContainsKey(HttpPropertyKeys.DependencyScope));
        }

        [Fact]
        public void Provider_validation_rejects_singleton_capture_of_scoped_service()
        {
            var services = new ServiceCollection();
            services.AddScoped<ScopedProbe>();
            services.AddSingleton<InvalidSingleton>();

            var exception = Assert.Throws<AggregateException>(() =>
                services.BuildServiceProvider(new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true
                }));

            Assert.Contains("ScopedProbe", exception.ToString());
        }

        [Fact]
        public void Runtime_dispose_stops_inner_before_disposing_root_provider()
        {
            var order = new List<string>();
            var runtime = new RecordingRuntime(order);
            var provider = new RecordingDisposable(order);
            var subject = new ServiceProviderRuntime(runtime, provider);

            subject.Dispose();
            subject.Dispose();

            Assert.Equal(new[] { "runtime", "provider" }, order);
        }

        [Fact]
        public void Runtime_dispose_keeps_provider_when_inner_stop_fails()
        {
            var order = new List<string>();
            var runtime = new RecordingRuntime(order, true);
            var provider = new RecordingDisposable(order, true);
            var subject = new ServiceProviderRuntime(runtime, provider);

            var exception = Assert.Throws<AggregateException>(() => subject.Dispose());

            Assert.Equal("runtime failure", Assert.Single(exception.InnerExceptions).Message);
            Assert.Equal(new[] { "runtime" }, order);
        }

            [Fact]
            public void Runtime_dispose_keeps_provider_until_a_timed_out_inner_stop_can_complete()
            {
                var order = new List<string>();
                var runtime = new TimeoutOnceRuntime(order);
                var provider = new RecordingDisposable(order);
                var subject = new ServiceProviderRuntime(runtime, provider);

                Assert.Throws<AggregateException>(() => subject.Dispose());
                Assert.Equal(new[] { "runtime-timeout" }, order);

                subject.Dispose();

                Assert.Equal(
                new[] { "runtime-timeout", "runtime-complete", "provider" },
                order);
            }

        [Fact]
        public void Runtime_world_stop_preserves_provider_for_a_later_ready_lifecycle()
        {
            var order = new List<string>();
            var runtime = new RecordingRuntime(order);
            var provider = new RecordingDisposable(order);
            var subject = new ServiceProviderRuntime(runtime, provider);

            subject.Stop();
            subject.MarkGameReady();

            Assert.Equal(new[] { "runtime" }, order);
            Assert.Equal(1, runtime.MarkGameReadyCount);

            subject.Dispose();
            Assert.Equal(new[] { "runtime", "runtime", "provider" }, order);
        }

        [Fact]
        public void Composition_root_disposes_the_owned_sqlite_connection_factory()
        {
            var dataDirectory = Path.Combine(
                Path.GetTempPath(),
                "7dpanel-di-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dataDirectory);
            var runtime = PanelServiceProviderFactory.CreateRuntime(
                PanelHostOptions.FromBinding(18080, "127.0.0.1", "http"),
                dataDirectory,
                null,
                _ => { });
            var providerField = typeof(ServiceProviderRuntime).GetField(
                "serviceProvider",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(providerField);
            var provider = Assert.IsAssignableFrom<IServiceProvider>(
                providerField.GetValue(runtime));
            var factory = provider.GetRequiredService<SqliteConnectionFactory>();
            var authenticationStore = provider.GetRequiredService<SqliteAuthenticationStore>();

            Assert.Same(
                authenticationStore,
                provider.GetRequiredService<IPanelCredentialStore>());
            Assert.Same(
                authenticationStore,
                provider.GetRequiredService<IPanelAccessTokenStore>());
            Assert.Same(
                authenticationStore,
                provider.GetRequiredService<IPanelApiKeyStore>());
            var commandService = provider.GetRequiredService<SevenDaysConsoleCommandService>();
            Assert.Same(
                commandService,
                provider.GetRequiredService<IConsoleCommandGateway>());
            var commandCatalog = provider.GetRequiredService<SevenDaysConsoleCommandCatalogQuery>();
            Assert.Same(
                commandCatalog,
                provider.GetRequiredService<IConsoleCommandCatalogQuery>());
            var consoleLogs = provider.GetRequiredService<ConsoleLogService>();
            Assert.Same(
                consoleLogs.LiveWindow,
                provider.GetRequiredService<IRecentConsoleLogQuery>());
            var commandAuditStore = provider.GetRequiredService<SqliteConsoleCommandAuditStore>();
            Assert.Same(
                commandAuditStore,
                provider.GetRequiredService<IConsoleCommandAuditStore>());
            Assert.NotNull(provider.GetRequiredService<ConsoleCommandAuditService>());
            Assert.NotNull(provider.GetRequiredService<ConsoleCommandRuntime>());
            Assert.NotNull(provider.GetRequiredService<ExecuteConsoleCommandUseCase>());
            var onlinePlayerQuery = provider.GetRequiredService<SevenDaysOnlinePlayerProjection>();
            Assert.Same(
                onlinePlayerQuery,
                provider.GetRequiredService<IOnlinePlayerQuery>());
            var gameResourceCatalog = provider.GetRequiredService<SevenDaysGameResourceCatalog>();
            Assert.Same(
                gameResourceCatalog,
                provider.GetRequiredService<IGameResourceCatalog>());
            Assert.NotNull(provider.GetRequiredService<QueryGameResourcesUseCase>());
            Assert.NotNull(provider.GetRequiredService<GetGameResourceIconUseCase>());
            Assert.IsType<GameResourceCatalogRuntime>(
                provider.GetRequiredService<IModRuntime>());
            Assert.NotNull(provider.GetRequiredService<PlayerHistoryRuntime>());
            Assert.NotNull(provider.GetRequiredService<GetOnlinePlayersUseCase>());
            Assert.NotNull(provider.GetRequiredService<IPlayerHistoryStore>());
            Assert.NotNull(provider.GetRequiredService<PlayerHistoryWriteService>());
            Assert.NotNull(provider.GetRequiredService<GetHistoricalPlayersUseCase>());
            Assert.NotNull(provider.GetRequiredService<GetHistoricalPlayerUseCase>());
            Assert.NotNull(provider.GetRequiredService<GetPlayerHistorySnapshotsUseCase>());
            var playerActionAuditTrail = provider.GetRequiredService<SqlitePlayerActionAuditTrail>();
            Assert.Same(
                playerActionAuditTrail,
                provider.GetRequiredService<IPlayerActionAuditTrail>());
            var playerActions = provider.GetRequiredService<SevenDaysPlayerActions>();
            Assert.Same(
                playerActions,
                provider.GetRequiredService<IPlayerActions>());
            Assert.NotNull(provider.GetRequiredService<KickPlayerUseCase>());

            try
            {
                runtime.Dispose();

                var exception = Record.Exception(() =>
                {
                    using var connection = factory.Open();
                });
                Assert.IsType<ObjectDisposedException>(exception);
            }
            finally
            {
                try { runtime.Dispose(); } catch { }
                factory.Dispose();
                if (Directory.Exists(dataDirectory))
                    Directory.Delete(dataDirectory, recursive: true);
            }
        }

        [Fact]
        public void Runtime_graph_shares_writer_and_disposes_recorder()
        {
            var dataDirectory = Path.Combine(
                Path.GetTempPath(),
                "7dpanel-di-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dataDirectory);
            var runtime = PanelServiceProviderFactory.CreateRuntime(
                PanelHostOptions.FromBinding(18080, "127.0.0.1", "http"),
                dataDirectory,
                null,
                _ => { });
            var providerField = typeof(ServiceProviderRuntime).GetField(
                "serviceProvider",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(providerField);
            var provider = Assert.IsAssignableFrom<IServiceProvider>(
                providerField.GetValue(runtime));

            var windows = provider.GetRequiredService<WindowsHostPlatformAdapter>();
            var linux = provider.GetRequiredService<LinuxHostPlatformAdapter>();
            Assert.Same(windows, provider.GetRequiredService<WindowsHostPlatformAdapter>());
            Assert.Same(linux, provider.GetRequiredService<LinuxHostPlatformAdapter>());
            Assert.True(
                ReferenceEquals(provider.GetRequiredService<IHostPlatformAdapter>(), windows) ||
                ReferenceEquals(provider.GetRequiredService<IHostPlatformAdapter>(), linux));
            Assert.Same(
                provider.GetRequiredService<HostOverviewQuery>(),
                provider.GetRequiredService<IHostOverviewQuery>());
            Assert.Same(
                provider.GetRequiredService<SevenDaysGameOverviewQuery>(),
                provider.GetRequiredService<IGameOverviewQuery>());
            Assert.Same(
                provider.GetRequiredService<SqliteServerOperationAuditTrail>(),
                provider.GetRequiredService<IServerOperationAuditTrail>());
            Assert.Same(
                provider.GetRequiredService<RestartScriptLauncher>(),
                provider.GetRequiredService<IRestartScriptLauncher>());
            Assert.Same(
                provider.GetRequiredService<SevenDaysShutdownServerGateway>(),
                provider.GetRequiredService<IShutdownServerGateway>());
            Assert.Same(
                provider.GetRequiredService<GetOverviewUseCase>(),
                provider.GetRequiredService<GetOverviewUseCase>());
            Assert.Same(
                provider.GetRequiredService<RestartServerUseCase>(),
                provider.GetRequiredService<RestartServerUseCase>());
            Assert.Same(
                provider.GetRequiredService<ShutdownServerUseCase>(),
                provider.GetRequiredService<ShutdownServerUseCase>());

            var activityStore = provider.GetRequiredService<SqliteRecentActivityStore>();
            var writer = provider.GetRequiredService<IRecentActivityWriter>();
            Assert.Same(activityStore, provider.GetRequiredService<IRecentActivityQuery>());
            Assert.Same(activityStore, writer);
            var oauth = provider.GetRequiredService<PanelOAuthAuthorizationServerProvider>();
            var recorder = provider.GetRequiredService<SevenDaysRecentActivityRecorder>();
            var activityRuntime = provider.GetRequiredService<SevenDaysRecentActivityRuntime>();
            Assert.Same(
                writer,
                GetPrivateField<IRecentActivityWriter>(oauth, "recentActivityWriter"));
            Assert.Same(
                writer,
                GetPrivateField<IRecentActivityWriter>(recorder, "writer"));
            Assert.Same(
                recorder,
                GetPrivateField<SevenDaysRecentActivityRecorder>(activityRuntime, "recorder"));
            Assert.False(GetPrivateField<bool>(recorder, "started"));

            try
            {
                runtime.Dispose();

                Assert.True(GetPrivateField<bool>(recorder, "disposed"));
                Assert.False(GetPrivateField<bool>(recorder, "started"));
                Assert.Null(GetPrivateField<IDisposable>(recorder, "joinedSubscription"));
                Assert.Null(GetPrivateField<IDisposable>(recorder, "leftSubscription"));
            }
            finally
            {
                try { runtime.Dispose(); } catch { }
                if (Directory.Exists(dataDirectory))
                    Directory.Delete(dataDirectory, recursive: true);
            }
        }

        [Fact]
        public void Activity_runtime_starts_once_and_unsubscribes_on_stop()
        {
            var subscriptions = 0;
            var disposals = 0;
            var recorder = new SevenDaysRecentActivityRecorder(
                _ =>
                {
                    subscriptions++;
                    return new CallbackDisposable(() => disposals++);
                },
                _ =>
                {
                    subscriptions++;
                    return new CallbackDisposable(() => disposals++);
                },
                new NullRecentActivityWriter(),
                _ => { });
            var inner = new RecordingRuntime(new List<string>());
            var subject = new SevenDaysRecentActivityRuntime(recorder, inner);

            subject.Start();
            subject.Start();
            subject.Stop();
            subject.Stop();

            Assert.Equal(2, subscriptions);
            Assert.Equal(2, disposals);
            Assert.True(GetPrivateField<bool>(recorder, "disposed"));
        }

        [Fact]
        public async Task Activity_runtime_serializes_stop_with_an_in_progress_start()
        {
            var subscriptions = 0;
            var disposals = 0;
            var recorder = new SevenDaysRecentActivityRecorder(
                _ =>
                {
                    subscriptions++;
                    return new CallbackDisposable(() => disposals++);
                },
                _ =>
                {
                    subscriptions++;
                    return new CallbackDisposable(() => disposals++);
                },
                new NullRecentActivityWriter(),
                _ => { });
            var inner = new BlockingStartRuntime();
            var subject = new SevenDaysRecentActivityRuntime(recorder, inner);
            var stopAttempted = new ManualResetEventSlim();
            var stopReturned = new ManualResetEventSlim();

            var start = Task.Run(subject.Start);
            Assert.True(inner.StartEntered.Wait(TimeSpan.FromSeconds(5)));
            var stop = Task.Run(() =>
            {
                stopAttempted.Set();
                subject.Stop();
                stopReturned.Set();
            });
            Assert.True(stopAttempted.Wait(TimeSpan.FromSeconds(5)));
            var returnedWhileStartWasBlocked = stopReturned.Wait(TimeSpan.FromMilliseconds(250));

            inner.AllowStart.Set();
            await start;
            await stop;

            Assert.False(returnedWhileStartWasBlocked);
            Assert.False(inner.IsRunning);
            Assert.Equal(1, inner.StartCalls);
            Assert.Equal(1, inner.StopCalls);
            Assert.Equal(2, subscriptions);
            Assert.Equal(2, disposals);
        }

        [Fact]
        public async Task Runtime_dispose_drains_accepted_activity_before_disposing_writer()
        {
            Action<string>? joined = null;
            var writer = new DisposalAwareRecentActivityWriter();
            var recorder = new SevenDaysRecentActivityRecorder(
                handler =>
                {
                    joined = handler;
                    return new CallbackDisposable(() => { });
                },
                _ => new CallbackDisposable(() => { }),
                writer,
                _ => { });
            var services = new ServiceCollection();
            services.AddSingleton(_ => writer);
            services.AddSingleton(_ => recorder);
            var provider = services.BuildServiceProvider();
            provider.GetRequiredService<DisposalAwareRecentActivityWriter>();
            provider.GetRequiredService<SevenDaysRecentActivityRecorder>();
            var runtime = new ServiceProviderRuntime(
                new SevenDaysRecentActivityRuntime(
                    recorder,
                    new RecordingRuntime(new List<string>())),
                provider);
            runtime.Start();

            joined!("Amy");
            Assert.True(writer.WriteEntered.Wait(TimeSpan.FromSeconds(5)));
            var dispose = Task.Run(runtime.Dispose);
            var writerDisposedBeforeWriteFinished =
                writer.Disposed.Wait(TimeSpan.FromMilliseconds(250));

            writer.CompleteWrite.TrySetResult(true);
            await dispose;

            Assert.False(writerDisposedBeforeWriteFinished);
            Assert.True(writer.WriteFinished.IsSet);
            Assert.True(writer.Disposed.IsSet);
        }

        [Fact]
        public async Task Runtime_dispose_retry_keeps_provider_until_a_timed_out_write_finishes()
        {
            Action<string>? joined = null;
            var logs = new List<string>();
            var writer = new DisposalAwareRecentActivityWriter();
            var recorder = new SevenDaysRecentActivityRecorder(
                handler =>
                {
                    joined = handler;
                    return new CallbackDisposable(() => { });
                },
                _ => new CallbackDisposable(() => { }),
                writer,
                logs.Add);
            var services = new ServiceCollection();
            services.AddSingleton(_ => writer);
            services.AddSingleton(_ => recorder);
            var provider = services.BuildServiceProvider();
            provider.GetRequiredService<DisposalAwareRecentActivityWriter>();
            provider.GetRequiredService<SevenDaysRecentActivityRecorder>();
            var runtime = new ServiceProviderRuntime(
                new SevenDaysRecentActivityRuntime(
                    recorder,
                    new RecordingRuntime(new List<string>())),
                provider);
            runtime.Start();
            joined!("Amy");
            Assert.True(writer.WriteEntered.Wait(TimeSpan.FromSeconds(5)));

            Assert.Throws<AggregateException>(runtime.Dispose);
            Assert.False(writer.Disposed.IsSet);
            Assert.Equal(
                new[] { "Recent activity drain timed out; runtime resources remain active." },
                logs);
            Assert.DoesNotContain("Amy", Assert.Single(logs), StringComparison.Ordinal);
            var retry = Task.Run(runtime.Dispose);
            var writerDisposedBeforeWriteFinished =
                writer.Disposed.Wait(TimeSpan.FromMilliseconds(250));

            writer.CompleteWrite.TrySetResult(true);
            await retry;

            Assert.False(writerDisposedBeforeWriteFinished);
            Assert.True(writer.WriteFinished.IsSet);
            Assert.True(writer.Disposed.IsSet);
        }

        [Fact]
        public void Runtime_start_recovers_pending_player_actions_before_accepting_requests()
        {
            var dataDirectory = Path.Combine(
                Path.GetTempPath(),
                "7dpanel-di-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dataDirectory);
            var runtime = PanelServiceProviderFactory.CreateRuntime(
                PanelHostOptions.FromBinding(GetAvailablePort(), "127.0.0.1", "http"),
                dataDirectory,
                null,
                _ => { });
            var providerField = typeof(ServiceProviderRuntime).GetField(
                "serviceProvider",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(providerField);
            var provider = Assert.IsAssignableFrom<IServiceProvider>(
                providerField.GetValue(runtime));
            var bootstrapper = provider.GetRequiredService<SqliteDatabaseBootstrapper>();
            var audit = provider.GetRequiredService<SqlitePlayerActionAuditTrail>();
            var factory = provider.GetRequiredService<SqliteConnectionFactory>();

            try
            {
                bootstrapper.Upgrade();
                audit.CreatePending(new PlayerActionAuditIntent(
                    "pending-operation",
                    "owner",
                    7,
                    new PlayerPlatformIdentity("steam-1", "Steam"),
                    "rule violation",
                    DateTimeOffset.UtcNow));

                provider.GetRequiredService<ModHost>().Start();

                using var connection = factory.Open();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "SELECT status || ':' || failure_code FROM player_action_audit WHERE operation_id = 'pending-operation';";
                Assert.Equal("Unknown:process_interrupted", command.ExecuteScalar());
            }
            finally
            {
                try { runtime.Dispose(); } catch { }
                if (Directory.Exists(dataDirectory))
                    Directory.Delete(dataDirectory, recursive: true);
            }
        }

        private static int GetAvailablePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static T? GetPrivateField<T>(object instance, string fieldName)
        {
            var field = instance.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return (T?)field.GetValue(instance);
        }

        public sealed class ScopedProbeController : ApiController
        {
            public ScopedProbeController(ScopedProbe probe)
            {
                Probe = probe;
            }

            public ScopedProbe Probe { get; }
        }

        public sealed class ScopedProbe : IDisposable
        {
            private readonly Action? onDispose;

            public ScopedProbe()
            {
            }

            public ScopedProbe(Action onDispose)
            {
                this.onDispose = onDispose;
            }

            public void Dispose() => onDispose?.Invoke();
        }

        private sealed class InvalidSingleton
        {
            public InvalidSingleton(ScopedProbe probe)
            {
                Probe = probe;
            }

            public ScopedProbe Probe { get; }
        }

        private sealed class RecordingRuntime : IModRuntime
        {
            private readonly IList<string> order;
            private readonly bool failOnStop;

            public RecordingRuntime(IList<string> order, bool failOnStop = false)
            {
                this.order = order;
                this.failOnStop = failOnStop;
            }

            public int MarkGameReadyCount { get; private set; }

            public void Start()
            {
            }

            public void MarkGameReady()
            {
                MarkGameReadyCount++;
            }

            public void Stop()
            {
                order.Add("runtime");
                if (failOnStop) throw new InvalidOperationException("runtime failure");
            }
        }

        private sealed class BlockingStartRuntime : IModRuntime
        {
            private int running;
            private int startCalls;
            private int stopCalls;

            public ManualResetEventSlim StartEntered { get; } = new ManualResetEventSlim();
            public ManualResetEventSlim AllowStart { get; } = new ManualResetEventSlim();
            public bool IsRunning => Volatile.Read(ref running) != 0;
            public int StartCalls => Volatile.Read(ref startCalls);
            public int StopCalls => Volatile.Read(ref stopCalls);

            public void Start()
            {
                Interlocked.Increment(ref startCalls);
                StartEntered.Set();
                AllowStart.Wait();
                Volatile.Write(ref running, 1);
            }

            public void MarkGameReady()
            {
            }

            public void Stop()
            {
                Interlocked.Increment(ref stopCalls);
                Volatile.Write(ref running, 0);
            }
        }

        private sealed class TimeoutOnceRuntime : IModRuntime
        {
            private readonly IList<string> order;
            private bool firstStop = true;

            public TimeoutOnceRuntime(IList<string> order)
            {
                this.order = order;
            }

            public void Start()
            {
            }

            public void MarkGameReady()
            {
            }

            public void Stop()
            {
                if (firstStop)
                {
                    firstStop = false;
                    order.Add("runtime-timeout");
                    throw new TimeoutException("runtime still owns background work");
                }
                order.Add("runtime-complete");
            }
        }

        private sealed class RecordingDisposable : IDisposable
        {
            private readonly IList<string> order;
            private readonly bool failOnDispose;

            public RecordingDisposable(IList<string> order, bool failOnDispose = false)
            {
                this.order = order;
                this.failOnDispose = failOnDispose;
            }

            public void Dispose()
            {
                order.Add("provider");
                if (failOnDispose) throw new InvalidOperationException("provider failure");
            }
        }

        private sealed class CallbackDisposable : IDisposable
        {
            private Action? callback;

            public CallbackDisposable(Action callback)
            {
                this.callback = callback;
            }

            public void Dispose()
            {
                Interlocked.Exchange(ref callback, null)?.Invoke();
            }
        }

        private sealed class NullRecentActivityWriter : IRecentActivityWriter
        {
            public Task RecordPanelLoginSucceededAsync(string actorSubject, string actorDisplayName, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task RecordPlayerJoinedAsync(string playerDisplayName, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task RecordPlayerLeftAsync(string playerDisplayName, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task RecordRestartScriptStartedAsync(string actorSubject, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task RecordShutdownRequestedAsync(string actorSubject, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task RecordServerOperationFailedAsync(string actorSubject, string operationCode, string failureCode, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
        }

        private sealed class DisposalAwareRecentActivityWriter : IRecentActivityWriter, IDisposable
        {
            public ManualResetEventSlim WriteEntered { get; } = new ManualResetEventSlim();
            public ManualResetEventSlim WriteFinished { get; } = new ManualResetEventSlim();
            public ManualResetEventSlim Disposed { get; } = new ManualResetEventSlim();
            public TaskCompletionSource<bool> CompleteWrite { get; } =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task RecordPanelLoginSucceededAsync(string actorSubject, string actorDisplayName, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;

            public async Task RecordPlayerJoinedAsync(string playerDisplayName, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken)
            {
                WriteEntered.Set();
                await CompleteWrite.Task;
                WriteFinished.Set();
            }

            public Task RecordPlayerLeftAsync(string playerDisplayName, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task RecordRestartScriptStartedAsync(string actorSubject, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task RecordShutdownRequestedAsync(string actorSubject, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task RecordServerOperationFailedAsync(string actorSubject, string operationCode, string failureCode, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public void Dispose() => Disposed.Set();
        }

        private sealed class ScopeCaptureHandler : HttpMessageHandler
        {
            private readonly ScopedProbe expected;

            public ScopeCaptureHandler(ScopedProbe expected)
            {
                this.expected = expected;
            }

            public ScopedProbe? Resolved { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                var dependencyScope = Assert.IsAssignableFrom<System.Web.Http.Dependencies.IDependencyScope>(
                    request.Properties[HttpPropertyKeys.DependencyScope]);
                Resolved = Assert.IsType<ScopedProbe>(
                    dependencyScope.GetService(typeof(ScopedProbe)));
                Assert.Same(expected, Resolved);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }
        }
    }
}
