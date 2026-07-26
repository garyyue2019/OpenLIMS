using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Qc;

namespace OpenLIMS.Modules.Qc;

public sealed class QcModule(string postgresConnectionString) :
    IOpenLimsApiModule,
    IOpenLimsWorkerModule,
    IOpenLimsMigrationModule
{
    private readonly QcPersistenceOptions _options = new(postgresConnectionString);

    public ServerModuleDescriptor Descriptor { get; } = new(
        "qc",
        QcContract.Version,
        "qc",
        "OpenLIMS.Modules.Qc");

    public void AddApiServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        AddPersistence(services);
        services.AddHttpContextAccessor();
        services.TryAddScoped<IQcAuthorizationPort, HttpClaimsQcAuthorizationPort>();
        services.TryAddScoped<IQcRunService, QcRunService>();
        services.TryAddScoped<IQcReportabilityPort, QcReportabilityPort>();
        services.TryAddScoped<QcStore>();
        services.TryAddScoped<QcAttemptAuditWriter>();
    }

    public void MapApiEndpoints(IEndpointRouteBuilder endpoints) => QcEndpoints.Map(endpoints);

    public void AddWorkerServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        AddPersistence(services);
    }

    public Task ApplyMigrationAsync(CancellationToken cancellationToken) =>
        QcMigrator.ApplyAsync(_options.ConnectionString, cancellationToken);

    private void AddPersistence(IServiceCollection services)
    {
        services.TryAddSingleton(_options);
        services.TryAddSingleton<QcDataSource>();
    }
}

internal sealed record QcPersistenceOptions
{
    public QcPersistenceOptions(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("QC.PERSISTENCE_CONFIGURATION_INVALID");
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }
}
