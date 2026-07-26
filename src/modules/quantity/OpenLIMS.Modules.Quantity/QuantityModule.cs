using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Quantity;

namespace OpenLIMS.Modules.Quantity;

public sealed class QuantityModule(string postgresConnectionString) :
    IOpenLimsApiModule,
    IOpenLimsWorkerModule,
    IOpenLimsMigrationModule
{
    private readonly QuantityPersistenceOptions _options = new(postgresConnectionString);

    public ServerModuleDescriptor Descriptor { get; } = new(
        "quantity",
        QuantityContract.Version,
        "quantity",
        "OpenLIMS.Modules.Quantity");

    public void AddApiServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        AddPersistence(services);
        services.AddHttpContextAccessor();
        services.TryAddScoped<IQuantityAuthorizationPort, HttpClaimsQuantityAuthorizationPort>();
        services.TryAddScoped<IQuantityAccountService, QuantityAccountService>();
        services.TryAddScoped<IQuantityAvailabilityPort, QuantityAvailabilityPort>();
        services.TryAddScoped<QuantityStore>();
        services.TryAddScoped<QuantityAttemptAuditWriter>();
    }

    public void MapApiEndpoints(IEndpointRouteBuilder endpoints) => QuantityEndpoints.Map(endpoints);

    public void AddWorkerServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        AddPersistence(services);
    }

    public Task ApplyMigrationAsync(CancellationToken cancellationToken) =>
        QuantityMigrator.ApplyAsync(_options.ConnectionString, cancellationToken);

    private void AddPersistence(IServiceCollection services)
    {
        services.TryAddSingleton(_options);
        services.TryAddSingleton<QuantityDataSource>();
    }
}

internal sealed record QuantityPersistenceOptions
{
    public QuantityPersistenceOptions(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("QTY.PERSISTENCE_CONFIGURATION_INVALID");
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }
}
