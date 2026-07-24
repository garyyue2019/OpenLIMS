using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenLIMS.Contracts.Platform;

namespace OpenLIMS.Modules.Labeling;

internal interface ILabelPrinterTransport
{
    Task<LabelDispatchOutcome> SendAsync(
        string host,
        int port,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken);
}

internal sealed class TcpLabelPrinterTransport : ILabelPrinterTransport
{
    public async Task<LabelDispatchOutcome> SendAsync(
        string host,
        int port,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await client.ConnectAsync(host, port, linked.Token);
        }
        catch (Exception exception) when (exception is SocketException or IOException or OperationCanceledException)
        {
            if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return LabelDispatchOutcome.DefiniteFailure;
        }

        try
        {
            await client.GetStream().WriteAsync(payload, linked.Token);
            await client.GetStream().FlushAsync(linked.Token);
            return LabelDispatchOutcome.Dispatched;
        }
        catch (Exception exception) when (exception is SocketException or IOException or OperationCanceledException)
        {
            if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return LabelDispatchOutcome.Unknown;
        }
    }
}

internal sealed class LabelPrintDispatcher(
    IServiceScopeFactory scopeFactory,
    ILabelPrinterTransport transport,
    IClock clock,
    ILogger<LabelPrintDispatcher> logger) : BackgroundService
{
    internal async Task<bool> DispatchOneAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<LabelingStore>();
        var job = await store.ClaimNextAsync(clock.UtcNow, cancellationToken);
        if (job is null)
        {
            return false;
        }

        var outcome = await transport.SendAsync(
            job.PrinterHost,
            job.PrinterPort,
            job.RenderedPayload,
            cancellationToken);
        var errorCode = outcome switch
        {
            LabelDispatchOutcome.DefiniteFailure => "LABEL.PRINTER_UNAVAILABLE",
            LabelDispatchOutcome.Unknown => "LABEL.PRINT_DELIVERY_UNKNOWN",
            _ => null
        };
        await store.CompleteDispatchAsync(job, outcome, errorCode, clock.UtcNow, cancellationToken);
        logger.LogInformation(
            "Label print job {PrintJobId} dispatch outcome {Outcome} attempt {AttemptCount}",
            job.Id,
            outcome,
            job.AttemptCount);
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!await DispatchOneAsync(stoppingToken))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken);
            }
        }
    }
}
