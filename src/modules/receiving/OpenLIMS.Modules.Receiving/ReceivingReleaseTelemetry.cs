using System.Diagnostics.Metrics;

namespace OpenLIMS.Modules.Receiving;

internal static class ReceivingReleaseTelemetry
{
    public const string MeterName = "OpenLIMS.Receiving.Release";
    private static readonly Meter Meter = new(MeterName, "2.0.0");
    private static readonly Counter<long> Releases = Meter.CreateCounter<long>("receipt_release_total");
    private static readonly Counter<long> Blocked = Meter.CreateCounter<long>("receipt_release_blocked_total");
    private static readonly Counter<long> Gates = Meter.CreateCounter<long>("lab_execution_gate_total");

    public static void RecordRelease(string outcome, string result)
    {
        if (string.Equals(outcome, "BLOCKED", StringComparison.Ordinal))
        {
            Blocked.Add(1, new KeyValuePair<string, object?>("reason", result));
            return;
        }

        Releases.Add(1, new KeyValuePair<string, object?>("outcome", outcome), new("result", result));
    }

    public static void RecordGate(string action, string decision, string? state) =>
        Gates.Add(
            1,
            new KeyValuePair<string, object?>("action", action),
            new KeyValuePair<string, object?>("decision", decision),
            new KeyValuePair<string, object?>("state", state ?? "UNKNOWN"));
}
