using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using OpenLIMS.Contracts.Platform;

namespace OpenLIMS.BuildingBlocks.Platform;

public interface IOpenLimsServerModule
{
    ServerModuleDescriptor Descriptor { get; }
}

public interface IOpenLimsApiModule : IOpenLimsServerModule
{
    void AddApiServices(IServiceCollection services);

    void MapApiEndpoints(IEndpointRouteBuilder endpoints);
}

public interface IOpenLimsWorkerModule : IOpenLimsServerModule
{
    void AddWorkerServices(IServiceCollection services);
}

public interface IOpenLimsMigrationModule : IOpenLimsServerModule
{
    Task ApplyMigrationAsync(CancellationToken cancellationToken);
}

public sealed class OpenLimsModuleCatalog
{
    public const string CurrentContractVersion = "1.0.0";

    private static readonly Regex ModuleIdPattern = new(
        "^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static readonly Regex SchemaNamePattern = new(
        "^[a-z][a-z0-9_]*$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static readonly Regex AssemblyNamePattern = new(
        "^[A-Za-z][A-Za-z0-9_]*(?:\\.[A-Za-z][A-Za-z0-9_]*)*$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private OpenLimsModuleCatalog(IReadOnlyList<IOpenLimsServerModule> modules) => Modules = modules;

    public static OpenLimsModuleCatalog Empty { get; } =
        new(new ReadOnlyCollection<IOpenLimsServerModule>([]));

    public IReadOnlyList<IOpenLimsServerModule> Modules { get; }

    public static OpenLimsModuleCatalog Create(params IOpenLimsServerModule[] modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        var validated = new List<IOpenLimsServerModule>(modules.Length);
        var moduleIds = new HashSet<string>(StringComparer.Ordinal);
        var schemaNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var module in modules)
        {
            if (module is null)
            {
                throw new ModuleCompositionException(ModuleCompositionErrorCodes.DescriptorInvalid);
            }

            var descriptor = module.Descriptor;
            ValidateDescriptor(descriptor);

            if (!moduleIds.Add(descriptor.ModuleId))
            {
                throw new ModuleCompositionException(ModuleCompositionErrorCodes.ModuleIdDuplicate);
            }

            if (!schemaNames.Add(descriptor.SchemaName))
            {
                throw new ModuleCompositionException(ModuleCompositionErrorCodes.SchemaNameDuplicate);
            }

            validated.Add(module);
        }

        return validated.Count == 0
            ? Empty
            : new OpenLimsModuleCatalog(new ReadOnlyCollection<IOpenLimsServerModule>(validated));
    }

    private static void ValidateDescriptor(ServerModuleDescriptor? descriptor)
    {
        if (descriptor is null ||
            !IsValidIdentifier(descriptor.ModuleId, ModuleIdPattern, 63) ||
            !IsValidIdentifier(descriptor.SchemaName, SchemaNamePattern, 63) ||
            string.Equals(descriptor.SchemaName, "platform", StringComparison.Ordinal) ||
            string.Equals(descriptor.SchemaName, "information_schema", StringComparison.Ordinal) ||
            descriptor.SchemaName.StartsWith("pg_", StringComparison.Ordinal) ||
            !IsValidIdentifier(descriptor.MigrationAssembly, AssemblyNamePattern, 255))
        {
            throw new ModuleCompositionException(ModuleCompositionErrorCodes.DescriptorInvalid);
        }

        if (!string.Equals(descriptor.ContractVersion, CurrentContractVersion, StringComparison.Ordinal))
        {
            throw new ModuleCompositionException(ModuleCompositionErrorCodes.ContractVersionUnsupported);
        }
    }

    private static bool IsValidIdentifier(string? value, Regex pattern, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximumLength &&
        pattern.IsMatch(value);
}

public sealed class ModuleCompositionException(string errorCode) : InvalidOperationException(errorCode)
{
    public string ErrorCode { get; } = errorCode;
}

public static class ModuleCompositionErrorCodes
{
    public const string DescriptorInvalid = "PLT.MODULE_DESCRIPTOR_INVALID";
    public const string ContractVersionUnsupported = "PLT.MODULE_CONTRACT_VERSION_UNSUPPORTED";
    public const string ModuleIdDuplicate = "PLT.MODULE_ID_DUPLICATE";
    public const string SchemaNameDuplicate = "PLT.MODULE_SCHEMA_NAME_DUPLICATE";
    public const string RegistrationDuplicate = "PLT.MODULE_REGISTRATION_DUPLICATE";
    public const string RouteDuplicate = "PLT.MODULE_ROUTE_DUPLICATE";
    public const string MigrationModuleIdInvalid = "PLT.MODULE_MIGRATION_ID_INVALID";
    public const string MigrationModuleUnknown = "PLT.MODULE_MIGRATION_UNKNOWN";
    public const string MigrationUnsupported = "PLT.MODULE_MIGRATION_UNSUPPORTED";
}

public static class OpenLimsModuleMigrationRunner
{
    public static async Task ApplyAsync(
        OpenLimsModuleCatalog catalog,
        string moduleId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(moduleId))
        {
            throw new ModuleCompositionException(ModuleCompositionErrorCodes.MigrationModuleIdInvalid);
        }

        var module = catalog.Modules.SingleOrDefault(candidate =>
            string.Equals(candidate.Descriptor.ModuleId, moduleId, StringComparison.Ordinal));
        if (module is null)
        {
            throw new ModuleCompositionException(ModuleCompositionErrorCodes.MigrationModuleUnknown);
        }

        if (module is not IOpenLimsMigrationModule migrationModule)
        {
            throw new ModuleCompositionException(ModuleCompositionErrorCodes.MigrationUnsupported);
        }

        await migrationModule.ApplyMigrationAsync(cancellationToken);
    }
}
