using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Ai;
using OpenLIMS.Contracts.Platform;

namespace OpenLIMS.Modules.Ai;

public sealed class AiModule(string postgresConnectionString) :
    IOpenLimsApiModule,
    IOpenLimsWorkerModule,
    IOpenLimsMigrationModule
{
    private readonly AiPersistenceOptions _options = new(postgresConnectionString);

    public ServerModuleDescriptor Descriptor { get; } = new(
        "ai", AiContract.Version, "ai", "OpenLIMS.Modules.Ai");

    public void AddApiServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        AddPersistence(services);
        services.AddHttpContextAccessor();
        services.TryAddSingleton<IAiOutputValidator>(AiGovernanceRules.Instance);
        services.TryAddScoped<IAiAuthorizationPort, HttpClaimsAiAuthorizationPort>();
        services.TryAddScoped<IAiProviderPort, DisabledAiProviderPort>();
        services.TryAddScoped<IAiRunService, AiRunService>();
        services.TryAddScoped<AiStore>();
        services.TryAddScoped<AiAttemptAuditWriter>();
    }

    public void MapApiEndpoints(IEndpointRouteBuilder endpoints) => AiEndpoints.Map(endpoints);

    public void AddWorkerServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        AddPersistence(services);
    }

    public Task ApplyMigrationAsync(CancellationToken cancellationToken) =>
        AiMigrator.ApplyAsync(_options.ConnectionString, cancellationToken);

    private void AddPersistence(IServiceCollection services)
    {
        services.TryAddSingleton(_options);
        services.TryAddSingleton<AiDataSource>();
    }
}

internal sealed record AiPersistenceOptions
{
    public AiPersistenceOptions(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("AIX.PERSISTENCE_CONFIGURATION_INVALID");
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }
}
