using System.Diagnostics.Metrics;

namespace OpenLIMS.Modules.Instrument;

internal static class InstrumentTelemetry
{
    private static readonly Meter Meter = new("OpenLIMS.Instrument", "1.0.0");
    private static readonly Counter<long> Registrations = Meter.CreateCounter<long>("instrument_file_registration_total");
    private static readonly Counter<long> Rows = Meter.CreateCounter<long>("instrument_parsed_row_total");
    private static readonly Counter<long> Exceptions = Meter.CreateCounter<long>("instrument_import_exception_total");
    private static readonly Counter<long> Resolutions = Meter.CreateCounter<long>("instrument_exception_resolution_total");
    private static readonly Counter<long> Gate = Meter.CreateCounter<long>("instrument_gate_total");
    private static readonly Counter<long> Rejected = Meter.CreateCounter<long>("instrument_rejected_total");

    public static void RecordRegistration(string sourceSystem) =>
        Registrations.Add(1, new KeyValuePair<string, object?>("sourceSystem", sourceSystem));

    public static void RecordRows(int validCount, int exceptionCount)
    {
        if (validCount > 0)
            Rows.Add(validCount);
        if (exceptionCount > 0)
            Exceptions.Add(exceptionCount);
    }

    public static void RecordResolution(string kind) =>
        Resolutions.Add(1, new KeyValuePair<string, object?>("kind", kind));

    public static void RecordGate(string decision) =>
        Gate.Add(1, new KeyValuePair<string, object?>("decision", decision));

    public static void RecordRejected(string reason) =>
        Rejected.Add(1, new KeyValuePair<string, object?>("reason", reason));
}
