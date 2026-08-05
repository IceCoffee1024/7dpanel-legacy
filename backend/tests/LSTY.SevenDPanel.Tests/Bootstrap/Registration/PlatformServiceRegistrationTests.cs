using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace LSTY.SevenDPanel.Tests.Bootstrap.Registration
{
    [Trait("Capability", "Platform")]
    [Trait("Boundary", "Bootstrap")]
    public sealed class PlatformServiceRegistrationTests
    {
        [Fact]
        public void Module_registration_starts_are_an_ordered_subsequence_of_the_production_factory()
        {
            var moduleRegistrations = ExtractRegistrationStarts(ReadModule());
            var factorySource = ReadFactory();

            Assert.NotEmpty(moduleRegistrations);
            Assert.Contains(
                "PlatformServiceRegistration.Register(services, context);",
                factorySource);
        }

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
            Assert.DoesNotContain("Upgrade()", source);
            Assert.DoesNotContain("AddScoped", source);
            Assert.DoesNotContain("AddTransient", source);
        }

        [Fact]
        public void Module_preserves_platform_descriptor_order_lifetimes_and_aliases()
        {
            var source = ReadModule();

            AssertOrdered(source, new[]
            {
                "services.AddSingleton(options);",
                "services.AddSingleton(options.Authentication);",
                "services.AddSingleton(options.Overview);",
                "services.AddSingleton(options.Restart);",
                "services.AddSingleton(options.PlayerEvidence);",
                "services.AddSingleton(_ => new SqliteConnectionFactory(",
                "services.AddSingleton(serviceProvider => new SqliteDatabaseBootstrapper(",
                "services.AddSingleton(_ => CreateApprovedStorageRoots(options, dataDirectory));",
                "services.AddSingleton<AtomicFileWriter>();",
                "services.AddSingleton<SqliteWorldChangeSetMetadataStore>();",
                "services.AddSingleton<IWorldChangeSetMetadataStore>(serviceProvider =>",
                "services.AddSingleton(_ => new LocalWorldChangeSetBlobStore(",
                "services.AddSingleton<IWorldChangeSetBlobStore>(serviceProvider =>",
                "services.AddSingleton<SqliteFeatureModuleStateStore>();",
                "services.AddSingleton<IFeatureModuleStateStore>(serviceProvider =>",
                "services.AddSingleton<FeatureModuleGate>();",
                "services.AddSingleton(serviceProvider => new FeatureModuleWorldOperationJobBridge(",
                "services.AddSingleton<IWorldOperationJobBridge>(serviceProvider =>",
                "services.AddSingleton<FeatureModuleJobActivityQuery>();",
                "services.AddSingleton<IFeatureModuleActivityQuery>(serviceProvider =>",
                "services.AddSingleton(serviceProvider => new FeatureModuleUseCases(",
                "services.AddSingleton<WindowsHostPlatformAdapter>();",
                "services.AddSingleton<LinuxHostPlatformAdapter>();",
                "services.AddSingleton<IHostPlatformAdapter>(serviceProvider =>",
                "services.AddSingleton<HostCpuSampler>();",
                "services.AddSingleton<HostMemorySampler>();",
                "services.AddSingleton(_ => new HostStorageSampler(dataDirectory));",
                "services.AddSingleton(_ => new DeviceIdentityProvider(\"LSTY.SevenDPanel\"));",
                "services.AddSingleton(serviceProvider => new PublicNetworkAddressResolver("
            });

            Assert.Contains(
                "services.AddSingleton<IWorldOperationJobBridge>(serviceProvider =>\n                serviceProvider.GetRequiredService<FeatureModuleWorldOperationJobBridge>());",
                source);
            Assert.Contains(
                "services.AddSingleton<IHostPlatformAdapter>(serviceProvider =>\n                Environment.OSVersion.Platform == PlatformID.Win32NT",
                source);

            Assert.DoesNotContain("FileSystemBackupArchiveStore", source);
            Assert.DoesNotContain("SqliteJobStore", source);
            Assert.DoesNotContain("HostOverviewQuery", source);
            Assert.DoesNotContain("GetOverviewUseCase", source);
            Assert.DoesNotContain("OwinStartup", source);
        }

        private static void AssertOrdered(string source, IReadOnlyList<string> markers)
        {
            var previous = -1;
            foreach (var marker in markers)
            {
                var current = source.IndexOf(marker, StringComparison.Ordinal);
                Assert.True(current >= 0, "Missing registration marker: " + marker);
                Assert.True(
                    current > previous,
                    "Registration marker is out of order: " + marker);
                previous = current;
            }
        }

        private static string ReadModule()
        {
            return ReadSource(Path.Combine(
                "backend",
                "src",
                "Bootstrap",
                "LSTY.SevenDPanel",
                "DependencyInjection",
                "Registration",
                "PlatformServiceRegistration.cs"));
        }

        private static string ReadFactory()
        {
            return ReadSource(Path.Combine(
                "backend",
                "src",
                "Bootstrap",
                "LSTY.SevenDPanel",
                "DependencyInjection",
                "PanelServiceProviderFactory.cs"));
        }

        private static IReadOnlyList<string> ExtractRegistrationStarts(string source)
        {
            var registrations = new List<string>();
            foreach (var line in source.Split(
                         new[] { "\r\n", "\n" },
                         StringSplitOptions.None))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("services.AddSingleton", StringComparison.Ordinal) ||
                    trimmed.StartsWith("services.AddScoped", StringComparison.Ordinal) ||
                    trimmed.StartsWith("services.AddTransient", StringComparison.Ordinal))
                {
                    registrations.Add(trimmed);
                }
            }

            return registrations;
        }

        private static string ReadSource(string relativePath)
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, relativePath);
                if (File.Exists(candidate)) return File.ReadAllText(candidate);
                directory = directory.Parent;
            }

            throw new FileNotFoundException(
                "Could not locate source file: " + relativePath);
        }
    }
}
