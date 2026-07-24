using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OpenLIMS.Modules.Receiving;

internal sealed class ReceivingOutboxMonitor(
    ReceivingDataSource dataSource,
    ILogger<ReceivingOutboxMonitor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var command = dataSource.DataSource.CreateCommand("""
                    select count(*)
                    from receiving.outbox
                    where dispatched_at is null
                    """);
                var pending = Convert.ToInt64(await command.ExecuteScalarAsync(stoppingToken));
                if (pending > 0)
                {
                    logger.LogWarning("Receiving outbox has {PendingCount} pending events", pending);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (exception is Npgsql.NpgsqlException or InvalidOperationException)
            {
                logger.LogError("Receiving outbox backlog probe failed with {ErrorCategory}", "REC.OUTBOX_PROBE_FAILED");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
