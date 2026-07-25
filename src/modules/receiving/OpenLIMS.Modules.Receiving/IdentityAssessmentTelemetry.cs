using System.Diagnostics.Metrics;

namespace OpenLIMS.Modules.Receiving;

internal static class IdentityAssessmentTelemetry
{
    public const string MeterName = "OpenLIMS.Receiving.IdentityAssessment";
    private static readonly Meter Meter = new(MeterName, "1.0.0");
    private static readonly Counter<long> AssessmentCounter = Meter.CreateCounter<long>("identity_assessment_total");
    private static readonly Counter<long> GateCounter = Meter.CreateCounter<long>("lab_execution_gate_total");

    public static void RecordAssessment(string outcome) =>
        AssessmentCounter.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    public static void RecordGate(string action, string decision, string? assessmentState) =>
        GateCounter.Add(
            1,
            new KeyValuePair<string, object?>("action", action),
            new KeyValuePair<string, object?>("decision", decision),
            new KeyValuePair<string, object?>("assessment_state", assessmentState ?? "UNKNOWN"));
}
