using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Platform;
using Xunit;

namespace OpenLIMS.Platform.UnitTests;

[Trait("Profile", "module-onboarding")]
public sealed class ModuleCatalogTests
{
    [Fact]
    public void Valid_descriptors_are_preserved_in_explicit_order()
    {
        var first = Module("first-module", "first_module");
        var second = Module("second-module", "second_module");

        var catalog = OpenLimsModuleCatalog.Create(first, second);

        Assert.Collection(catalog.Modules, item => Assert.Same(first, item), item => Assert.Same(second, item));
    }

    [Fact]
    public void Duplicate_module_id_fails_closed()
    {
        var exception = Assert.Throws<ModuleCompositionException>(() =>
            OpenLimsModuleCatalog.Create(Module("same-module", "schema_one"), Module("same-module", "schema_two")));

        Assert.Equal(ModuleCompositionErrorCodes.ModuleIdDuplicate, exception.ErrorCode);
    }

    [Fact]
    public void Duplicate_schema_name_fails_closed()
    {
        var exception = Assert.Throws<ModuleCompositionException>(() =>
            OpenLimsModuleCatalog.Create(Module("module-one", "same_schema"), Module("module-two", "same_schema")));

        Assert.Equal(ModuleCompositionErrorCodes.SchemaNameDuplicate, exception.ErrorCode);
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("1.x")]
    [InlineData("^1.0.0")]
    [InlineData("1.0")]
    [InlineData("2.0.0")]
    public void Non_exact_or_unsupported_contract_version_fails_closed(string version)
    {
        var exception = Assert.Throws<ModuleCompositionException>(() =>
            OpenLimsModuleCatalog.Create(Module("valid-module", "valid_module", version)));

        Assert.Equal(ModuleCompositionErrorCodes.ContractVersionUnsupported, exception.ErrorCode);
    }

    [Theory]
    [InlineData("Invalid_Module", "valid_schema", "Valid.Migrations")]
    [InlineData("-invalid", "valid_schema", "Valid.Migrations")]
    [InlineData("valid-module", "Invalid-Schema", "Valid.Migrations")]
    [InlineData("valid-module", "platform", "Valid.Migrations")]
    [InlineData("valid-module", "information_schema", "Valid.Migrations")]
    [InlineData("valid-module", "pg_catalog", "Valid.Migrations")]
    [InlineData("valid-module", "valid_schema", "invalid assembly")]
    [InlineData("valid-module", "valid_schema", "Invalid..Assembly")]
    public void Invalid_identifiers_or_reserved_schema_fail_closed(
        string moduleId,
        string schemaName,
        string migrationAssembly)
    {
        var exception = Assert.Throws<ModuleCompositionException>(() =>
            OpenLimsModuleCatalog.Create(new TestModule(new ServerModuleDescriptor(
                moduleId,
                OpenLimsModuleCatalog.CurrentContractVersion,
                schemaName,
                migrationAssembly))));

        Assert.Equal(ModuleCompositionErrorCodes.DescriptorInvalid, exception.ErrorCode);
    }

    [Fact]
    public void Catalog_is_an_immutable_snapshot_of_the_supplied_array()
    {
        var first = Module("first-module", "first_module");
        IOpenLimsServerModule[] supplied = [first];
        var catalog = OpenLimsModuleCatalog.Create(supplied);

        supplied[0] = Module("replacement-module", "replacement_module");

        Assert.Same(first, Assert.Single(catalog.Modules));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Migration_requires_a_non_empty_explicit_module_id(string moduleId)
    {
        var exception = await Assert.ThrowsAsync<ModuleCompositionException>(() =>
            OpenLimsModuleMigrationRunner.ApplyAsync(
                OpenLimsModuleCatalog.Empty,
                moduleId,
                TestContext.Current.CancellationToken));

        Assert.Equal(ModuleCompositionErrorCodes.MigrationModuleIdInvalid, exception.ErrorCode);
    }

    [Theory]
    [InlineData("unknown-module")]
    [InlineData("VALID-MODULE")]
    public async Task Migration_requires_an_exact_known_module_id(string moduleId)
    {
        var catalog = OpenLimsModuleCatalog.Create(Module("valid-module", "valid_module"));

        var exception = await Assert.ThrowsAsync<ModuleCompositionException>(() =>
            OpenLimsModuleMigrationRunner.ApplyAsync(catalog, moduleId, TestContext.Current.CancellationToken));

        Assert.Equal(ModuleCompositionErrorCodes.MigrationModuleUnknown, exception.ErrorCode);
    }

    [Fact]
    public async Task Migration_rejects_a_known_module_without_the_migration_capability()
    {
        var catalog = OpenLimsModuleCatalog.Create(Module("valid-module", "valid_module"));

        var exception = await Assert.ThrowsAsync<ModuleCompositionException>(() =>
            OpenLimsModuleMigrationRunner.ApplyAsync(
                catalog,
                "valid-module",
                TestContext.Current.CancellationToken));

        Assert.Equal(ModuleCompositionErrorCodes.MigrationUnsupported, exception.ErrorCode);
    }

    private static TestModule Module(
        string moduleId,
        string schemaName,
        string version = OpenLimsModuleCatalog.CurrentContractVersion) =>
        new(new ServerModuleDescriptor(moduleId, version, schemaName, $"OpenLIMS.Modules.{moduleId.Replace('-', '.')}.Migrations"));

    private sealed class TestModule(ServerModuleDescriptor descriptor) : IOpenLimsServerModule
    {
        public ServerModuleDescriptor Descriptor { get; } = descriptor;
    }
}
