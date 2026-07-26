using System.Diagnostics.Metrics;

namespace OpenLIMS.Modules.Scope;

internal static class ScopeTelemetry
{
    private static readonly Meter Meter = new("OpenLIMS.Scope", "1.0.0");
    private static readonly Counter<long> Approved = Meter.CreateCounter<long>("scope_matrix_approved_total");
    private static readonly Counter<long> Gate = Meter.CreateCounter<long>("scope_gate_total");
    private static readonly Counter<long> Rejected = Meter.CreateCounter<long>("scope_rejected_total");

    public static void RecordApproved(string operation) =>
        Approved.Add(1, new KeyValuePair<string, object?>("operation", operation));

    public static void RecordGate(string decision) =>
        Gate.Add(1, new KeyValuePair<string, object?>("decision", decision));

    public static void RecordRejected(string reason) =>
        Rejected.Add(1, new KeyValuePair<string, object?>("reason", reason));
}
