using System.Diagnostics.Metrics;

namespace OpenLIMS.Modules.Quantity;

internal static class QuantityTelemetry
{
    private static readonly Meter Meter = new("OpenLIMS.Quantity", "1.0.0");
    private static readonly Counter<long> Posted = Meter.CreateCounter<long>("quantity_entry_posted_total");
    private static readonly Counter<long> Gate = Meter.CreateCounter<long>("quantity_gate_total");
    private static readonly Counter<long> Rejected = Meter.CreateCounter<long>("quantity_rejected_total");

    public static void RecordPosted(string entryType) =>
        Posted.Add(1, new KeyValuePair<string, object?>("entryType", entryType));

    public static void RecordGate(string decision) =>
        Gate.Add(1, new KeyValuePair<string, object?>("decision", decision));

    public static void RecordRejected(string reason) =>
        Rejected.Add(1, new KeyValuePair<string, object?>("reason", reason));
}
