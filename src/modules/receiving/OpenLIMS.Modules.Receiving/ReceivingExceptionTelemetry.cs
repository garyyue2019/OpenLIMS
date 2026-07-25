using System.Diagnostics.Metrics;

namespace OpenLIMS.Modules.Receiving;

internal static class ReceivingExceptionTelemetry
{
    public const string MeterName = "OpenLIMS.Receiving.Exception";
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Created = Meter.CreateCounter<long>("receiving_exception_total");
    private static readonly Counter<long> Decisions = Meter.CreateCounter<long>("receiving_exception_decision_total");

    public static void RecordCreated(string type, string severity) =>
        Created.Add(1, new KeyValuePair<string, object?>("type", type), new("severity", severity), new("status", "OPEN"));

    public static void RecordDecision(string decisionType, string result) =>
        Decisions.Add(1, new KeyValuePair<string, object?>("decision_type", decisionType), new("result", result));
}
