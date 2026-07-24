namespace OpenLIMS.Contracts.Platform;

public sealed record ServerModuleDescriptor(
    string ModuleId,
    string ContractVersion,
    string SchemaName,
    string MigrationAssembly);
