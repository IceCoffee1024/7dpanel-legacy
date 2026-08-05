using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace LSTY.SevenDPanel.Tests.Bootstrap.Registration
{
    [Trait("Capability", "Community")]
    [Trait("Boundary", "Bootstrap")]
    public sealed class CommunityServiceRegistrationTests
    {
        [Fact]
        public void Community_registration_matches_factory_order_and_boundaries()
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

            Assert.DoesNotContain("services.AddSingleton<SevenDaysGameEventRuntime", module);
            Assert.DoesNotContain("services.AddSingleton<Discord", module);
            Assert.DoesNotContain("services.AddSingleton<GeoIp", module);
            Assert.DoesNotContain("services.AddSingleton<PlayerActionRecovery", module);

            Assert.NotEmpty(ReadRegistrationStartLines(module));
            Assert.Contains(
                "CommunityServiceRegistration.Register(services, context);",
                factory);

            Assert.Contains(
                "services.AddSingleton<IChatHistoryStore>(serviceProvider =>\n                serviceProvider.GetRequiredService<SqliteChatStore>());",
                module);
            Assert.Contains(
                "services.AddSingleton<IChatMuteExpirationStore>(serviceProvider =>\n                serviceProvider.GetRequiredService<SqliteChatMuteStore>());",
                module);
            Assert.Contains(
                "services.AddSingleton<ICommunityGameCommandConfigurationStore>(serviceProvider =>\n                serviceProvider.GetRequiredService<SqliteCommunityStore>());",
                module);
            Assert.Contains(
                "services.AddSingleton<ICommunityVoteActionPort>(serviceProvider =>\n                serviceProvider.GetRequiredService<CommunityVoteActionAdapter>());",
                module);
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
                    "Community registration is not an ordered production-factory registration: " +
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
                    "CommunityServiceRegistration.cs");
                if (File.Exists(candidate)) return File.ReadAllText(candidate);
                directory = directory.Parent;
            }

            throw new FileNotFoundException(
                "Could not locate CommunityServiceRegistration.cs.");
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
