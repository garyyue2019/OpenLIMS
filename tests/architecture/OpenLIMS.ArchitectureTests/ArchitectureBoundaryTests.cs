using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace OpenLIMS.ArchitectureTests;

public sealed partial class ArchitectureBoundaryTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void Production_projects_do_not_reference_tests_or_business_implementation_roots()
    {
        var projectRoots = new[] { "src", "contracts" };
        var forbiddenSegments = new[] { "/tests/", "/src/modules/", "/src/packs/" };

        foreach (var projectRoot in projectRoots)
        {
            foreach (var projectFile in Directory.EnumerateFiles(Path.Combine(RepositoryRoot, projectRoot), "*.csproj", SearchOption.AllDirectories))
            {
                var document = XDocument.Load(projectFile);
                var references = document.Descendants("ProjectReference")
                    .Select(reference => reference.Attribute("Include")?.Value.Replace('\\', '/') ?? string.Empty)
                    .ToArray();

                Assert.All(references, reference =>
                    Assert.DoesNotContain(forbiddenSegments, segment => $"/{reference.TrimStart('.').TrimStart('/')}".Contains(segment, StringComparison.OrdinalIgnoreCase)));
            }
        }
    }

    [Fact]
    public void Engineering_spike_contains_no_production_business_module_or_pack()
    {
        Assert.False(Directory.Exists(Path.Combine(RepositoryRoot, "src", "modules")));
        Assert.False(Directory.Exists(Path.Combine(RepositoryRoot, "src", "packs")));
    }

    [Fact]
    public void Api_maps_only_the_approved_technical_routes()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "host", "api", "OpenLIMS.Api", "Program.cs"));
        var routes = RoutePattern().Matches(program).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "/health/live", "/health/ready", "/system/status", "/openapi/v1.json" };

        Assert.Equal(allowed, routes);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "OpenLIMS.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    [GeneratedRegex("Map(?:Get|Post|Put|Patch|Delete)\\(\\\"([^\\\"]+)\\\"")]
    private static partial Regex RoutePattern();
}
