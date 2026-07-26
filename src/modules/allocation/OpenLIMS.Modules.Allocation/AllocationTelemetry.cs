using System.Diagnostics.Metrics;

namespace OpenLIMS.Modules.Allocation;

internal static class AllocationTelemetry
{
    private static readonly Meter Meter = new("OpenLIMS.Allocation", "1.0.0");
    private static readonly Counter<long> Assigned = Meter.CreateCounter<long>("allocation_assigned_total");
    private static readonly Counter<long> Gate = Meter.CreateCounter<long>("allocation_gate_total");
    private static readonly Counter<long> Rejected = Meter.CreateCounter<long>("allocation_rejected_total");

    public static void RecordAssigned(bool destructive) =>
        Assigned.Add(1, new KeyValuePair<string, object?>("destructive", destructive));

    public static void RecordGate(string source, string decision) =>
        Gate.Add(
            1,
            new KeyValuePair<string, object?>("source", source),
            new KeyValuePair<string, object?>("decision", decision));

    public static void RecordRejected(string reason) =>
        Rejected.Add(1, new KeyValuePair<string, object?>("reason", reason));
}
