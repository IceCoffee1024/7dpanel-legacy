using System;
using System.IO;
using System.Reflection;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Adapters.SevenDays.Inbound.Lifecycle;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Maps;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.DependencyInjection;
using LSTY.SevenDPanel.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class PlayerMapDependencyInjectionTests
    {
        [Fact]
        public void Production_lifecycle_accepts_game_ready_after_world_shutdown_without_replacing_provider()
        {
            var dataDirectory = Path.Combine(
                Path.GetTempPath(),
                "7dpanel-map-lifecycle-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dataDirectory);
            var runtime = PanelServiceProviderFactory.CreateRuntime(
                PanelHostOptions.FromBinding(26998, "127.0.0.1", "http"),
                dataDirectory,
                null,
                _ => { });
            var providerField = typeof(ServiceProviderRuntime).GetField(
                "serviceProvider",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var provider = Assert.IsAssignableFrom<IServiceProvider>(providerField.GetValue(runtime));
            var mapRuntime = provider.GetRequiredService<SevenDaysMapProjectionRuntime>();
            var readyField = typeof(SevenDaysMapProjectionRuntime).GetField(
                "ready",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var events = new FakeLifecycleEvents();
            using var adapter = new SevenDaysGameLifecycleAdapter(runtime, events);

            try
            {
                adapter.RegisterAndStart();
                events.RaiseGameStartDone();
                events.RaiseWorldShuttingDown();
                Assert.Equal(0, readyField.GetValue(mapRuntime));

                events.RaiseGameStartDone();

                Assert.Same(provider, providerField.GetValue(runtime));
                Assert.Equal(1, readyField.GetValue(mapRuntime));
            }
            finally
            {
                try { runtime.Dispose(); } catch { }
                if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, true);
            }
        }

        [Fact]
        public void Runtime_registers_map_projection_queries_use_cases_and_runtime_wrapper()
        {
            var dataDirectory = Path.Combine(
                Path.GetTempPath(),
                "7dpanel-map-di-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dataDirectory);
            var runtime = PanelServiceProviderFactory.CreateRuntime(
                PanelHostOptions.FromBinding(26999, "127.0.0.1", "http"),
                dataDirectory,
                null,
                _ => { });
            var providerField = typeof(ServiceProviderRuntime).GetField(
                "serviceProvider",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var provider = Assert.IsAssignableFrom<IServiceProvider>(providerField!.GetValue(runtime));

            try
            {
                var metadataProjection =
                    provider.GetRequiredService<SevenDaysMapMetadataProjection>();
                var gameTimeProjection =
                    provider.GetRequiredService<SevenDaysMapGameTimeProjection>();
                var layerProjection =
                    provider.GetRequiredService<SevenDaysMapLayerProjection>();
                var transientProjection =
                    provider.GetRequiredService<SevenDaysTransientEntityProjection>();
                Assert.NotSame(metadataProjection, gameTimeProjection);
                Assert.Same(metadataProjection, provider.GetRequiredService<IMapMetadataQuery>());
                Assert.Same(gameTimeProjection, provider.GetRequiredService<IMapGameTimeQuery>());
                Assert.Same(layerProjection, provider.GetRequiredService<IMapLayerProjection>());
                Assert.Same(transientProjection, provider.GetRequiredService<ITransientEntityMapProjection>());
                Assert.Same(
                    provider.GetRequiredService<SqlitePlayerHistoryStore>(),
                    provider.GetRequiredService<IPlayerMapSpatialQueryStore>());
                Assert.NotNull(provider.GetRequiredService<GetMapMetadataUseCase>());
                Assert.NotNull(provider.GetRequiredService<GetMapGameTimeUseCase>());
                Assert.NotNull(provider.GetRequiredService<GetPlayerTrackUseCase>());
                Assert.NotNull(provider.GetRequiredService<GetMapLayerUseCase>());
                Assert.NotNull(provider.GetRequiredService<GetHistoricalPlayerLastLocationsUseCase>());
                Assert.NotNull(provider.GetRequiredService<SearchPlayersInAreaUseCase>());
                Assert.NotNull(provider.GetRequiredService<GetTransientEntityMapLayerUseCase>());
                Assert.IsType<SevenDaysMapProjectionRuntime>(provider.GetRequiredService<IModRuntime>());
            }
            finally
            {
                try { runtime.Dispose(); } catch { }
                if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, true);
            }
        }

        private sealed class FakeLifecycleEvents : ISevenDaysLifecycleEvents
        {
            private Action? gameStartDone;
            private Action? worldShuttingDown;

            public IDisposable SubscribeGameStartDone(Action handler)
            {
                gameStartDone = handler;
                return new Subscription(() => gameStartDone = null);
            }

            public IDisposable SubscribeWorldShuttingDown(Action handler)
            {
                worldShuttingDown = handler;
                return new Subscription(() => worldShuttingDown = null);
            }

            public IDisposable SubscribeGameShutdown(Action handler) =>
                new Subscription(() => { });

            public void RaiseGameStartDone() => gameStartDone!();
            public void RaiseWorldShuttingDown() => worldShuttingDown!();

            private sealed class Subscription : IDisposable
            {
                private readonly Action dispose;
                public Subscription(Action dispose) { this.dispose = dispose; }
                public void Dispose() => dispose();
            }
        }
    }
}
