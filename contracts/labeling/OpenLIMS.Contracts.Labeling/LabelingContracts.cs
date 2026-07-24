using OpenLIMS.Contracts.Receiving;

namespace OpenLIMS.Contracts.Labeling;

public static class LabelingContract
{
    public const string Version = "1.0.0";
    public const string IdempotencyHeader = "Idempotency-Key";
    public const string CreateJobsPath = "/api/v1/label-jobs";
    public const string JobPath = "/api/v1/label-jobs/{printJobId}";
    public const string ReprintPath = "/api/v1/label-jobs/{printJobId}/reprint";
    public const string ResolveScanPath = "/api/v1/scans/resolve";
}

public sealed record LabelPrintTarget(string ObjectType, string ObjectId, long ObjectVersion);

public sealed record CreateLabelJobsRequest(
    string PrinterId,
    IReadOnlyList<LabelPrintTarget> Targets);

public sealed record ReprintLabelRequest(string PrinterId, string Reason);

public sealed record ResolveLabelScanRequest(string BarcodePayload);

public sealed record LabelPrintJobResult(
    string PrintJobId,
    string ObjectType,
    string ObjectId,
    string BusinessNumber,
    string TemplateVersion,
    string PrinterId,
    string Status,
    bool IsReprint,
    int SuccessfulReprintCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateLabelJobsResult(IReadOnlyList<LabelPrintJobResult> Jobs);

public sealed record LabelScanResolution(
    string ObjectType,
    string ObjectId,
    string BusinessNumber,
    string State,
    string PrintVerificationStatus,
    IReadOnlyList<string> AllowedActions);

public sealed record LogicalLabelPrinter(
    string PrinterId,
    string LaboratoryId,
    string DisplayName,
    string Host,
    int Port,
    string Protocol,
    string ConfigurationVersion,
    bool Enabled);

public static class LabelPrintJobStates
{
    public const string Requested = "REQUESTED";
    public const string Dispatching = "DISPATCHING";
    public const string Dispatched = "DISPATCHED";
    public const string Verified = "VERIFIED";
    public const string Failed = "FAILED";
    public const string Unknown = "UNKNOWN";
}

public static class LabelingErrorCodes
{
    public const string BarcodeInvalid = "LABEL.BARCODE_INVALID";
    public const string BarcodeVersionUnsupported = "LABEL.BARCODE_VERSION_UNSUPPORTED";
    public const string ObjectNotAccessible = "LABEL.OBJECT_NOT_ACCESSIBLE";
    public const string ObjectTypeUnsupported = "LABEL.OBJECT_TYPE_UNSUPPORTED";
    public const string PrinterNotConfigured = "LABEL.PRINTER_NOT_CONFIGURED";
    public const string PrinterScopeMismatch = "LABEL.PRINTER_SCOPE_MISMATCH";
    public const string ReprintReasonRequired = "LABEL.REPRINT_REASON_REQUIRED";
    public const string ReprintLimitOverrideRequired = "LABEL.REPRINT_LIMIT_OVERRIDE_REQUIRED";
    public const string DeliveryUnknown = "LABEL.PRINT_DELIVERY_UNKNOWN";
    public const string IdempotencyConflict = "LABEL.IDEMPOTENCY_CONFLICT";
    public const string ValidationFailed = "LABEL.VALIDATION_FAILED";
    public const string PersistenceUnavailable = "LABEL.PERSISTENCE_UNAVAILABLE";
}
