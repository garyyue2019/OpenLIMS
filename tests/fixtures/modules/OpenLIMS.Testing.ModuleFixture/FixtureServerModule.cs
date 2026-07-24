using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Platform;

namespace OpenLIMS.Testing.ModuleFixture;

public sealed class FixtureServerModule : IOpenLimsApiModule, IOpenLimsWorkerModule, IOpenLimsMigrationModule
{
    public const string EndpointPath = "/__fixtures/module-onboarding";

    public ServerModuleDescriptor Descriptor { get; } = new(
        "fixture-module",
        OpenLimsModuleCatalog.CurrentContractVersion,
        "fixture_module",
        "OpenLIMS.Testing.ModuleFixture.Migrations");

    public int MigrationApplyCount { get; private set; }

    public void AddApiServices(IServiceCollection services) => services.AddSingleton<FixtureApiService>();

    public void MapApiEndpoints(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet(EndpointPath, (FixtureApiService service) => new { moduleId = service.ModuleId });

    public void AddWorkerServices(IServiceCollection services) => services.AddSingleton<FixtureWorkerService>();

    public Task ApplyMigrationAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MigrationApplyCount++;
        return Task.CompletedTask;
    }
}

public sealed class DuplicateRouteFixtureModule : IOpenLimsApiModule
{
    public ServerModuleDescriptor Descriptor { get; } = new(
        "duplicate-route-fixture",
        OpenLimsModuleCatalog.CurrentContractVersion,
        "duplicate_route_fixture",
        "OpenLIMS.Testing.DuplicateRouteFixture.Migrations");

    public void AddApiServices(IServiceCollection services)
    {
    }

    public void MapApiEndpoints(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet(FixtureServerModule.EndpointPath, () => Results.NoContent());
}

public sealed class FixtureApiService
{
    public string ModuleId => "fixture-module";
}

public sealed class FixtureWorkerService
{
    public bool IsRegistered => true;
}
