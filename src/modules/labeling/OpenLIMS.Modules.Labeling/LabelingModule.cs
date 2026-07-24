using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Labeling;
using OpenLIMS.Contracts.Platform;

namespace OpenLIMS.Modules.Labeling;

public sealed class LabelingModule(
    string postgresConnectionString,
    IEnumerable<LogicalLabelPrinter> printers) :
    IOpenLimsApiModule,
    IOpenLimsWorkerModule,
    IOpenLimsMigrationModule
{
    private readonly LabelingPersistenceOptions _options = new(postgresConnectionString);
    private readonly LogicalLabelPrinter[] _printers = printers?.ToArray()
        ?? throw new ArgumentNullException(nameof(printers));

    public ServerModuleDescriptor Descriptor { get; } = new(
        "labeling",
        LabelingContract.Version,
        "labeling",
        "OpenLIMS.Modules.Labeling");

    public void AddApiServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        AddPersistence(services);
        services.AddHttpContextAccessor();
        services.TryAddSingleton(new LabelPrinterRegistry(_printers));
        services.TryAddScoped<ILabelingAuthorization, HttpClaimsLabelingAuthorization>();
        services.TryAddScoped<ILabelingService, LabelingService>();
        services.TryAddScoped<LabelingStore>();
    }

    public void MapApiEndpoints(IEndpointRouteBuilder endpoints) => LabelingEndpoints.Map(endpoints);

    public void AddWorkerServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        AddPersistence(services);
        services.TryAddSingleton(new LabelPrinterRegistry(_printers));
        services.TryAddScoped<LabelingStore>();
        services.TryAddSingleton<ILabelPrinterTransport, TcpLabelPrinterTransport>();
        services.AddHostedService<LabelPrintDispatcher>();
    }

    public Task ApplyMigrationAsync(CancellationToken cancellationToken) =>
        LabelingMigrator.ApplyAsync(_options.ConnectionString, cancellationToken);

    private void AddPersistence(IServiceCollection services)
    {
        services.TryAddSingleton(_options);
        services.TryAddSingleton<LabelingDataSource>();
    }
}

internal sealed record LabelingPersistenceOptions
{
    public LabelingPersistenceOptions(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("LABEL.PERSISTENCE_CONFIGURATION_INVALID");
        }

        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }
}
