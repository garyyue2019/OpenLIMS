using Microsoft.Extensions.Hosting;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Labeling;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Modules.Labeling;
using OpenLIMS.Modules.Quantity;
using OpenLIMS.Modules.Receiving;
using OpenLIMS.Modules.Scope;
using OpenLIMS.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

var organizationGroupId = builder.Configuration["Platform:OrganizationGroupId"];
var postgresConnectionString = builder.Configuration["Platform:PostgresConnectionString"];
if (string.IsNullOrWhiteSpace(organizationGroupId) || string.IsNullOrWhiteSpace(postgresConnectionString))
{
    throw new InvalidOperationException("PLT.CONFIGURATION_INVALID");
}

var labelPrinters = builder.Configuration.GetSection("Labeling:Printers").Get<LogicalLabelPrinter[]>() ?? [];
IOpenLimsServerModule[] modules =
[
    new ReceivingModule(postgresConnectionString),
    new LabelingModule(postgresConnectionString, labelPrinters),
    new ScopeModule(postgresConnectionString),
    new QuantityModule(postgresConnectionString)
];
var moduleCatalog = OpenLimsModuleCatalog.Create(modules);

if (args.Length == 1 && string.Equals(args[0], "--apply-platform-migration", StringComparison.Ordinal))
{
    await PlatformMigrationRunner.ApplyAsync(postgresConnectionString);
    return;
}

if (args.Length == 2 && string.Equals(args[0], "--apply-module-migration", StringComparison.Ordinal))
{
    await OpenLimsModuleMigrationRunner.ApplyAsync(moduleCatalog, args[1]);
    return;
}

if (args.Length > 0)
{
    throw new InvalidOperationException("PLT.COMMAND_INVALID");
}

builder.Services.AddSingleton<ICurrentOrganizationContext>(new DeploymentOrganizationContext(new OrganizationScope(organizationGroupId)));
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddOpenLimsWorkerModule(moduleCatalog);
builder.Services.AddHostedService<IdleWorker>();
var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("OpenLIMS.ModuleComposition");
foreach (var module in moduleCatalog.Modules)
{
    logger.LogInformation(
        "OpenLIMS module {ModuleId} contract {ContractVersion} registered for Worker host",
        module.Descriptor.ModuleId,
        module.Descriptor.ContractVersion);
}
await host.RunAsync();

internal sealed class IdleWorker(ILogger<IdleWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Worker started");
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}
