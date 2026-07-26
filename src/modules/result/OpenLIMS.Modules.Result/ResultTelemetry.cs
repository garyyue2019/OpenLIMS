using System.Diagnostics.Metrics;

namespace OpenLIMS.Modules.Result;

internal static class ResultTelemetry
{
    private static readonly Meter Meter = new("OpenLIMS.Result", "1.0.0");
    private static readonly Counter<long> Groups = Meter.CreateCounter<long>("result_group_created_total");
    private static readonly Counter<long> Observations = Meter.CreateCounter<long>("result_observation_total");
    private static readonly Counter<long> Adoptions = Meter.CreateCounter<long>("result_adoption_total");
    private static readonly Counter<long> Gate = Meter.CreateCounter<long>("result_gate_total");
    private static readonly Counter<long> Rejected = Meter.CreateCounter<long>("result_rejected_total");

    public static void RecordGroupCreated() => Groups.Add(1);

    public static void RecordObservation(string kind) =>
        Observations.Add(1, new KeyValuePair<string, object?>("kind", kind));

    public static void RecordAdoption(long adoptionVersion) =>
        Adoptions.Add(1, new KeyValuePair<string, object?>("initial", adoptionVersion == 1));

    public static void RecordGate(string decision) =>
        Gate.Add(1, new KeyValuePair<string, object?>("decision", decision));

    public static void RecordRejected(string reason) =>
        Rejected.Add(1, new KeyValuePair<string, object?>("reason", reason));
}
