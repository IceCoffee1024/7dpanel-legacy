using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace LSTY.SevenDPanel.Tests.Bootstrap.Registration
{
    [Trait("Capability", "Automation")]
    [Trait("Boundary", "Bootstrap")]
    public sealed class AutomationServiceRegistrationTests
    {
        [Fact]
        public void Automation_registration_matches_factory_order_and_preserves_aliases()
        {
            var module = ReadModule();
            var factory = ReadFactory();

            AssertModuleIsRegistrationOnly(module);
            Assert.NotEmpty(ReadRegistrationStartLines(module));
            Assert.Contains(
                "AutomationServiceRegistration.Register(services, context);",
                factory);

            Assert.Contains(
                "services.AddSingleton<IAutomationStore>(serviceProvider =>\n                serviceProvider.GetRequiredService<SqliteAutomationStore>());",
                module);
            Assert.Contains(
                "services.AddSingleton<IAutomationExecutionRecoveryStore>(serviceProvider =>\n                serviceProvider.GetRequiredService<SqliteAutomationStore>());",
                module);
            Assert.Contains(
                "services.AddSingleton<IAutomationExecutionQuery>(serviceProvider =>\n                serviceProvider.GetRequiredService<SqliteAutomationStore>());",
                module);
            Assert.Contains(
                "services.AddSingleton<IAutomationDependencyCatalog>(serviceProvider =>\n                serviceProvider.GetRequiredService<FeatureModuleAutomationDependencyCatalog>());",
                module);
            Assert.Contains(
                "services.AddSingleton<IAutomationTargetResolver>(serviceProvider =>\n                serviceProvider.GetRequiredService<StableAutomationTargetResolver>());",
                module);
            Assert.Contains(
                "services.AddSingleton<IAutomationActionDispatcher>(serviceProvider =>\n                serviceProvider.GetRequiredService<AutomationActionDispatcher>());",
                module);
            Assert.Contains(
                "services.AddSingleton<IAutomationTriggerIngress>(serviceProvider =>\n                serviceProvider.GetRequiredService<AutomationTriggerRuntime>());",
                module);
            Assert.Contains("services.AddSingleton(serviceProvider => new AutomationRuntime(", module);
            Assert.Contains("services.AddSingleton(serviceProvider => new AutomationRecoveryRuntime(", module);
        }

        [Fact]
        public void Automation_registration_excludes_other_boundary_ownership()
        {
            var module = ReadModule();

            Assert.DoesNotContain("services.AddSingleton<Discord", module);
            Assert.DoesNotContain("services.AddSingleton<GeoIp", module);
            Assert.DoesNotContain("services.AddSingleton<SqliteAuthenticationStore", module);
            Assert.DoesNotContain("services.AddSingleton<SqliteGameEventStore", module);
            Assert.DoesNotContain("services.AddSingleton<SqliteServerOperationAuditTrail", module);
            Assert.DoesNotContain("services.AddSingleton<SevenDaysChatRuntime", module);
            Assert.DoesNotContain("services.AddSingleton<GameResourceCatalogRuntime", module);
            Assert.DoesNotContain("services.AddSingleton<JobsAndSchedulingRuntime", module);
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
                    "Automation registration is not an ordered production-factory registration: " +
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
                    "AutomationServiceRegistration.cs");
                if (File.Exists(candidate)) return File.ReadAllText(candidate);
                directory = directory.Parent;
            }

            throw new FileNotFoundException(
                "Could not locate AutomationServiceRegistration.cs.");
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
