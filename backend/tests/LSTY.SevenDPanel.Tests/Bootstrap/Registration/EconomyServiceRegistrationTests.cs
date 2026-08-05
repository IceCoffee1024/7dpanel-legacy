using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace LSTY.SevenDPanel.Tests.Bootstrap.Registration
{
    [Trait("Capability", "Economy")]
    [Trait("Boundary", "Bootstrap")]
    public sealed class EconomyServiceRegistrationTests
    {
        [Fact]
        public void Economy_registration_matches_factory_order_and_preserves_aliases()
        {
            var module = ReadModule();
            var factory = ReadFactory();

            Assert.DoesNotContain("BuildServiceProvider", module);
            Assert.DoesNotContain(".GetService(", module);
            Assert.DoesNotContain("Task", module);
            Assert.DoesNotContain("Timer", module);
            Assert.DoesNotContain("static IServiceProvider", module);
            Assert.DoesNotContain("static ServiceProvider", module);
            Assert.DoesNotContain("CreateRuntime(", module);
            Assert.DoesNotContain("AddScoped", module);
            Assert.DoesNotContain("AddTransient", module);

            Assert.NotEmpty(ReadRegistrationStartLines(module));
            Assert.Contains(
                "EconomyServiceRegistration.Register(services, context);",
                factory);

            Assert.Contains(
                "services.AddSingleton<IEconomyLedgerStore>(serviceProvider =>\n                serviceProvider.GetRequiredService<SqliteEconomyLedgerStore>());",
                module);
            Assert.Contains(
                "services.AddSingleton<IEconomyAccountAdministrationStore>(serviceProvider =>\n                serviceProvider.GetRequiredService<SqliteEconomyLedgerStore>());",
                module);
            Assert.Contains(
                "services.AddSingleton<IRewardDeliveryJournal>(serviceProvider =>\n                serviceProvider.GetRequiredService<SqliteRewardStore>());",
                module);
            Assert.Contains(
                "services.AddSingleton<IShopCatalogQueryStore>(serviceProvider =>\n                serviceProvider.GetRequiredService<SqliteCommerceStore>());",
                module);
            Assert.Contains(
                "services.AddSingleton<IDailyRewardClaimStore>(serviceProvider =>\n                serviceProvider.GetRequiredService<SqliteCommerceStore>());",
                module);
            Assert.Contains(
                "services.AddSingleton<IDailyRewardPolicyStore>(serviceProvider =>\n                serviceProvider.GetRequiredService<SqliteCommerceStore>());",
                module);
            Assert.Contains("services.AddSingleton(serviceProvider => new RewardEvidenceRuntime(", module);
            Assert.Contains(
                "services.AddSingleton(serviceProvider => new GameResourceCatalogRuntime(",
                module);
        }

        [Fact]
        public void Economy_registration_excludes_other_boundary_ownership()
        {
            var module = ReadModule();

            Assert.DoesNotContain("services.AddSingleton<SqliteGrantItemOperationStore", module);
            Assert.DoesNotContain("services.AddSingleton<SqliteRemoveItemOperationStore", module);
            Assert.DoesNotContain("services.AddSingleton<PlayerEvidenceRuntime", module);
            Assert.DoesNotContain("services.AddSingleton<PlayerActionRecoveryRuntime", module);
            Assert.DoesNotContain("services.AddSingleton<JobsAndSchedulingRuntime", module);
            Assert.DoesNotContain("services.AddSingleton<WorldOperationRuntime", module);
            Assert.DoesNotContain("services.AddSingleton<SevenDaysChatRuntime", module);
            Assert.DoesNotContain("services.AddSingleton<CommunityVoteRuntime", module);
            Assert.DoesNotContain("services.AddSingleton<AutomationActionDispatcher", module);
            Assert.DoesNotContain("services.AddSingleton<DiscordRuntime", module);
            Assert.DoesNotContain("services.AddSingleton<GeoIpRuntime", module);
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
                    "Economy registration is not an ordered production-factory registration: " +
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
                    "EconomyServiceRegistration.cs");
                if (File.Exists(candidate)) return File.ReadAllText(candidate);
                directory = directory.Parent;
            }

            throw new FileNotFoundException(
                "Could not locate EconomyServiceRegistration.cs.");
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
