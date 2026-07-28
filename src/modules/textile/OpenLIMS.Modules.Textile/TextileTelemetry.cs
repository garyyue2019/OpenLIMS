using System.Diagnostics.Metrics;

namespace OpenLIMS.Modules.Textile;

internal static class TextileTelemetry
{
    private static readonly Meter Meter = new("OpenLIMS.Textile", "1.0.0");
    private static readonly Counter<long> Requirements =
        Meter.CreateCounter<long>("openlims_textile_sample_requirement_total");
    private static readonly Counter<long> Plans =
        Meter.CreateCounter<long>("openlims_textile_cutting_plan_total");
    private static readonly Counter<long> Rejections =
        Meter.CreateCounter<long>("openlims_textile_rejected_total");

    public static void RecordRequirement(string decision) =>
        Requirements.Add(1, new KeyValuePair<string, object?>("decision", decision));

    public static void RecordPlan(string state) =>
        Plans.Add(1, new KeyValuePair<string, object?>("state", state));

    public static void RecordRejected(string errorCode) =>
        Rejections.Add(1, new KeyValuePair<string, object?>("error_code", errorCode));
}
