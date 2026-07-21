using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Hosting;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.ConsoleCommands;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.DependencyInjection;
using LSTY.SevenDPanel.Application.ConsoleCommands;
using LSTY.SevenDPanel.DependencyInjection;
using LSTY.SevenDPanel.Hosting;
using LSTY.SevenDPanel.Hosting.Authentication;
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
        public void Runtime_stops_inner_before_disposing_root_provider()
        {
            var order = new List<string>();
            var runtime = new RecordingRuntime(order);
            var provider = new RecordingDisposable(order);
            var subject = new ServiceProviderRuntime(runtime, provider);

            subject.Stop();
            subject.Stop();

            Assert.Equal(new[] { "runtime", "provider" }, order);
        }

        [Fact]
        public void Runtime_aggregates_stop_and_provider_disposal_failures()
        {
            var runtime = new RecordingRuntime(new List<string>(), true);
            var provider = new RecordingDisposable(new List<string>(), true);
            var subject = new ServiceProviderRuntime(runtime, provider);

            var exception = Assert.Throws<AggregateException>(() => subject.Stop());

            Assert.Equal(2, exception.InnerExceptions.Count);
            Assert.Equal(
                new[] { "runtime failure", "provider failure" },
                exception.InnerExceptions.Select(failure => failure.Message));
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
            Assert.IsType<SevenDaysRestrictedConsoleGateway>(
                provider.GetRequiredService<IRestrictedConsoleGateway>());
            Assert.NotNull(provider.GetRequiredService<ExecuteConsoleCommandUseCase>());

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

            public void Start()
            {
            }

            public void MarkGameReady()
            {
            }

            public void Stop()
            {
                order.Add("runtime");
                if (failOnStop) throw new InvalidOperationException("runtime failure");
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
