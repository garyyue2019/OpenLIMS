namespace OpenLIMS.Contracts.Instrument;

public static class InstrumentContract
{
    public const string Version = "1.0.0";
    public const string RuleSetVersion = "INST-IMPORT@1.0.0";
    public const string RegisterFilePath = "/api/v1/instrument-files";
    public const string SubmitRowsPath = "/api/v1/instrument-files/{id}/rows";
    public const string ResolveExceptionPath = "/api/v1/instrument-files/{id}/exceptions/{exceptionId}/resolution";
    public const string GetFilePath = "/api/v1/instrument-files/{id}";
    public const string StatusPath = "/api/v1/instrument-files/{id}/import-status";
}

public static class InstrumentCapabilities
{
    public const string Import = "instrument.import";
}

public static class InstrumentClaimTypes
{
    public const string Capability = "capability";
    public const string LegalEntity = "legal_entity";
    public const string Laboratory = "laboratory";
}

public static class InstrumentSourceSystems
{
    public const string Instrument = "INSTRUMENT";
    public const string Cds = "CDS";
    public const string Middleware = "MIDDLEWARE";
}

public static class InstrumentFileStates
{
    public const string Ingested = "INGESTED";
    public const string Blocked = "BLOCKED";
    public const string Completed = "COMPLETED";
}

public static class InstrumentExceptionReasons
{
    public const string UnknownSample = "UNKNOWN_SAMPLE";
    public const string IllegalUnit = "ILLEGAL_UNIT";
    public const string UnparsableValue = "UNPARSABLE_VALUE";
    public const string DuplicateRow = "DUPLICATE_ROW";
    public const string QualifierConflict = "QUALIFIER_CONFLICT";
}

public static class InstrumentExceptionStates
{
    public const string Pending = "PENDING";
    public const string Resolved = "RESOLVED";
}

public static class InstrumentResolutionKinds
{
    public const string AcceptWithMapping = "ACCEPT_WITH_MAPPING";
    public const string RejectRow = "REJECT_ROW";
}

public static class InstrumentStatusDecisions
{
    public const string Allowed = "ALLOWED";
    public const string Blocked = "BLOCKED";
    public const string Unknown = "UNKNOWN";
}

public static class InstrumentStatusReasons
{
    public const string FileRequired = "FILE_REGISTRATION_REQUIRED";
    public const string PendingExceptions = "PENDING_EXCEPTIONS";
    public const string ImportIncomplete = "IMPORT_INCOMPLETE";
    public const string VersionMismatch = "VERSION_MISMATCH";
    public const string RuleSetVersionUnknown = "RULE_SET_VERSION_UNKNOWN";
    public const string InstrumentUnavailable = "INSTRUMENT_UNAVAILABLE";
}

public static class InstrumentErrorCodes
{
    public const string ValidationFailed = "INS.VALIDATION_FAILED";
    public const string DuplicateFile = "INS.DUPLICATE_FILE";
    public const string ExceptionAlreadyResolved = "INS.EXCEPTION_ALREADY_RESOLVED";
    public const string ExpectedVersionConflict = "INS.EXPECTED_VERSION_CONFLICT";
    public const string NotAuthorized = "INS.NOT_AUTHORIZED";
    public const string ObjectNotAccessible = "INS.OBJECT_NOT_ACCESSIBLE";
    public const string PersistenceUnavailable = "INS.PERSISTENCE_UNAVAILABLE";
}

public sealed record InstrumentVersionedReference(string Id, long Version);

public sealed record InstrumentObjectContext(string LegalEntityId, string LaboratoryId);

public sealed record RegisterInstrumentFileRequest(
    string RuleSetVersion,
    InstrumentObjectContext ObjectScope,
    InstrumentVersionedReference ExternalRef,
    string Sha256,
    string SourceSystem,
    InstrumentVersionedReference InstrumentRef,
    string ParserVersion,
    int DeclaredRowCount);

public sealed record InstrumentRowInput(
    int RowNumber,
    string SampleNumber,
    string BatchPosition,
    string Parameter,
    string Unit,
    string? Qualifier,
    string RawValue,
    string ParsedValue);

public sealed record SubmitInstrumentRowsRequest(
    long ExpectedCurrentVersion,
    string RuleSetVersion,
    IReadOnlyList<InstrumentRowInput> Rows);

public sealed record ResolveImportExceptionRequest(
    long ExpectedCurrentVersion,
    string RuleSetVersion,
    string Kind,
    string Reason,
    InstrumentRowMapping? CorrectedMapping = null);

public sealed record InstrumentRowMapping(
    string SampleNumber,
    string BatchPosition,
    string Parameter,
    string Unit,
    string? Qualifier);

public sealed record InstrumentParsedRowResult(
    string RowId,
    string FileRegistrationId,
    int RowNumber,
    string SampleNumber,
    string BatchPosition,
    string Parameter,
    string Unit,
    string? Qualifier,
    string RawValue,
    string ParsedValue,
    string ParserVersion,
    string RecordedBy,
    DateTimeOffset RecordedAt);

public sealed record InstrumentImportExceptionResult(
    string ExceptionId,
    string FileRegistrationId,
    int RowNumber,
    string ReasonCode,
    string RawContent,
    string State,
    InstrumentExceptionResolutionResult? Resolution);

public sealed record InstrumentExceptionResolutionResult(
    string ResolutionId,
    string ExceptionId,
    string Kind,
    InstrumentRowMapping? CorrectedMapping,
    string Reason,
    string ResolvedBy,
    DateTimeOffset ResolvedAt);

public sealed record InstrumentFileResult(
    string FileRegistrationId,
    long Version,
    string State,
    string RuleSetVersion,
    InstrumentObjectContext ObjectScope,
    InstrumentVersionedReference ExternalRef,
    string Sha256,
    string SourceSystem,
    InstrumentVersionedReference InstrumentRef,
    string ParserVersion,
    int DeclaredRowCount,
    IReadOnlyList<InstrumentParsedRowResult> Rows,
    IReadOnlyList<InstrumentImportExceptionResult> Exceptions,
    string RegisteredBy,
    DateTimeOffset RegisteredAt);

public sealed record InstrumentImportStatusRequest(
    string OrganizationGroupId,
    string FileRegistrationId,
    long ExpectedFileVersion,
    string RuleSetVersion)
{
    public string? CorrelationId { get; init; }
}

public sealed record InstrumentImportStatusResult(
    string Decision,
    IReadOnlyList<string> ReasonCodes,
    string FileRegistrationId,
    long? CurrentVersion,
    int? CompletedRowCount,
    int? PendingExceptionCount,
    string RuleSetVersion);

public interface IInstrumentImportPort
{
    ValueTask<InstrumentImportStatusResult> EvaluateAsync(
        InstrumentImportStatusRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record InstrumentAuthorizationRequest(
    string OrganizationGroupId,
    string ActorId,
    InstrumentObjectContext ObjectScope,
    string Capability);

public sealed record InstrumentAuthorizationDecision(bool Allowed)
{
    public static InstrumentAuthorizationDecision Permit { get; } = new(true);
    public static InstrumentAuthorizationDecision Deny { get; } = new(false);
}

public interface IInstrumentAuthorizationPort
{
    ValueTask<InstrumentAuthorizationDecision> AuthorizeAsync(
        InstrumentAuthorizationRequest request,
        CancellationToken cancellationToken = default);
}
