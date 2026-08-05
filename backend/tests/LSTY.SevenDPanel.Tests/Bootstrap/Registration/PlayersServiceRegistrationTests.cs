using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace LSTY.SevenDPanel.Tests.Bootstrap.Registration
{
    [Trait("Capability", "Players")]
    [Trait("Boundary", "Bootstrap")]
    public sealed class PlayersServiceRegistrationTests
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
            Assert.DoesNotContain("AddTransient", source);
            Assert.DoesNotContain("Background", source);
        }

        [Fact]
        public void Module_preserves_player_descriptor_order_lifetimes_and_aliases()
        {
            var source = ReadModule();
            var factory = ReadFactory();

            Assert.NotEmpty(ReadRegistrationStartLines(source));
            Assert.Contains(
                "PlayersServiceRegistration.Register(services, context);",
                factory);

            Assert.Contains(
                "services.AddSingleton<IPlayerHistoryStore>(serviceProvider =>\n                serviceProvider.GetRequiredService<SqlitePlayerHistoryStore>());",
                source);
            Assert.Contains(
                "services.AddSingleton<IPlayerActions>(serviceProvider =>\n                serviceProvider.GetRequiredService<SevenDaysPlayerActions>());",
                source);
            Assert.Contains(
                "services.AddScoped<ServerEventSseSession>();",
                source);
            Assert.Contains(
                "services.AddSingleton<IPlayerEvidenceStore>(serviceProvider =>\n                serviceProvider.GetRequiredService<SqlitePlayerEvidenceStore>());",
                source);
            Assert.Contains(
                "services.AddSingleton<SevenDaysGrantItemEvidenceCapture>();\n            services.AddSingleton(serviceProvider => new SevenDaysGrantItemGateway(\n                serviceProvider.GetRequiredService<IGameResourceCatalog>(),\n                serviceProvider.GetRequiredService<SevenDaysGrantItemEvidenceCapture>()\n                    .FindOnlineObservedAtUtc,\n                serviceProvider.GetRequiredService<SevenDaysGrantItemEvidenceCapture>()\n                    .CaptureAsync));",
                source);
            Assert.DoesNotContain(
                "services.AddSingleton<SevenDaysGrantItemGateway>();",
                source);
        }

        [Fact]
        public void Module_excludes_registrations_owned_by_other_boundaries()
        {
            var source = ReadModule();

            Assert.DoesNotContain("services.AddSingleton<SevenDaysWorldToolCatalog", source);
            Assert.DoesNotContain("services.AddSingleton<IWorldToolCatalog", source);
            Assert.DoesNotContain("services.AddSingleton<QueryWorldUseCase", source);
            Assert.DoesNotContain("services.AddSingleton<QueryWorldToolCatalogUseCase", source);
            Assert.DoesNotContain("services.AddSingleton<MoveOnlinePlayerUseCase", source);
            Assert.DoesNotContain("services.AddSingleton<ModHost", source);
            Assert.DoesNotContain("services.AddSingleton<ConsoleLogRuntime", source);
            Assert.DoesNotContain("services.AddSingleton<ConsoleCommandRuntime", source);
            Assert.DoesNotContain("services.AddSingleton<SevenDaysRecentActivityRuntime", source);
            Assert.DoesNotContain("services.AddSingleton<SevenDaysChatRuntime", source);
            Assert.DoesNotContain("services.AddSingleton<SevenDaysGameEventRuntime", source);
            Assert.DoesNotContain("services.AddSingleton<RewardEvidenceRuntime", source);
            Assert.DoesNotContain("services.AddSingleton<GameResourceCatalogRuntime", source);
            Assert.DoesNotContain("services.AddSingleton<ThirdWaveRewardDeliveryAdapter", source);
            Assert.DoesNotContain("services.AddSingleton<IEconomyLedgerStore", source);
            Assert.DoesNotContain("services.AddSingleton<Automation", source);
            Assert.DoesNotContain("services.AddSingleton<Community", source);
            Assert.DoesNotContain("services.AddSingleton<Discord", source);
            Assert.DoesNotContain("services.AddSingleton<GeoIp", source);
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
                    "Players registration is not an ordered production-factory registration: " +
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
                    "PlayersServiceRegistration.cs");
                if (File.Exists(candidate)) return File.ReadAllText(candidate);
                directory = directory.Parent;
            }

            throw new FileNotFoundException(
                "Could not locate PlayersServiceRegistration.cs.");
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
