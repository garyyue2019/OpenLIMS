using System.Diagnostics.Metrics;

namespace OpenLIMS.Modules.Qc;

internal static class QcTelemetry
{
    private static readonly Meter Meter = new("OpenLIMS.Qc", "1.0.0");
    private static readonly Counter<long> Runs = Meter.CreateCounter<long>("qc_run_total");
    private static readonly Counter<long> Results = Meter.CreateCounter<long>("qc_result_total");
    private static readonly Counter<long> Verdicts = Meter.CreateCounter<long>("qc_verdict_total");
    private static readonly Counter<long> ImpactTargets = Meter.CreateCounter<long>("qc_impact_target_total");
    private static readonly Counter<long> DeviationApprovals = Meter.CreateCounter<long>("qc_deviation_approval_total");
    private static readonly Counter<long> Gates = Meter.CreateCounter<long>("qc_release_gate_total");
    private static readonly Counter<long> Releases = Meter.CreateCounter<long>("qc_release_total");
    private static readonly Counter<long> BatchGate = Meter.CreateCounter<long>("qc_batch_gate_total");
    private static readonly Counter<long> Reportability = Meter.CreateCounter<long>("qc_reportability_total");
    private static readonly Counter<long> Rejected = Meter.CreateCounter<long>("qc_rejected_total");

    public static void RecordRun() => Runs.Add(1);

    public static void RecordResult(string verdict) =>
        Results.Add(1, new KeyValuePair<string, object?>("verdict", verdict));

    public static void RecordVerdict(string state) =>
        Verdicts.Add(1, new KeyValuePair<string, object?>("state", state));

    public static void RecordImpact(int targetCount) => ImpactTargets.Add(targetCount);

    public static void RecordDeviationApproval() => DeviationApprovals.Add(1);

    public static void RecordGateSatisfied(string kind) =>
        Gates.Add(1, new KeyValuePair<string, object?>("kind", kind));

    public static void RecordRelease() => Releases.Add(1);

    public static void RecordGate(string decision) =>
        BatchGate.Add(1, new KeyValuePair<string, object?>("decision", decision));

    public static void RecordReportability(string decision) =>
        Reportability.Add(1, new KeyValuePair<string, object?>("decision", decision));

    public static void RecordRejected(string reason) =>
        Rejected.Add(1, new KeyValuePair<string, object?>("reason", reason));
}
