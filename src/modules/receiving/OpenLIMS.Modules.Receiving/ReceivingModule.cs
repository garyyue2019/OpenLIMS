using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Receiving;

namespace OpenLIMS.Modules.Receiving;

public sealed class ReceivingModule(string postgresConnectionString) :
    IOpenLimsApiModule,
    IOpenLimsWorkerModule,
    IOpenLimsMigrationModule
{
    private readonly ReceivingPersistenceOptions _options = new(postgresConnectionString);

    public ServerModuleDescriptor Descriptor { get; } = new(
        "receiving",
        ReceivingContract.Version,
        "receiving",
        "OpenLIMS.Modules.Receiving");

    public void AddApiServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        AddPersistence(services);
        services.AddHttpContextAccessor();
        services.TryAddScoped<IReceivingAuthorizationPort, HttpClaimsReceivingAuthorizationPort>();
        services.TryAddScoped<IReceiptRegistrationService, ReceiptRegistrationService>();
        services.TryAddScoped<ReceivingRegistrationStore>();
        services.TryAddScoped<ReceivingAttemptAuditWriter>();
    }

    public void MapApiEndpoints(IEndpointRouteBuilder endpoints) => ReceivingEndpoints.Map(endpoints);

    public void AddWorkerServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        AddPersistence(services);
        services.AddHostedService<ReceivingOutboxMonitor>();
    }

    public Task ApplyMigrationAsync(CancellationToken cancellationToken) =>
        ReceivingMigrator.ApplyAsync(_options.ConnectionString, cancellationToken);

    private void AddPersistence(IServiceCollection services)
    {
        services.TryAddSingleton(_options);
        services.TryAddSingleton<ReceivingDataSource>();
    }
}

internal sealed record ReceivingPersistenceOptions
{
    public ReceivingPersistenceOptions(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("REC.PERSISTENCE_CONFIGURATION_INVALID");
        }

        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }
}
