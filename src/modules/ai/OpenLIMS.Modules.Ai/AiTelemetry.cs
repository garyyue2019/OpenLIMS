using System.Diagnostics.Metrics;

namespace OpenLIMS.Modules.Ai;

internal static class AiTelemetry
{
    private static readonly Meter Meter = new("OpenLIMS.Ai", "1.0.0");
    private static readonly Counter<long> Runs = Meter.CreateCounter<long>("ai_run_total");
    private static readonly Counter<long> Dispositions = Meter.CreateCounter<long>("ai_disposition_total");
    private static readonly Counter<long> QueueReads = Meter.CreateCounter<long>("ai_review_queue_read_total");
    private static readonly Counter<long> Rejected = Meter.CreateCounter<long>("ai_rejected_total");

    public static void RecordRun(string status) =>
        Runs.Add(1, new KeyValuePair<string, object?>("status", status));

    public static void RecordDisposition(string kind) =>
        Dispositions.Add(1, new KeyValuePair<string, object?>("kind", kind));

    public static void RecordQueueRead(int count) => QueueReads.Add(1, new KeyValuePair<string, object?>("count", count));

    public static void RecordRejected(string reason) =>
        Rejected.Add(1, new KeyValuePair<string, object?>("reason", reason));
}
