using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Platform;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

var organizationGroupId = builder.Configuration["Platform:OrganizationGroupId"];
var postgresConnectionString = builder.Configuration["Platform:PostgresConnectionString"];
if (string.IsNullOrWhiteSpace(organizationGroupId) || string.IsNullOrWhiteSpace(postgresConnectionString))
{
    throw new InvalidOperationException("PLT.CONFIGURATION_INVALID");
}

if (args.Length == 1 && string.Equals(args[0], "--apply-platform-migration", StringComparison.Ordinal))
{
    await PlatformMigrationRunner.ApplyAsync(postgresConnectionString);
    return;
}

builder.Services.AddSingleton<ICurrentOrganizationContext>(new DeploymentOrganizationContext(new OrganizationScope(organizationGroupId)));
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddHostedService<IdleWorker>();
await builder.Build().RunAsync();

internal sealed class IdleWorker(ILogger<IdleWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Worker started");
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}
