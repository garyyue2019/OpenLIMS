using System.Diagnostics.Metrics;

namespace OpenLIMS.Modules.Toy;

internal static class ToyTelemetry
{
    private static readonly Meter Meter = new("OpenLIMS.Toy", "1.0.0");
    private static readonly Counter<long> Declarations = Meter.CreateCounter<long>("toy_age_declaration_total");
    private static readonly Counter<long> Decisions = Meter.CreateCounter<long>("toy_age_grade_decision_total");
    private static readonly Counter<long> Freezes = Meter.CreateCounter<long>("toy_age_grade_freeze_total");
    private static readonly Counter<long> Assessments = Meter.CreateCounter<long>("toy_accessibility_assessment_total");
    private static readonly Counter<long> TriggersRaised = Meter.CreateCounter<long>("toy_reassessment_trigger_total");
    private static readonly Counter<long> TriggersResolved = Meter.CreateCounter<long>("toy_reassessment_resolved_total");
    private static readonly Counter<long> Status = Meter.CreateCounter<long>("toy_age_grade_status_total");
    private static readonly Counter<long> Rejected = Meter.CreateCounter<long>("toy_rejected_total");

    public static void RecordDeclaration() => Declarations.Add(1);

    public static void RecordDecision() => Decisions.Add(1);

    public static void RecordFreeze() => Freezes.Add(1);

    public static void RecordAssessment(string stage) =>
        Assessments.Add(1, new KeyValuePair<string, object?>("stage", stage));

    public static void RecordTriggerRaised(string scope) =>
        TriggersRaised.Add(1, new KeyValuePair<string, object?>("scope", scope));

    public static void RecordTriggerResolved() => TriggersResolved.Add(1);

    public static void RecordStatus(string decision) =>
        Status.Add(1, new KeyValuePair<string, object?>("decision", decision));

    public static void RecordRejected(string reason) =>
        Rejected.Add(1, new KeyValuePair<string, object?>("reason", reason));
}
