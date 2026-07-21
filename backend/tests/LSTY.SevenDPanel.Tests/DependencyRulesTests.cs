using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class DependencyRulesTests
    {
        private static readonly string RepositoryRoot = FindRepositoryRoot();
        private static readonly string SourceRoot = Path.Combine(RepositoryRoot, "backend", "src");

        [Fact]
        public void Project_references_follow_architecture()
        {
            foreach (var projectPath in Directory.GetFiles(SourceRoot, "*.csproj", SearchOption.AllDirectories))
            {
                var document = XDocument.Load(projectPath);
                var references = document
                    .Descendants("ProjectReference")
                    .Select(element => ResolveProjectReference(projectPath, element))
                    .ToArray();

                if (IsIn(projectPath, "Runtime"))
                {
                    Assert.Empty(references);
                    Assert.Empty(document.Descendants("PackageReference"));
                    Assert.Empty(document.Descendants("Reference"));
                    continue;
                }

                if (IsIn(projectPath, "Core", "LSTY.SevenDPanel.Domain"))
                {
                    Assert.Empty(references);
                    continue;
                }

                if (IsIn(projectPath, "Core", "LSTY.SevenDPanel.Application"))
                {
                    Assert.All(references, reference =>
                        Assert.True(IsIn(reference, "Core", "LSTY.SevenDPanel.Domain"),
                            "Application may only reference Domain: " + reference));
                    continue;
                }

                if (IsIn(projectPath, "Adapters"))
                {
                    Assert.All(references, reference =>
                        Assert.True(
                            IsIn(reference, "Runtime", "LSTY.SevenDPanel.Hosting") ||
                            IsIn(reference, "Core", "LSTY.SevenDPanel.Application"),
                            "Adapters may only reference Hosting or Application: " + reference));
                }
            }
        }

        [Fact]
        public void Adapter_directions_do_not_reference_each_other()
        {
            var adaptersRoot = Path.Combine(SourceRoot, "Adapters");
            AssertDirectionDoesNotReference(adaptersRoot, "Inbound", ".Outbound");
            AssertDirectionDoesNotReference(adaptersRoot, "Outbound", ".Inbound");
        }

        [Fact]
        public void Bootstrap_is_the_only_mod_api_implementation()
        {
            var implementations = Directory
                .GetFiles(SourceRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => File.ReadAllText(path).Contains(": IModApi"))
                .ToArray();

            var implementation = Assert.Single(implementations);
            Assert.True(IsIn(implementation, "Bootstrap", "LSTY.SevenDPanel"),
                "IModApi must only be implemented by the Bootstrap project: " + implementation);
        }

        [Fact]
        public void Panel_host_start_is_bound_to_mod_initialization()
        {
            var modMainPath = Path.Combine(SourceRoot, "Bootstrap", "LSTY.SevenDPanel", "ModMain.cs");
            var providerFactoryPath = Path.Combine(
                SourceRoot,
                "Bootstrap",
                "LSTY.SevenDPanel",
                "DependencyInjection",
                "PanelServiceProviderFactory.cs");
            var lifecyclePath = Path.Combine(
                SourceRoot,
                "Adapters",
                "LSTY.SevenDPanel.Adapters.SevenDays",
                "Inbound",
                "Lifecycle",
                "SevenDaysGameLifecycleAdapter.cs");
            var modMainSource = File.ReadAllText(modMainPath);
            var providerFactorySource = File.ReadAllText(providerFactoryPath);
            var lifecycleSource = File.ReadAllText(lifecyclePath);

            Assert.Contains("candidateAdapter.RegisterAndStart();", modMainSource);
            Assert.Contains("PanelServiceProviderFactory.CreateRuntime(", modMainSource);
            Assert.DoesNotContain("enableUnauthenticatedDevelopmentConsoleLogStream", modMainSource, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("events.SubscribeGameStartDone", lifecycleSource);
            Assert.Contains("services.AddSingleton(_ => new ConsoleLogService(log));", providerFactorySource);
            Assert.Contains("services.AddScoped<ServerEventSseSession>();", providerFactorySource);
            Assert.Contains("ValidateOnBuild = true", providerFactorySource);
            Assert.Contains("ValidateScopes = true", providerFactorySource);

            var candidateRuntimeIndex = modMainSource.IndexOf("var candidateRuntime = PanelServiceProviderFactory.CreateRuntime(", StringComparison.Ordinal);
            var candidateAdapterIndex = modMainSource.IndexOf("var candidateAdapter = new SevenDaysGameLifecycleAdapter(candidateRuntime);", StringComparison.Ordinal);
            var registerIndex = modMainSource.IndexOf("candidateAdapter.RegisterAndStart();", StringComparison.Ordinal);
            var publishRuntimeIndex = modMainSource.IndexOf("runtime = candidateRuntime;", StringComparison.Ordinal);
            var publishAdapterIndex = modMainSource.IndexOf("adapter = candidateAdapter;", StringComparison.Ordinal);
            Assert.True(registerIndex >= 0, "Bootstrap must start the candidate lifecycle adapter.");
            Assert.True(candidateRuntimeIndex >= 0 && candidateAdapterIndex > candidateRuntimeIndex,
                "Bootstrap must build the validated service provider before lifecycle registration.");
            Assert.True(publishRuntimeIndex > registerIndex, "Bootstrap must publish the runtime only after lifecycle registration succeeds.");
            Assert.True(publishAdapterIndex > registerIndex, "Bootstrap must publish the adapter only after lifecycle registration succeeds.");
            Assert.Contains("candidateAdapter.Dispose();", modMainSource);
            Assert.Contains("candidateRuntime.Dispose();", modMainSource);

            var registeredIndex = lifecycleSource.IndexOf("registered = true;", StringComparison.Ordinal);
            var startIndex = lifecycleSource.IndexOf("runtime.Start();", StringComparison.Ordinal);
            Assert.True(registeredIndex >= 0, "Lifecycle adapter must record lifecycle registration.");
            Assert.True(startIndex > registeredIndex, "Lifecycle adapter must register all lifecycle handlers before starting the panel host.");
        }

        [Fact]
        public void Microsoft_dependency_injection_packages_follow_composition_boundary()
        {
            var projects = Directory
                .GetFiles(SourceRoot, "*.csproj", SearchOption.AllDirectories)
                .Select(path => new
                {
                    Path = path,
                    Packages = XDocument.Load(path)
                        .Descendants("PackageReference")
                        .Select(element => (string)element.Attribute("Include"))
                        .Where(include => !string.IsNullOrWhiteSpace(include))
                        .ToArray()
                })
                .ToArray();

            var implementationOwner = Assert.Single(
                projects,
                project => project.Packages.Contains("Microsoft.Extensions.DependencyInjection"));
            Assert.True(IsIn(implementationOwner.Path, "Bootstrap", "LSTY.SevenDPanel"));

            var abstractionsOwner = Assert.Single(
                projects,
                project => project.Packages.Contains("Microsoft.Extensions.DependencyInjection.Abstractions"));
            Assert.True(IsIn(abstractionsOwner.Path, "Adapters", "LSTY.SevenDPanel.Adapters.Web"));

            Assert.DoesNotContain(projects, project =>
                (IsIn(project.Path, "Runtime") ||
                 IsIn(project.Path, "Adapters", "LSTY.SevenDPanel.Adapters.SevenDays")) &&
                project.Packages.Any(package =>
                    package.StartsWith("Microsoft.Extensions.DependencyInjection", StringComparison.Ordinal)));
        }

        [Fact]
        public void Publish_script_enforces_console_log_dependency_boundary()
        {
            var publishScript = File.ReadAllText(Path.Combine(
                RepositoryRoot,
                "backend",
                "scripts",
                "Publish-Mod.ps1"));

            Assert.Contains("'UnityEngine.CoreModule.dll'", publishScript);
            Assert.Contains("$requiredNames", publishScript);
            Assert.Contains("'System.Threading.Channels.dll'", publishScript);
            Assert.Contains("'System.Threading.Tasks.Extensions.dll'", publishScript);
            Assert.Contains("'Microsoft.Extensions.DependencyInjection.dll'", publishScript);
            Assert.Contains("'Microsoft.Extensions.DependencyInjection.Abstractions.dll'", publishScript);
            Assert.Contains("'Microsoft.Bcl.AsyncInterfaces.dll'", publishScript);
            Assert.Contains("'System.Runtime.CompilerServices.Unsafe.dll'", publishScript);
            Assert.Contains("Missing required managed dependencies", publishScript);
        }

        private static void AssertDirectionDoesNotReference(
            string adaptersRoot,
            string direction,
            string forbiddenNamespace)
        {
            var directionDirectories = Directory
                .GetDirectories(adaptersRoot, direction, SearchOption.AllDirectories);

            foreach (var directory in directionDirectories)
            {
                foreach (var sourcePath in Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories))
                {
                    var source = File.ReadAllText(sourcePath);
                    Assert.False(source.Contains(forbiddenNamespace),
                        direction + " must not reference " + forbiddenNamespace.TrimStart('.') + ": " + sourcePath);
                }
            }
        }

        private static string ResolveProjectReference(string projectPath, XElement reference)
        {
            var include = (string)reference.Attribute("Include");
            Assert.False(string.IsNullOrWhiteSpace(include),
                "ProjectReference Include is required: " + projectPath);
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectPath), include));
        }

        private static bool IsIn(string path, params string[] segments)
        {
            var normalized = Path.GetFullPath(path)
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            var expected = Path.DirectorySeparatorChar +
                string.Join(Path.DirectorySeparatorChar.ToString(), segments) +
                Path.DirectorySeparatorChar;
            return normalized.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "backend", "7DPanel.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate the 7DPanel repository root.");
        }
    }
}
