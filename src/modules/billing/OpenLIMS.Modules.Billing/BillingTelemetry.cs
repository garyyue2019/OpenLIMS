using System.Diagnostics.Metrics;

namespace OpenLIMS.Modules.Billing;

internal static class BillingTelemetry
{
    private static readonly Meter Meter = new("OpenLIMS.Billing", "1.0.0");
    private static readonly Counter<long> Evidence = Meter.CreateCounter<long>("billing_evidence_total");
    private static readonly Counter<long> Adjustment = Meter.CreateCounter<long>("billing_adjustment_total");
    private static readonly Counter<long> Gate = Meter.CreateCounter<long>("billing_gate_total");
    private static readonly Counter<long> Rejected = Meter.CreateCounter<long>("billing_rejected_total");

    public static void RecordEvidence(bool zeroAmount) =>
        Evidence.Add(1, new KeyValuePair<string, object?>("zeroAmount", zeroAmount));

    public static void RecordAdjustment(bool positive) =>
        Adjustment.Add(1, new KeyValuePair<string, object?>("positive", positive));

    public static void RecordGate(string decision) =>
        Gate.Add(1, new KeyValuePair<string, object?>("decision", decision));

    public static void RecordRejected(string reason) =>
        Rejected.Add(1, new KeyValuePair<string, object?>("reason", reason));
}
