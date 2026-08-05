using System.Diagnostics.Metrics;

namespace OpenLIMS.Modules.Operations;

internal static class OperationsTelemetry
{
    private static readonly Meter Meter = new("OpenLIMS.Operations", "1.0.0");
    private static readonly Counter<long> Facts = Meter.CreateCounter<long>("operations_facts_total");
    private static readonly Counter<long> Rejected = Meter.CreateCounter<long>("operations_rejected_total");

    public static void Record(string operation) =>
        Facts.Add(1, new KeyValuePair<string, object?>("operation", operation));

    public static void RecordRejected(string reason) =>
        Rejected.Add(1, new KeyValuePair<string, object?>("reason", reason));
}
