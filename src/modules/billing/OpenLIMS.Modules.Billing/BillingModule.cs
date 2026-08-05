using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Billing;
using OpenLIMS.Contracts.Platform;

namespace OpenLIMS.Modules.Billing;

public sealed class BillingModule(string postgresConnectionString) :
    IOpenLimsApiModule,
    IOpenLimsWorkerModule,
    IOpenLimsMigrationModule
{
    private readonly BillingPersistenceOptions _options = new(postgresConnectionString);

    public ServerModuleDescriptor Descriptor { get; } = new(
        "billing",
        BillingContract.Version,
        "billing",
        "OpenLIMS.Modules.Billing");

    public void AddApiServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        AddPersistence(services);
        services.AddHttpContextAccessor();
        services.TryAddScoped<IBillingAuthorizationPort, HttpClaimsBillingAuthorizationPort>();
        services.TryAddScoped<IBillingEvidenceService, BillingEvidenceService>();
        services.TryAddScoped<IBillingEvidencePort, BillingEvidencePort>();
        services.TryAddScoped<IBillingIntegrationService, BillingIntegrationService>();
        services.TryAddScoped<BillingStore>();
        services.TryAddScoped<BillingIntegrationStore>();
        services.TryAddScoped<BillingAttemptAuditWriter>();
    }

    public void MapApiEndpoints(IEndpointRouteBuilder endpoints) => BillingEndpoints.Map(endpoints);

    public void AddWorkerServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        AddPersistence(services);
    }

    public async Task ApplyMigrationAsync(CancellationToken cancellationToken)
    {
        await BillingMigrator.ApplyAsync(_options.ConnectionString, cancellationToken);
        await BillingIntegrationMigrator.ApplyAsync(_options.ConnectionString, cancellationToken);
    }

    private void AddPersistence(IServiceCollection services)
    {
        services.TryAddSingleton(_options);
        services.TryAddSingleton<BillingDataSource>();
    }
}

internal sealed record BillingPersistenceOptions
{
    public BillingPersistenceOptions(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("BIL.PERSISTENCE_CONFIGURATION_INVALID");
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }
}
