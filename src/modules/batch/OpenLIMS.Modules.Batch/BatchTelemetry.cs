using System.Diagnostics.Metrics;

namespace OpenLIMS.Modules.Batch;

internal static class BatchTelemetry
{
    private static readonly Meter Meter = new("OpenLIMS.Batch", "1.0.0");
    private static readonly Counter<long> Created = Meter.CreateCounter<long>("batch_created_total");
    private static readonly Counter<long> Member = Meter.CreateCounter<long>("batch_member_total");
    private static readonly Counter<long> Frozen = Meter.CreateCounter<long>("batch_frozen_total");
    private static readonly Counter<long> Gate = Meter.CreateCounter<long>("batch_gate_total");
    private static readonly Counter<long> Rejected = Meter.CreateCounter<long>("batch_rejected_total");

    public static void RecordCreated(string batchType) =>
        Created.Add(1, new KeyValuePair<string, object?>("batchType", batchType));

    public static void RecordMember(string memberType) =>
        Member.Add(1, new KeyValuePair<string, object?>("memberType", memberType));

    public static void RecordFrozen(string cause) =>
        Frozen.Add(1, new KeyValuePair<string, object?>("cause", cause));

    public static void RecordGate(string decision) =>
        Gate.Add(1, new KeyValuePair<string, object?>("decision", decision));

    public static void RecordRejected(string reason) =>
        Rejected.Add(1, new KeyValuePair<string, object?>("reason", reason));
}
