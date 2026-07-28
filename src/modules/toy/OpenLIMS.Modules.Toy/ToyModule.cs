using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Toy;

namespace OpenLIMS.Modules.Toy;

public sealed class ToyModule(string postgresConnectionString) :
    IOpenLimsApiModule,
    IOpenLimsWorkerModule,
    IOpenLimsMigrationModule
{
    private readonly ToyPersistenceOptions _options = new(postgresConnectionString);

    public ServerModuleDescriptor Descriptor { get; } = new(
        "toy",
        ToyContract.Version,
        "toy",
        "OpenLIMS.Modules.Toy");

    public void AddApiServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        AddPersistence(services);
        services.AddHttpContextAccessor();
        services.TryAddScoped<IToyAuthorizationPort, HttpClaimsToyAuthorizationPort>();
        services.TryAddScoped<IToyProductService, ToyProductService>();
        services.TryAddScoped<IToyAgeGradeStatusPort, ToyAgeGradeStatusPort>();
        services.TryAddScoped<IToyTestUnitPlanService, ToyTestUnitPlanService>();
        services.TryAddScoped<IToyTestUnitPlanStatusPort, ToyTestUnitPlanStatusPort>();
        services.TryAddScoped<IToyLabelReviewService, ToyLabelReviewService>();
        services.TryAddScoped<IToyLabelReviewStatusPort, ToyLabelReviewStatusPort>();
        services.TryAddScoped<IToyLabelReviewImpactPort, ToyLabelReviewImpactPort>();
        services.TryAddScoped<IToyConclusionService, ToyConclusionService>();
        services.TryAddScoped<ToyStore>();
        services.TryAddScoped<ToyTestUnitPlanStore>();
        services.TryAddScoped<ToyLabelReviewStore>();
        services.TryAddScoped<ToyConclusionStore>();
        services.TryAddScoped<ToyAttemptAuditWriter>();
    }

    public void MapApiEndpoints(IEndpointRouteBuilder endpoints) => ToyEndpoints.Map(endpoints);

    public void AddWorkerServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        AddPersistence(services);
    }

    public async Task ApplyMigrationAsync(CancellationToken cancellationToken)
    {
        await ToyMigrator.ApplyAsync(_options.ConnectionString, cancellationToken);
        await ToyTestUnitPlanMigrator.ApplyAsync(_options.ConnectionString, cancellationToken);
        await ToyLabelReviewMigrator.ApplyAsync(_options.ConnectionString, cancellationToken);
        await ToyConclusionMigrator.ApplyAsync(_options.ConnectionString, cancellationToken);
    }

    private void AddPersistence(IServiceCollection services)
    {
        services.TryAddSingleton(_options);
        services.TryAddSingleton<ToyDataSource>();
    }
}

internal sealed record ToyPersistenceOptions
{
    public ToyPersistenceOptions(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("TOY.PERSISTENCE_CONFIGURATION_INVALID");
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }
}
