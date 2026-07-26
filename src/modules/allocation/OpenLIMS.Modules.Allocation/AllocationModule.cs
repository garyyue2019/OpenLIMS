using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Allocation;
using OpenLIMS.Contracts.Platform;

namespace OpenLIMS.Modules.Allocation;

public sealed class AllocationModule(string postgresConnectionString) :
    IOpenLimsApiModule,
    IOpenLimsWorkerModule,
    IOpenLimsMigrationModule
{
    private readonly AllocationPersistenceOptions _options = new(postgresConnectionString);

    public ServerModuleDescriptor Descriptor { get; } = new(
        "allocation",
        AllocationContract.Version,
        "allocation",
        "OpenLIMS.Modules.Allocation");

    public void AddApiServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        AddPersistence(services);
        services.AddHttpContextAccessor();
        services.TryAddScoped<IAllocationAuthorizationPort, HttpClaimsAllocationAuthorizationPort>();
        services.TryAddScoped<ITestObjectAllocationService, TestObjectAllocationService>();
        services.TryAddScoped<IAllocationStatusPort, AllocationStatusPort>();
        services.TryAddScoped<AllocationStore>();
        services.TryAddScoped<AllocationAttemptAuditWriter>();
    }

    public void MapApiEndpoints(IEndpointRouteBuilder endpoints) => AllocationEndpoints.Map(endpoints);

    public void AddWorkerServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        AddPersistence(services);
    }

    public Task ApplyMigrationAsync(CancellationToken cancellationToken) =>
        AllocationMigrator.ApplyAsync(_options.ConnectionString, cancellationToken);

    private void AddPersistence(IServiceCollection services)
    {
        services.TryAddSingleton(_options);
        services.TryAddSingleton<AllocationDataSource>();
    }
}

internal sealed record AllocationPersistenceOptions
{
    public AllocationPersistenceOptions(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ALC.PERSISTENCE_CONFIGURATION_INVALID");
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }
}
