using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace LSTY.SevenDPanel.Tests.Bootstrap.Registration
{
    [Trait("Capability", "Administration")]
    [Trait("Boundary", "Bootstrap")]
    public sealed class AdministrationServiceRegistrationTests
    {
        [Fact]
        public void Administration_registration_matches_factory_order_and_preserves_aliases()
        {
            var module = ReadModule();
            var factory = ReadFactory();

            AssertModuleIsRegistrationOnly(module);
            Assert.NotEmpty(ReadRegistrationStartLines(module));
            Assert.Contains(
                "AdministrationServiceRegistration.Register(services, context);",
                factory);

            Assert.Contains(
                "services.AddSingleton<IPanelCredentialStore>(serviceProvider =>\n                serviceProvider.GetRequiredService<SqliteAuthenticationStore>());",
                module);
            Assert.Contains(
                "services.AddSingleton<IDiscordIntegrationStore>(serviceProvider =>\n                serviceProvider.GetRequiredService<SqliteDiscordIntegrationStore>());",
                module);
            Assert.Contains(
                "services.AddSingleton<IGeoIpProvider>(serviceProvider =>\n                serviceProvider.GetRequiredService<LocalMmdbGeoIpProvider>());",
                module);
            Assert.Contains(
                "services.AddSingleton<IRecentActivityWriter>(serviceProvider =>\n                serviceProvider.GetRequiredService<SqliteRecentActivityStore>());",
                module);
            Assert.Contains(
                "services.AddSingleton<IHostOverviewQuery>(serviceProvider =>\n                serviceProvider.GetRequiredService<HostOverviewQuery>());",
                module);
            Assert.Contains(
                "services.AddSingleton<IGameOverviewQuery>(serviceProvider =>\n                serviceProvider.GetRequiredService<SevenDaysGameOverviewQuery>());",
                module);
            Assert.Contains(
                "services.AddSingleton<IRestartPolicyQuery, UnavailableRestartPolicyQuery>();",
                module);
            Assert.Contains(
                "services.AddSingleton<IConsoleCommandGateway>(serviceProvider =>\n                serviceProvider.GetRequiredService<SevenDaysConsoleCommandService>());",
                module);
            Assert.Contains(
                "services.AddSingleton<IGameEventStore>(serviceProvider =>\n                serviceProvider.GetRequiredService<SqliteGameEventStore>());",
                module);
            Assert.Contains(
                "services.AddSingleton<IUnifiedAuditQuery>(serviceProvider =>\n                serviceProvider.GetRequiredService<SqliteUnifiedAuditQuery>());",
                module);
            Assert.Contains(
                "services.AddSingleton<IPanelRuntimeStatus>(serviceProvider =>\n                serviceProvider.GetRequiredService<ModHost>());",
                module);
            Assert.Contains(
                "services.AddSingleton<IModRuntime>(serviceProvider =>\n                serviceProvider.GetRequiredService<ServerOperationRecoveryRuntime>());",
                module);
            Assert.Contains("OwinStartup.RegisterAuthenticationServices(services, log);", module);
            Assert.Contains("RestartPolicySummary.Unavailable()", module);
            Assert.Contains("services.AddSingleton<IRestartPolicyQuery, UnavailableRestartPolicyQuery>();", module);
        }

        [Fact]
        public void Administration_registration_excludes_operations_and_other_capability_ownership()
        {
            var module = ReadModule();

            Assert.DoesNotContain("services.AddSingleton<SqliteServerOperationAuditTrail", module);
            Assert.DoesNotContain("services.AddSingleton<IServerOperationAuditTrail", module);
            Assert.DoesNotContain("services.AddSingleton<SqliteServerOperationStore", module);
            Assert.DoesNotContain("services.AddSingleton<ServerOperationProcessInstance", module);
            Assert.DoesNotContain("services.AddSingleton<RestartServerUseCase", module);
            Assert.DoesNotContain("services.AddSingleton<ShutdownServerUseCase", module);
            Assert.DoesNotContain("services.AddSingleton<JobsAndSchedulingRuntime", module);
            Assert.DoesNotContain("services.AddSingleton<SevenDaysChatRuntime", module);
            Assert.DoesNotContain("services.AddSingleton<SevenDaysMapProjectionRuntime", module);
            Assert.DoesNotContain("services.AddSingleton<AutomationExecutionEngine", module);
            Assert.DoesNotContain("services.AddSingleton<AutomationActionDispatcher", module);
            Assert.DoesNotContain("services.AddSingleton<GameResourceCatalogRuntime", module);
            Assert.DoesNotContain("services.AddSingleton<SqliteCommunityStore", module);
            Assert.DoesNotContain("services.AddSingleton<ICommunityStore", module);
            Assert.DoesNotContain("services.AddSingleton<SqliteEconomyLedgerStore", module);
            Assert.DoesNotContain("services.AddSingleton<IEconomyLedgerStore", module);
            Assert.DoesNotContain("services.AddSingleton<SqliteCommerceStore", module);
            Assert.DoesNotContain("services.AddSingleton<ICommerceStore", module);
            Assert.DoesNotContain("services.AddSingleton<WindowsHostPlatformAdapter>", module);
            Assert.DoesNotContain("services.AddSingleton<LinuxHostPlatformAdapter>", module);
            Assert.DoesNotContain("services.AddSingleton<IHostPlatformAdapter>", module);
            Assert.DoesNotContain("services.AddSingleton<HostCpuSampler>", module);
            Assert.DoesNotContain("services.AddSingleton<HostMemorySampler>", module);
            Assert.DoesNotContain("services.AddSingleton(_ => new HostStorageSampler(", module);
            Assert.DoesNotContain("services.AddSingleton(_ => new DeviceIdentityProvider(", module);
            Assert.DoesNotContain("services.AddSingleton(serviceProvider => new PublicNetworkAddressResolver(", module);
            Assert.DoesNotContain("services.AddScoped", module);
            Assert.DoesNotContain("services.AddTransient", module);
        }

        private static void AssertModuleIsRegistrationOnly(string source)
        {
            Assert.DoesNotContain("BuildServiceProvider", source);
            Assert.DoesNotContain(".GetService(", source);
            Assert.DoesNotContain("Task", source);
            Assert.DoesNotContain("Timer", source);
            Assert.DoesNotContain("static IServiceProvider", source);
            Assert.DoesNotContain("static ServiceProvider", source);
            Assert.DoesNotContain("CreateRuntime(", source);
            Assert.DoesNotContain("AddScoped", source);
            Assert.DoesNotContain("AddTransient", source);
            Assert.DoesNotContain("Background", source);
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
                    "Administration registration is not an ordered production-factory registration: " +
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
                    "AdministrationServiceRegistration.cs");
                if (File.Exists(candidate)) return File.ReadAllText(candidate);
                directory = directory.Parent;
            }

            throw new FileNotFoundException(
                "Could not locate AdministrationServiceRegistration.cs.");
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
