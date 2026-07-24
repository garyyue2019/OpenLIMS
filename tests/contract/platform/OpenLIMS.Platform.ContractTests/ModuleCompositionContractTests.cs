extern alias worker;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OpenLIMS.Api;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Testing.ModuleFixture;
using Xunit;
using WorkerModuleComposition = worker::OpenLIMS.Worker.WorkerModuleComposition;

namespace OpenLIMS.Platform.ContractTests;

[Trait("Profile", "module-onboarding")]
public sealed class ModuleCompositionContractTests
{
    [Fact]
    public async Task Explicit_fixture_catalog_registers_api_service_and_route_once()
    {
        var module = new FixtureServerModule();
        var catalog = OpenLimsModuleCatalog.Create(module);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddOpenLimsModule(catalog);
        await using var app = builder.Build();
        app.MapOpenLimsModuleEndpoints(catalog);

        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();
        using var response = await client.GetAsync(FixtureServerModule.EndpointPath, TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"moduleId\":\"fixture-module\"}", content);
        Assert.Equal(0, module.MigrationApplyCount);
    }

    [Fact]
    public void Explicit_fixture_catalog_registers_worker_service_without_applying_migration()
    {
        var module = new FixtureServerModule();
        var services = new ServiceCollection();

        WorkerModuleComposition.AddOpenLimsWorkerModule(services, OpenLimsModuleCatalog.Create(module));
        using var provider = services.BuildServiceProvider();

        Assert.True(provider.GetRequiredService<FixtureWorkerService>().IsRegistered);
        Assert.Equal(0, module.MigrationApplyCount);
    }

    [Fact]
    public async Task Fixture_migration_runs_once_only_after_an_explicit_exact_module_request()
    {
        var module = new FixtureServerModule();
        var catalog = OpenLimsModuleCatalog.Create(module);
        Assert.Equal(0, module.MigrationApplyCount);

        await OpenLimsModuleMigrationRunner.ApplyAsync(
            catalog,
            "fixture-module",
            TestContext.Current.CancellationToken);

        Assert.Equal(1, module.MigrationApplyCount);
    }

    [Fact]
    public async Task Canceled_fixture_migration_never_invokes_the_module()
    {
        var module = new FixtureServerModule();
        var catalog = OpenLimsModuleCatalog.Create(module);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            OpenLimsModuleMigrationRunner.ApplyAsync(catalog, "fixture-module", cancellation.Token));

        Assert.Equal(0, module.MigrationApplyCount);
    }

    [Fact]
    public void Api_module_registration_cannot_be_invoked_twice()
    {
        var services = new ServiceCollection();
        var catalog = OpenLimsModuleCatalog.Create(new FixtureServerModule());
        services.AddOpenLimsModule(catalog);

        var exception = Assert.Throws<ModuleCompositionException>(() => services.AddOpenLimsModule(catalog));

        Assert.Equal(ModuleCompositionErrorCodes.RegistrationDuplicate, exception.ErrorCode);
    }

    [Fact]
    public void Worker_module_registration_cannot_be_invoked_twice()
    {
        var services = new ServiceCollection();
        var catalog = OpenLimsModuleCatalog.Create(new FixtureServerModule());
        WorkerModuleComposition.AddOpenLimsWorkerModule(services, catalog);

        var exception = Assert.Throws<ModuleCompositionException>(() =>
            WorkerModuleComposition.AddOpenLimsWorkerModule(services, catalog));

        Assert.Equal(ModuleCompositionErrorCodes.RegistrationDuplicate, exception.ErrorCode);
    }

    [Fact]
    public void Duplicate_api_route_fails_during_explicit_mapping()
    {
        var catalog = OpenLimsModuleCatalog.Create(new FixtureServerModule(), new DuplicateRouteFixtureModule());
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddOpenLimsModule(catalog);
        using var app = builder.Build();

        var exception = Assert.Throws<ModuleCompositionException>(() => app.MapOpenLimsModuleEndpoints(catalog));

        Assert.Equal(ModuleCompositionErrorCodes.RouteDuplicate, exception.ErrorCode);
    }

    [Fact]
    public async Task Production_api_catalog_does_not_expose_fixture_route()
    {
        using var factory = new ConfiguredApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(FixtureServerModule.EndpointPath, TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void Production_hosts_do_not_reference_test_fixture_assembly()
    {
        var apiReferences = typeof(Program).Assembly.GetReferencedAssemblies();
        var workerReferences = typeof(WorkerModuleComposition).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(apiReferences, reference => reference.Name == "OpenLIMS.Testing.ModuleFixture");
        Assert.DoesNotContain(workerReferences, reference => reference.Name == "OpenLIMS.Testing.ModuleFixture");
    }
}
