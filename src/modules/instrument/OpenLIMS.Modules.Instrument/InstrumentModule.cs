using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Instrument;
using OpenLIMS.Contracts.Platform;

namespace OpenLIMS.Modules.Instrument;

public sealed class InstrumentModule(string postgresConnectionString) :
    IOpenLimsApiModule,
    IOpenLimsWorkerModule,
    IOpenLimsMigrationModule
{
    private readonly InstrumentPersistenceOptions _options = new(postgresConnectionString);

    public ServerModuleDescriptor Descriptor { get; } = new(
        "instrument",
        InstrumentContract.Version,
        "instrument",
        "OpenLIMS.Modules.Instrument");

    public void AddApiServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        AddPersistence(services);
        services.AddHttpContextAccessor();
        services.TryAddScoped<IInstrumentAuthorizationPort, HttpClaimsInstrumentAuthorizationPort>();
        services.TryAddScoped<IInstrumentImportService, InstrumentImportService>();
        services.TryAddScoped<IInstrumentImportPort, InstrumentImportPort>();
        services.TryAddScoped<InstrumentStore>();
        services.TryAddScoped<InstrumentAttemptAuditWriter>();
    }

    public void MapApiEndpoints(IEndpointRouteBuilder endpoints) => InstrumentEndpoints.Map(endpoints);

    public void AddWorkerServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        AddPersistence(services);
    }

    public Task ApplyMigrationAsync(CancellationToken cancellationToken) =>
        InstrumentMigrator.ApplyAsync(_options.ConnectionString, cancellationToken);

    private void AddPersistence(IServiceCollection services)
    {
        services.TryAddSingleton(_options);
        services.TryAddSingleton<InstrumentDataSource>();
    }
}

internal sealed record InstrumentPersistenceOptions
{
    public InstrumentPersistenceOptions(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("INS.PERSISTENCE_CONFIGURATION_INVALID");
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }
}
