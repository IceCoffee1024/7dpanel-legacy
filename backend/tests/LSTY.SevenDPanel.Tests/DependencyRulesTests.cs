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

            var candidateRuntimeIndex = modMainSource.IndexOf("candidateRuntime = PanelServiceProviderFactory.CreateRuntime(", StringComparison.Ordinal);
            var candidateAdapterIndex = modMainSource.IndexOf("candidateAdapter = new SevenDaysGameLifecycleAdapter(candidateRuntime);", StringComparison.Ordinal);
            var registerIndex = modMainSource.IndexOf("candidateAdapter.RegisterAndStart();", StringComparison.Ordinal);
            var publishRuntimeIndex = modMainSource.IndexOf("runtime = candidateRuntime;", StringComparison.Ordinal);
            var publishAdapterIndex = modMainSource.IndexOf("adapter = candidateAdapter;", StringComparison.Ordinal);
            Assert.True(registerIndex >= 0, "Bootstrap must start the candidate lifecycle adapter.");
            Assert.True(candidateRuntimeIndex >= 0 && candidateAdapterIndex > candidateRuntimeIndex,
                "Bootstrap must build the validated service provider before lifecycle registration.");
            Assert.True(publishRuntimeIndex > registerIndex, "Bootstrap must publish the runtime only after lifecycle registration succeeds.");
            Assert.True(publishAdapterIndex > registerIndex, "Bootstrap must publish the adapter only after lifecycle registration succeeds.");
            Assert.Contains("candidateAdapter?.Dispose();", modMainSource);
            Assert.Contains("candidateRuntime?.Dispose();", modMainSource);

            var registeredIndex = lifecycleSource.IndexOf("registered = true;", StringComparison.Ordinal);
            var startIndex = lifecycleSource.IndexOf("runtime.Start();", StringComparison.Ordinal);
            Assert.True(registeredIndex >= 0, "Lifecycle adapter must record lifecycle registration.");
            Assert.True(startIndex > registeredIndex, "Lifecycle adapter must register all lifecycle handlers before starting the panel host.");
        }

        [Fact]
        public void Assembly_location_patch_precedes_runtime_composition()
        {
            var bootstrapDirectory = Path.Combine(
                SourceRoot,
                "Bootstrap",
                "LSTY.SevenDPanel");
            var modMainSource = File.ReadAllText(Path.Combine(
                bootstrapDirectory,
                "ModMain.cs"));
            var patchFiles = Directory
                .GetFiles(bootstrapDirectory, "*.cs", SearchOption.AllDirectories)
                .Where(path => File.ReadAllText(path).Contains("[HarmonyPatch]"))
                .ToArray();
            var patchSource = File.ReadAllText(Assert.Single(patchFiles));

            var modInstanceIndex = modMainSource.IndexOf(
                "ModInstance = modInstance;",
                StringComparison.Ordinal);
            var patchIndex = modMainSource.IndexOf(
                "Harmony.CreateAndPatchAll(",
                StringComparison.Ordinal);
            var locationValidationIndex = modMainSource.IndexOf(
                "typeof(ModMain).Assembly.Location",
                StringComparison.Ordinal);
            var runtimeIndex = modMainSource.IndexOf(
                "PanelServiceProviderFactory.CreateRuntime(",
                StringComparison.Ordinal);
            Assert.True(modInstanceIndex >= 0);
            Assert.True(patchIndex > modInstanceIndex);
            Assert.True(locationValidationIndex > patchIndex);
            Assert.True(runtimeIndex > locationValidationIndex);
            Assert.Contains("candidateHarmony?.UnpatchSelf();", modMainSource);
            Assert.Contains("Assembly location compatibility patch applied.", modMainSource);

            Assert.Contains("[HarmonyTargetMethod]", patchSource);
            Assert.Contains("typeof(int).Assembly.GetType()", patchSource);
            Assert.Contains("nameof(Assembly.Location)", patchSource);
            Assert.Contains("[HarmonyPostfix]", patchSource);
            Assert.Contains("string.IsNullOrEmpty(__result)", patchSource);
            Assert.Contains("ContainsAssembly(__instance)", patchSource);
            Assert.Contains("__instance.GetName().Name + \".dll\"", patchSource);

            var project = XDocument.Load(Path.Combine(
                bootstrapDirectory,
                "LSTY.SevenDPanel.csproj"));
            var harmonyReference = project
                .Descendants("Reference")
                .Single(element => string.Equals(
                    (string)element.Attribute("Include"),
                    "0Harmony",
                    StringComparison.OrdinalIgnoreCase));
            Assert.Equal(
                "$(SevenDaysHarmonyDirectory)\\0Harmony.dll",
                (string)harmonyReference.Element("HintPath"));
            Assert.Equal(
                "false",
                (string)harmonyReference.Element("Private"),
                ignoreCase: true);
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
        public void Publish_script_enforces_runtime_dependency_boundary()
        {
            var publishScript = File.ReadAllText(Path.Combine(
                RepositoryRoot,
                "backend",
                "scripts",
                "Publish-Mod.ps1"));
            var forbiddenNamesStart = publishScript.IndexOf(
                "$forbiddenNames = @(",
                StringComparison.Ordinal);
            var forbiddenNamesEnd = publishScript.IndexOf(
                "$forbiddenFiles =",
                forbiddenNamesStart,
                StringComparison.Ordinal);
            var requiredNamesStart = publishScript.IndexOf(
                "$requiredNames = @(",
                StringComparison.Ordinal);
            var requiredNamesEnd = publishScript.IndexOf(
                "$missingRequired =",
                requiredNamesStart,
                StringComparison.Ordinal);
            Assert.True(forbiddenNamesStart >= 0 && forbiddenNamesEnd > forbiddenNamesStart);
            Assert.True(requiredNamesStart >= 0 && requiredNamesEnd > requiredNamesStart);
            var forbiddenNames = publishScript.Substring(
                forbiddenNamesStart,
                forbiddenNamesEnd - forbiddenNamesStart);
            var requiredNames = publishScript.Substring(
                requiredNamesStart,
                requiredNamesEnd - requiredNamesStart);

            Assert.Contains("'UnityEngine.CoreModule.dll'", publishScript);
            Assert.Contains("'0Harmony.dll'", forbiddenNames);
            Assert.DoesNotContain("'0Harmony.dll'", requiredNames);
            Assert.Contains("$requiredNames", publishScript);
            Assert.Contains("'System.Threading.Channels.dll'", publishScript);
            Assert.Contains("'System.Threading.Tasks.Extensions.dll'", publishScript);
            Assert.Contains("'Microsoft.Extensions.DependencyInjection.dll'", publishScript);
            Assert.Contains("'Microsoft.Extensions.DependencyInjection.Abstractions.dll'", publishScript);
            Assert.Contains("'Microsoft.Bcl.AsyncInterfaces.dll'", publishScript);
            Assert.Contains("'Microsoft.CSharp.dll'", publishScript);
            Assert.Contains("'System.Runtime.CompilerServices.Unsafe.dll'", publishScript);
            Assert.Contains("'System.Runtime.InteropServices.RuntimeInformation.dll'", publishScript);
            Assert.Contains("'System.Reflection.Emit.dll'", publishScript);
            Assert.Contains("'System.Dynamic.dll'", publishScript);
            Assert.Contains("'System.ComponentModel.DataAnnotations.dll'", publishScript);
            Assert.Contains("'Dapper.dll'", publishScript);
            Assert.Contains("'dbup-core.dll'", publishScript);
            Assert.Contains("'dbup-sqlite.dll'", publishScript);
            Assert.Contains("'Microsoft.Data.Sqlite.dll'", publishScript);
            Assert.DoesNotContain("'SQLitePCLRaw.batteries_v2.dll'", forbiddenNames);
            Assert.Contains("'SQLitePCLRaw.batteries_v2.dll'", requiredNames);
            Assert.Contains("'SQLitePCLRaw.batteries_v2.dll.config'", requiredNames);
            Assert.Contains("'SQLitePCLRaw.core.dll'", publishScript);
            Assert.Contains("'SQLitePCLRaw.provider.dynamic_cdecl.dll'", publishScript);
            Assert.Contains("'e_sqlite3.dll'", publishScript);
            Assert.Contains("'runtimes\\win-x64\\native\\e_sqlite3.dll'", publishScript);
            Assert.Contains("'runtimes\\linux-x64\\native\\libe_sqlite3.so'", publishScript);
            Assert.Contains("$forbiddenRootRuntimeNames", publishScript);
            Assert.Contains("Remove-Item -LiteralPath $path -Force", publishScript);
            Assert.DoesNotContain("Copy-Item -LiteralPath $requiredNativePath", publishScript);
            Assert.Contains("$requiredRuntimeAssetPaths", publishScript);
            Assert.Contains("'System.Data.SQLite.dll'", publishScript);
            Assert.Contains("'SQLite.Interop.dll'", publishScript);
            Assert.Contains("Missing required managed dependencies", publishScript);
        }

        [Fact]
        public void Sqlite_persistence_uses_the_approved_managed_provider()
        {
            var projectPath = Path.Combine(
                SourceRoot,
                "Adapters",
                "LSTY.SevenDPanel.Adapters.Persistence.Sqlite",
                "LSTY.SevenDPanel.Adapters.Persistence.Sqlite.csproj");
            var project = XDocument.Load(projectPath);
            var bootstrapProjectPath = Path.Combine(
                SourceRoot,
                "Bootstrap",
                "LSTY.SevenDPanel",
                "LSTY.SevenDPanel.csproj");
            var bootstrapProject = XDocument.Load(bootstrapProjectPath);
            var packages = project
                .Descendants("PackageReference")
                .ToDictionary(
                    element => (string)element.Attribute("Include"),
                    element => (string)element.Attribute("Version"),
                    StringComparer.OrdinalIgnoreCase);

            Assert.Equal("10.0.9", packages["Microsoft.Data.Sqlite"]);
            Assert.Equal("2.1.12", packages["SQLitePCLRaw.bundle_e_sqlite3"]);
            Assert.Equal("2.1.12", packages["SQLitePCLRaw.lib.e_sqlite3"]);
            var nativePackage = project.Descendants("PackageReference")
                .Single(element => string.Equals(
                    (string)element.Attribute("Include"),
                    "SQLitePCLRaw.lib.e_sqlite3",
                    StringComparison.OrdinalIgnoreCase));
            Assert.Equal(
                "build;buildTransitive",
                (string)nativePackage.Attribute("ExcludeAssets"));
            Assert.DoesNotContain(
                "System.Runtime.InteropServices.RuntimeInformation",
                packages.Keys,
                StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain(packages.Keys, package =>
                package.StartsWith("System.Data.SQLite", StringComparison.OrdinalIgnoreCase));

            var expectedFrameworkReferences = new[]
            {
                "Microsoft.CSharp",
                "System.Reflection.Emit",
                "System.Dynamic",
                "System.ComponentModel.DataAnnotations"
            };
            var references = bootstrapProject.Descendants("Reference").ToDictionary(
                element => (string)element.Attribute("Include"),
                StringComparer.OrdinalIgnoreCase);
            foreach (var referenceName in expectedFrameworkReferences)
            {
                var reference = references[referenceName];
                Assert.Equal(
                    "true",
                    (string)reference.Element("Private"),
                    ignoreCase: true);
                Assert.Contains(
                    "$(SystemRoot)\\Microsoft.NET\\Framework64\\v4.0.30319\\",
                    (string)reference.Element("HintPath"));
            }

            var bootstrapContentPaths = bootstrapProject.Descendants("None")
                .Select(element => (string)element.Attribute("Include"))
                .Where(include => !string.IsNullOrWhiteSpace(include))
                .ToArray();
            Assert.Contains(bootstrapContentPaths, include => include.EndsWith(
                "System.Runtime.InteropServices.RuntimeInformation.dll",
                StringComparison.OrdinalIgnoreCase));

            var sqliteContentPaths = project.Descendants("None")
                .Select(element => (string)element.Attribute("Include"))
                .Where(include => !string.IsNullOrWhiteSpace(include))
                .ToArray();
            Assert.Contains(sqliteContentPaths, include => include.EndsWith(
                "runtimes\\win-x64\\native\\e_sqlite3.dll",
                StringComparison.OrdinalIgnoreCase));
            Assert.Contains(sqliteContentPaths, include => include.EndsWith(
                "runtimes\\linux-x64\\native\\libe_sqlite3.so",
                StringComparison.OrdinalIgnoreCase));

            var projectDirectory = Path.GetDirectoryName(projectPath)!;
            Assert.False(File.Exists(Path.Combine(projectDirectory, "SqliteRuntimeLoader.cs")));
            Assert.False(File.Exists(Path.Combine(
                projectDirectory,
                "RuntimeInformationResourceManagerShim.cs")));
            var persistenceSource = string.Join(
                Environment.NewLine,
                Directory.GetFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                    .Select(File.ReadAllText));
            Assert.DoesNotContain("SQLite3Provider_dynamic_cdecl.Setup", persistenceSource);
            Assert.DoesNotContain("raw.SetProvider", persistenceSource);
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
