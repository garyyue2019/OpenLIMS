using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Scope;

namespace OpenLIMS.Modules.Scope;

public sealed class ScopeModule(string postgresConnectionString) :
    IOpenLimsApiModule,
    IOpenLimsWorkerModule,
    IOpenLimsMigrationModule
{
    private readonly ScopePersistenceOptions _options = new(postgresConnectionString);

    public ServerModuleDescriptor Descriptor { get; } = new(
        "scope",
        ScopeContract.Version,
        "scope",
        "OpenLIMS.Modules.Scope");

    public void AddApiServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        AddPersistence(services);
        services.AddHttpContextAccessor();
        services.TryAddScoped<IScopeAuthorizationPort, HttpClaimsScopeAuthorizationPort>();
        services.TryAddScoped<IScopeMatrixService, ScopeMatrixService>();
        services.TryAddScoped<IScopeProductionEligibilityPort, ScopeProductionEligibilityPort>();
        services.TryAddScoped<ScopeStore>();
        services.TryAddScoped<ScopeAttemptAuditWriter>();
    }

    public void MapApiEndpoints(IEndpointRouteBuilder endpoints) => ScopeEndpoints.Map(endpoints);

    public void AddWorkerServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        AddPersistence(services);
    }

    public Task ApplyMigrationAsync(CancellationToken cancellationToken) =>
        ScopeMigrator.ApplyAsync(_options.ConnectionString, cancellationToken);

    private void AddPersistence(IServiceCollection services)
    {
        services.TryAddSingleton(_options);
        services.TryAddSingleton<ScopeDataSource>();
    }
}

internal sealed record ScopePersistenceOptions
{
    public ScopePersistenceOptions(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("SCP.PERSISTENCE_CONFIGURATION_INVALID");
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }
}
