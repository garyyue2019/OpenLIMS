using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace OpenLIMS.ArchitectureTests;

public sealed partial class ArchitectureBoundaryTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void Production_project_references_respect_modular_monolith_boundaries()
    {
        var projects = LoadProductionProjects();
        var violations = new List<string>();
        var moduleDependencies = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in projects.Values)
        {
            foreach (var reference in project.References)
            {
                if (!IsWithinRepository(reference))
                {
                    violations.Add($"{project.RelativePath} references a project outside the repository: {reference}");
                    continue;
                }

                var targetRelativePath = RelativePath(reference);
                if (targetRelativePath.StartsWith("tests/", StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add($"{project.RelativePath} references test project {targetRelativePath}");
                    continue;
                }

                var sourceModule = GetModuleId(project.RelativePath);
                var targetModule = GetModuleId(targetRelativePath);

                if (IsPlatformFoundation(project.RelativePath) && targetModule is not null)
                {
                    violations.Add($"platform foundation {project.RelativePath} references module implementation {targetRelativePath}");
                }

                if (sourceModule is not null && targetModule is not null && !StringComparer.OrdinalIgnoreCase.Equals(sourceModule, targetModule))
                {
                    if (!IsModulePublicContract(targetRelativePath))
                    {
                        violations.Add($"module '{sourceModule}' references private implementation from module '{targetModule}': {targetRelativePath}");
                        continue;
                    }

                    if (!moduleDependencies.TryGetValue(sourceModule, out var dependencies))
                    {
                        dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        moduleDependencies.Add(sourceModule, dependencies);
                    }

                    dependencies.Add(targetModule);
                }
            }
        }

        violations.AddRange(FindModuleDependencyCycles(moduleDependencies));

        Assert.Empty(violations);
    }

    [Fact]
    public void Production_project_reference_graph_is_acyclic()
    {
        var projects = LoadProductionProjects();
        var projectPaths = projects.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var projectPath in projectPaths)
        {
            Assert.False(ContainsCycle(projectPath, projects, projectPaths, visiting, visited), $"Circular production project dependency detected from {RelativePath(projectPath)}.");
        }
    }

    [Fact]
    public void Production_contains_no_dynamic_module_pack_root()
    {
        Assert.False(Directory.Exists(Path.Combine(RepositoryRoot, "src", "packs")));
    }

    [Fact]
    public void Module_public_contracts_do_not_expose_private_persistence_types_or_tables()
    {
        var contractRoots = new[]
        {
            Path.Combine(RepositoryRoot, "contracts", "modules"),
            Path.Combine(RepositoryRoot, "contracts", "receiving"),
            Path.Combine(RepositoryRoot, "contracts", "labeling"),
            Path.Combine(RepositoryRoot, "contracts", "scope"),
            Path.Combine(RepositoryRoot, "contracts", "quantity"),
            Path.Combine(RepositoryRoot, "contracts", "allocation"),
            Path.Combine(RepositoryRoot, "contracts", "textile"),
            Path.Combine(RepositoryRoot, "contracts", "batch"),
            Path.Combine(RepositoryRoot, "src", "modules")
        };
        var violations = contractRoots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(file => IsModulePublicContract(RelativePath(file)))
            .Where(file => PrivatePersistencePattern().IsMatch(File.ReadAllText(file)))
            .Select(RelativePath)
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void Api_maps_only_the_approved_technical_routes()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "host", "api", "OpenLIMS.Api", "Program.cs"));
        var routes = RoutePattern().Matches(program).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "/health/live", "/health/ready", "/system/status", "/openapi/v1.json" };

        Assert.Equal(allowed, routes);
    }

    [Fact]
    public void Production_hosts_use_an_explicit_receiving_module_manifest()
    {
        var hostPrograms = new[]
        {
            Path.Combine(RepositoryRoot, "src", "host", "api", "OpenLIMS.Api", "Program.cs"),
            Path.Combine(RepositoryRoot, "src", "host", "worker", "OpenLIMS.Worker", "Program.cs")
        };

        Assert.All(hostPrograms, program => Assert.Matches(ReceivingModuleManifestPattern(), File.ReadAllText(program)));
        Assert.All(hostPrograms, program => Assert.Contains("new LabelingModule(", File.ReadAllText(program), StringComparison.Ordinal));
        Assert.All(hostPrograms, program => Assert.Contains("new ScopeModule(", File.ReadAllText(program), StringComparison.Ordinal));
        Assert.All(hostPrograms, program => Assert.Contains("new QuantityModule(", File.ReadAllText(program), StringComparison.Ordinal));
        Assert.All(hostPrograms, program => Assert.Contains("new BatchModule(", File.ReadAllText(program), StringComparison.Ordinal));
        Assert.All(hostPrograms, program => Assert.Contains("new AllocationModule(", File.ReadAllText(program), StringComparison.Ordinal));
        Assert.All(hostPrograms, program => Assert.DoesNotContain("Assembly.Load", File.ReadAllText(program), StringComparison.Ordinal));
    }

    [Fact]
    public void Receiving_persistence_accesses_only_its_private_schema()
    {
        var moduleRoot = Path.Combine(RepositoryRoot, "src", "modules", "receiving");
        var sql = Directory.EnumerateFiles(moduleRoot, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => SchemaAccessPattern().Matches(File.ReadAllText(file)))
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.NotEmpty(sql);
        Assert.All(sql, schema => Assert.Equal("receiving", schema, ignoreCase: true));
    }

    [Fact]
    public void Labeling_persistence_accesses_only_its_private_schema()
    {
        var moduleRoot = Path.Combine(RepositoryRoot, "src", "modules", "labeling");
        var sql = Directory.EnumerateFiles(moduleRoot, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => SchemaAccessPattern().Matches(File.ReadAllText(file)))
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.NotEmpty(sql);
        Assert.All(sql, schema => Assert.Equal("labeling", schema, ignoreCase: true));
    }

    [Fact]
    public void Scope_persistence_accesses_only_its_private_schema()
    {
        var moduleRoot = Path.Combine(RepositoryRoot, "src", "modules", "scope");
        var sql = Directory.EnumerateFiles(moduleRoot, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => SchemaAccessPattern().Matches(File.ReadAllText(file)))
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.NotEmpty(sql);
        Assert.All(sql, schema => Assert.Equal("scope", schema, ignoreCase: true));
    }

    [Fact]
    public void Batch_persistence_accesses_only_its_private_schema()
    {
        var moduleRoot = Path.Combine(RepositoryRoot, "src", "modules", "batch");
        var sql = Directory.EnumerateFiles(moduleRoot, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => SchemaAccessPattern().Matches(File.ReadAllText(file)))
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.NotEmpty(sql);
        Assert.All(sql, schema => Assert.Equal("batch", schema, ignoreCase: true));
    }

    [Fact]
    public void Quantity_persistence_accesses_only_its_private_schema()
    {
        var moduleRoot = Path.Combine(RepositoryRoot, "src", "modules", "quantity");
        var sql = Directory.EnumerateFiles(moduleRoot, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => SchemaAccessPattern().Matches(File.ReadAllText(file)))
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.NotEmpty(sql);
        Assert.All(sql, schema => Assert.Equal("quantity", schema, ignoreCase: true));
    }

    [Fact]
    public void Allocation_persistence_accesses_only_its_private_schema()
    {
        var moduleRoot = Path.Combine(RepositoryRoot, "src", "modules", "allocation");
        var sql = Directory.EnumerateFiles(moduleRoot, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => SchemaAccessPattern().Matches(File.ReadAllText(file)))
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.NotEmpty(sql);
        Assert.All(sql, schema => Assert.Equal("allocation", schema, ignoreCase: true));
    }

    private static Dictionary<string, ProductionProject> LoadProductionProjects()
    {
        var projects = new Dictionary<string, ProductionProject>(StringComparer.OrdinalIgnoreCase);
        foreach (var projectRoot in new[] { "src", "contracts" })
        {
            var absoluteRoot = Path.Combine(RepositoryRoot, projectRoot);
            if (!Directory.Exists(absoluteRoot))
            {
                continue;
            }

            foreach (var projectFile in Directory.EnumerateFiles(absoluteRoot, "*.csproj", SearchOption.AllDirectories))
            {
                var fullProjectPath = Path.GetFullPath(projectFile);
                var document = XDocument.Load(projectFile);
                var references = document.Descendants("ProjectReference")
                    .Select(reference => reference.Attribute("Include")?.Value)
                    .Where(include => !string.IsNullOrWhiteSpace(include))
                    .Select(include => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectFile)!, include!)))
                    .ToArray();

                projects.Add(fullProjectPath, new ProductionProject(RelativePath(fullProjectPath), references));
            }
        }

        return projects;
    }

    private static bool ContainsCycle(
        string projectPath,
        IReadOnlyDictionary<string, ProductionProject> projects,
        IReadOnlySet<string> projectPaths,
        ISet<string> visiting,
        ISet<string> visited)
    {
        if (visited.Contains(projectPath))
        {
            return false;
        }

        if (!visiting.Add(projectPath))
        {
            return true;
        }

        foreach (var reference in projects[projectPath].References.Where(projectPaths.Contains))
        {
            if (ContainsCycle(reference, projects, projectPaths, visiting, visited))
            {
                return true;
            }
        }

        visiting.Remove(projectPath);
        visited.Add(projectPath);
        return false;
    }

    private static IEnumerable<string> FindModuleDependencyCycles(IReadOnlyDictionary<string, HashSet<string>> dependencies)
    {
        foreach (var module in dependencies.Keys)
        {
            var stack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (ModuleDependencyContainsCycle(module, dependencies, stack, new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
            {
                yield return $"circular module dependency detected from '{module}'";
            }
        }
    }

    private static bool ModuleDependencyContainsCycle(
        string module,
        IReadOnlyDictionary<string, HashSet<string>> dependencies,
        ISet<string> stack,
        ISet<string> visited)
    {
        if (!visited.Add(module))
        {
            return stack.Contains(module);
        }

        stack.Add(module);
        if (dependencies.TryGetValue(module, out var targets))
        {
            foreach (var target in targets)
            {
                if (stack.Contains(target) || ModuleDependencyContainsCycle(target, dependencies, stack, visited))
                {
                    return true;
                }
            }
        }

        stack.Remove(module);
        return false;
    }

    private static string? GetModuleId(string relativePath)
    {
        var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 4 &&
            segments[0].Equals("src", StringComparison.OrdinalIgnoreCase) &&
            segments[1].Equals("modules", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Matches("^[a-z][a-z0-9-]*$", segments[2]);
            return segments[2];
        }

        if (segments.Length >= 3 &&
            segments[0].Equals("contracts", StringComparison.OrdinalIgnoreCase) &&
            !segments[1].Equals("platform", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Matches("^[a-z][a-z0-9-]*$", segments[1]);
            return segments[1];
        }

        return null;
    }

    private static bool IsModulePublicContract(string relativePath)
    {
        var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 3 &&
                segments[0].Equals("contracts", StringComparison.OrdinalIgnoreCase) &&
                !segments[1].Equals("platform", StringComparison.OrdinalIgnoreCase) ||
            segments.Length >= 4 &&
                segments[0].Equals("src", StringComparison.OrdinalIgnoreCase) &&
                segments[1].Equals("modules", StringComparison.OrdinalIgnoreCase) &&
                segments[3].Equals("contracts", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPlatformFoundation(string relativePath) =>
        relativePath.StartsWith("contracts/platform/", StringComparison.OrdinalIgnoreCase) ||
        relativePath.StartsWith("src/building-blocks/", StringComparison.OrdinalIgnoreCase);

    private static bool IsWithinRepository(string path)
    {
        var relativePath = Path.GetRelativePath(RepositoryRoot, path);
        return relativePath != ".." && !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static string RelativePath(string path) => Path.GetRelativePath(RepositoryRoot, path).Replace('\\', '/');

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

    [GeneratedRegex("\\b(?:DbContext|DbSet|MigrationBuilder)\\b|\\[(?:Table|Column)\\s*\\(", RegexOptions.CultureInvariant)]
    private static partial Regex PrivatePersistencePattern();

    [GeneratedRegex("IOpenLimsServerModule\\s*\\[\\s*\\]\\s+modules\\s*=\\s*\\[\\s*new\\s+ReceivingModule\\(", RegexOptions.CultureInvariant)]
    private static partial Regex ReceivingModuleManifestPattern();

    [GeneratedRegex("\\b(?:from|into|update|join|references)\\s+([a-z_][a-z0-9_]*)\\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SchemaAccessPattern();

    private sealed record ProductionProject(string RelativePath, IReadOnlyList<string> References);
}
