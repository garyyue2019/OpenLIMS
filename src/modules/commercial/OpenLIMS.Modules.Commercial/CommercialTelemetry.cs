using System.Diagnostics.Metrics;

namespace OpenLIMS.Modules.Commercial;

internal static class CommercialTelemetry
{
    private static readonly Meter Meter = new("OpenLIMS.Commercial", "1.0.0");
    private static readonly Counter<long> Catalog = Meter.CreateCounter<long>("commercial_catalog_versions_total");
    private static readonly Counter<long> Inquiry = Meter.CreateCounter<long>("commercial_inquiry_versions_total");
    private static readonly Counter<long> Rejected = Meter.CreateCounter<long>("commercial_rejected_total");

    public static void RecordCatalog(string operation) =>
        Catalog.Add(1, new KeyValuePair<string, object?>("operation", operation));

    public static void RecordInquiry(string operation) =>
        Inquiry.Add(1, new KeyValuePair<string, object?>("operation", operation));

    public static void RecordRejected(string reason) =>
        Rejected.Add(1, new KeyValuePair<string, object?>("reason", reason));
}
