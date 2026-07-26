using System.Diagnostics.Metrics;

namespace OpenLIMS.Modules.Report;

internal static class ReportTelemetry
{
    private static readonly Meter Meter = new("OpenLIMS.Report", "1.0.0");
    private static readonly Counter<long> Reports = Meter.CreateCounter<long>("report_total");
    private static readonly Counter<long> Lines = Meter.CreateCounter<long>("report_line_total");
    private static readonly Counter<long> Gates = Meter.CreateCounter<long>("report_gate_total");
    private static readonly Counter<long> Blockers = Meter.CreateCounter<long>("report_blocker_total");
    private static readonly Counter<long> Sources = Meter.CreateCounter<long>("report_gate_source_total");
    private static readonly Counter<long> Accreditation = Meter.CreateCounter<long>("report_accreditation_total");
    private static readonly Counter<long> Submissions = Meter.CreateCounter<long>("report_submission_total");
    private static readonly Counter<long> IssuanceGate = Meter.CreateCounter<long>("report_issuance_gate_total");
    private static readonly Counter<long> Rejected = Meter.CreateCounter<long>("report_rejected_total");

    public static void RecordReport() => Reports.Add(1);

    public static void RecordLine() => Lines.Add(1);

    public static void RecordGate(string decision, int blockerCount)
    {
        Gates.Add(1, new KeyValuePair<string, object?>("decision", decision));
        if (blockerCount > 0)
            Blockers.Add(blockerCount);
    }

    public static void RecordSource(string source, string decision) =>
        Sources.Add(1,
            new KeyValuePair<string, object?>("source", source),
            new KeyValuePair<string, object?>("decision", decision));

    public static void RecordAccreditation(string status) =>
        Accreditation.Add(1, new KeyValuePair<string, object?>("status", status));

    public static void RecordSubmission() => Submissions.Add(1);

    public static void RecordIssuanceGate(string decision) =>
        IssuanceGate.Add(1, new KeyValuePair<string, object?>("decision", decision));

    public static void RecordRejected(string reason) =>
        Rejected.Add(1, new KeyValuePair<string, object?>("reason", reason));
}
