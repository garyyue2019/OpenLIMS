using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Textile;

namespace OpenLIMS.Modules.Textile;

public sealed class TextileModule(string postgresConnectionString) :
    IOpenLimsApiModule,
    IOpenLimsWorkerModule,
    IOpenLimsMigrationModule
{
    private readonly TextilePersistenceOptions _options = new(postgresConnectionString);

    public ServerModuleDescriptor Descriptor { get; } = new(
        "textile",
        TextileRuntimeContract.Version,
        "textile",
        "OpenLIMS.Modules.Textile");

    public void AddApiServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        AddPersistence(services);
        services.AddHttpContextAccessor();
        services.TryAddScoped<ITextileAuthorizationPort, HttpClaimsTextileAuthorizationPort>();
        services.TryAddScoped<ITextileRuntimeService, TextileRuntimeService>();
        services.TryAddScoped<ITextileCuttingPlanStatusPort, TextileCuttingPlanStatusPort>();
        services.TryAddScoped<TextileStore>();
        services.TryAddScoped<TextileAttemptAuditWriter>();
    }

    public void MapApiEndpoints(IEndpointRouteBuilder endpoints) => TextileEndpoints.Map(endpoints);

    public void AddWorkerServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        AddPersistence(services);
    }

    public Task ApplyMigrationAsync(CancellationToken cancellationToken) =>
        TextileMigrator.ApplyAsync(_options.ConnectionString, cancellationToken);

    private void AddPersistence(IServiceCollection services)
    {
        services.TryAddSingleton(_options);
        services.TryAddSingleton<TextileDataSource>();
    }
}

internal sealed record TextilePersistenceOptions
{
    public TextilePersistenceOptions(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("TEX.PERSISTENCE_CONFIGURATION_INVALID");
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }
}
