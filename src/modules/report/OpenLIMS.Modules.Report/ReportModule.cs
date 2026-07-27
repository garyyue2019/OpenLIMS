using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Report;

namespace OpenLIMS.Modules.Report;

public sealed class ReportModule(string postgresConnectionString) :
    IOpenLimsApiModule,
    IOpenLimsWorkerModule,
    IOpenLimsMigrationModule
{
    private readonly ReportPersistenceOptions _options = new(postgresConnectionString);

    public ServerModuleDescriptor Descriptor { get; } = new(
        "report",
        ReportContract.Version,
        "report",
        "OpenLIMS.Modules.Report");

    public void AddApiServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        AddPersistence(services);
        services.AddHttpContextAccessor();
        services.TryAddScoped<IReportAuthorizationPort, HttpClaimsReportAuthorizationPort>();
        services.TryAddScoped<ISignatoryAuthorityPort, HttpClaimsSignatoryAuthorityPort>();
        services.TryAddScoped<IAccreditationScopePort, UnresolvedAccreditationScopePort>();
        services.TryAddScoped<IReportService, ReportService>();
        services.TryAddScoped<IReportIssuanceGatePort, ReportIssuanceGatePort>();
        services.TryAddScoped<IReportVersionService, ReportVersionService>();
        services.TryAddScoped<IReportVersionChainPort, ReportVersionChainPort>();
        services.TryAddScoped<ReportVersionStore>();
        services.TryAddScoped<ReportStore>();
        services.TryAddScoped<ReportAttemptAuditWriter>();
    }

    public void MapApiEndpoints(IEndpointRouteBuilder endpoints) => ReportEndpoints.Map(endpoints);

    public void AddWorkerServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        AddPersistence(services);
    }

    public async Task ApplyMigrationAsync(CancellationToken cancellationToken)
    {
        await ReportMigrator.ApplyAsync(_options.ConnectionString, cancellationToken);
        await ReportVersionMigrator.ApplyAsync(_options.ConnectionString, cancellationToken);
    }

    private void AddPersistence(IServiceCollection services)
    {
        services.TryAddSingleton(_options);
        services.TryAddSingleton<ReportDataSource>();
    }
}

internal sealed record ReportPersistenceOptions
{
    public ReportPersistenceOptions(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("RPT.PERSISTENCE_CONFIGURATION_INVALID");
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }
}
