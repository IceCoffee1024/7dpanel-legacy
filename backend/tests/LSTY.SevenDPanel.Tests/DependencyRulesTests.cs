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
