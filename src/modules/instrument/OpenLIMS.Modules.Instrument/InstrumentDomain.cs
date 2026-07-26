using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using OpenLIMS.Contracts.Instrument;

namespace OpenLIMS.Modules.Instrument;

public sealed class InstrumentDomainException(string errorCode) : Exception(errorCode)
{
    public string ErrorCode { get; } = errorCode;
}

internal static partial class InstrumentRules
{
    private const int MaximumRowCount = 100_000;
    private static readonly Regex StableIdentifier = StableIdentifierPattern();
    private static readonly Regex Sha256Hex = Sha256Pattern();

    public static RegisterInstrumentFileRequest ValidateRegistration(RegisterInstrumentFileRequest? request)
    {
        if (request is null)
            throw new InstrumentDomainException(InstrumentErrorCodes.ValidationFailed);
        if (!string.Equals(request.RuleSetVersion, InstrumentContract.RuleSetVersion, StringComparison.Ordinal))
            throw new InstrumentDomainException(InstrumentErrorCodes.ValidationFailed);
        if (request.ObjectScope is null)
            throw new InstrumentDomainException(InstrumentErrorCodes.ValidationFailed);
        if (request.SourceSystem is not (InstrumentSourceSystems.Instrument or
            InstrumentSourceSystems.Cds or InstrumentSourceSystems.Middleware))
        {
            throw new InstrumentDomainException(InstrumentErrorCodes.ValidationFailed);
        }
        if (request.Sha256 is null || !Sha256Hex.IsMatch(request.Sha256))
            throw new InstrumentDomainException(InstrumentErrorCodes.ValidationFailed);
        if (request.DeclaredRowCount is < 1 or > MaximumRowCount)
            throw new InstrumentDomainException(InstrumentErrorCodes.ValidationFailed);

        return request with
        {
            ObjectScope = new InstrumentObjectContext(
                Identifier(request.ObjectScope.LegalEntityId),
                Identifier(request.ObjectScope.LaboratoryId)),
            ExternalRef = Reference(request.ExternalRef),
            InstrumentRef = Reference(request.InstrumentRef),
            ParserVersion = Identifier(request.ParserVersion)
        };
    }

    /// <summary>
    /// Classifies each submitted row as a valid parsed-row fact or an import
    /// exception. Raw content is preserved verbatim in both outcomes — the
    /// exception queue never rewrites what the instrument produced.
    /// A row number that a queued exception already holds cannot receive a
    /// second exception (the queue is keyed by row number), so it is rejected
    /// outright instead of being classified as a duplicate.
    /// </summary>
    public static (IReadOnlyList<InstrumentRowInput> ValidRows, IReadOnlyList<(InstrumentRowInput Row, string ReasonCode)> Exceptions)
        ClassifyRows(
            SubmitInstrumentRowsRequest? request,
            IReadOnlySet<int> factRowNumbers,
            IReadOnlySet<int> queuedRowNumbers)
    {
        if (request is null || request.Rows is null || request.Rows.Count is < 1 or > MaximumRowCount)
            throw new InstrumentDomainException(InstrumentErrorCodes.ValidationFailed);
        if (!string.Equals(request.RuleSetVersion, InstrumentContract.RuleSetVersion, StringComparison.Ordinal))
            throw new InstrumentDomainException(InstrumentErrorCodes.ValidationFailed);

        var valid = new List<InstrumentRowInput>();
        var exceptions = new List<(InstrumentRowInput, string)>();
        var seen = new HashSet<int>(factRowNumbers);
        var queued = new HashSet<int>(queuedRowNumbers);
        foreach (var row in request.Rows)
        {
            if (row is null || row.RowNumber is < 1 or > MaximumRowCount ||
                string.IsNullOrWhiteSpace(row.RawValue) || queued.Contains(row.RowNumber))
            {
                throw new InstrumentDomainException(InstrumentErrorCodes.ValidationFailed);
            }

            if (!seen.Add(row.RowNumber))
            {
                exceptions.Add((row, InstrumentExceptionReasons.DuplicateRow));
                queued.Add(row.RowNumber);
                continue;
            }
            if (!IsIdentifier(row.SampleNumber))
            {
                exceptions.Add((row, InstrumentExceptionReasons.UnknownSample));
                queued.Add(row.RowNumber);
                continue;
            }
            if (!IsIdentifier(row.Unit))
            {
                exceptions.Add((row, InstrumentExceptionReasons.IllegalUnit));
                queued.Add(row.RowNumber);
                continue;
            }
            if (string.IsNullOrWhiteSpace(row.ParsedValue))
            {
                exceptions.Add((row, InstrumentExceptionReasons.UnparsableValue));
                queued.Add(row.RowNumber);
                continue;
            }
            if (row.Qualifier is not null && !IsIdentifier(row.Qualifier))
            {
                exceptions.Add((row, InstrumentExceptionReasons.QualifierConflict));
                queued.Add(row.RowNumber);
                continue;
            }
            if (!IsIdentifier(row.BatchPosition) || !IsIdentifier(row.Parameter))
            {
                throw new InstrumentDomainException(InstrumentErrorCodes.ValidationFailed);
            }

            valid.Add(row);
        }

        return (valid, exceptions);
    }

    public static ResolveImportExceptionRequest ValidateResolution(ResolveImportExceptionRequest? request)
    {
        if (request is null ||
            !string.Equals(request.RuleSetVersion, InstrumentContract.RuleSetVersion, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new InstrumentDomainException(InstrumentErrorCodes.ValidationFailed);
        }

        var isAccept = string.Equals(request.Kind, InstrumentResolutionKinds.AcceptWithMapping, StringComparison.Ordinal);
        var isReject = string.Equals(request.Kind, InstrumentResolutionKinds.RejectRow, StringComparison.Ordinal);
        if (!isAccept && !isReject)
            throw new InstrumentDomainException(InstrumentErrorCodes.ValidationFailed);
        if (isReject && request.CorrectedMapping is not null)
            throw new InstrumentDomainException(InstrumentErrorCodes.ValidationFailed);
        if (isAccept)
        {
            var mapping = request.CorrectedMapping
                ?? throw new InstrumentDomainException(InstrumentErrorCodes.ValidationFailed);
            if (!IsIdentifier(mapping.SampleNumber) || !IsIdentifier(mapping.BatchPosition) ||
                !IsIdentifier(mapping.Parameter) || !IsIdentifier(mapping.Unit) ||
                (mapping.Qualifier is not null && !IsIdentifier(mapping.Qualifier)))
            {
                throw new InstrumentDomainException(InstrumentErrorCodes.ValidationFailed);
            }
        }

        return request;
    }

    public static string ResolveFileState(int declaredRowCount, int validRowCount, int pendingExceptionCount, int resolvedExceptionCount)
    {
        if (pendingExceptionCount > 0)
            return InstrumentFileStates.Blocked;
        return validRowCount + resolvedExceptionCount >= declaredRowCount
            ? InstrumentFileStates.Completed
            : InstrumentFileStates.Ingested;
    }

    public static InstrumentImportStatusResult EvaluateStatus(
        InstrumentImportStatusRequest request,
        InstrumentFileResult? file)
    {
        if (!string.Equals(request.RuleSetVersion, InstrumentContract.RuleSetVersion, StringComparison.Ordinal))
            return Unknown(file, InstrumentStatusReasons.RuleSetVersionUnknown);
        if (file is null)
            return Blocked(null, InstrumentStatusReasons.FileRequired);
        if (request.ExpectedFileVersion != file.Version)
            return Unknown(file, InstrumentStatusReasons.VersionMismatch);
        var pending = file.Exceptions.Count(entry =>
            string.Equals(entry.State, InstrumentExceptionStates.Pending, StringComparison.Ordinal));
        if (pending > 0)
            return Blocked(file, InstrumentStatusReasons.PendingExceptions);
        if (!string.Equals(file.State, InstrumentFileStates.Completed, StringComparison.Ordinal))
            return Blocked(file, InstrumentStatusReasons.ImportIncomplete);

        return new InstrumentImportStatusResult(
            InstrumentStatusDecisions.Allowed,
            [],
            file.FileRegistrationId,
            file.Version,
            file.Rows.Count,
            0,
            InstrumentContract.RuleSetVersion);
    }

    public static string HashTarget(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static InstrumentVersionedReference Reference(InstrumentVersionedReference? value)
    {
        if (value is null || value.Version < 1)
            throw new InstrumentDomainException(InstrumentErrorCodes.ValidationFailed);
        return new InstrumentVersionedReference(Identifier(value.Id), value.Version);
    }

    private static string Identifier(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (!StableIdentifier.IsMatch(trimmed))
            throw new InstrumentDomainException(InstrumentErrorCodes.ValidationFailed);
        return trimmed;
    }

    private static bool IsIdentifier(string? value) =>
        value is not null && StableIdentifier.IsMatch(value.Trim()) && value.Trim().Length > 0;

    private static InstrumentImportStatusResult Blocked(InstrumentFileResult? file, string reason) => new(
        InstrumentStatusDecisions.Blocked, [reason], file?.FileRegistrationId ?? string.Empty,
        file?.Version, file?.Rows.Count,
        file?.Exceptions.Count(entry => string.Equals(entry.State, InstrumentExceptionStates.Pending, StringComparison.Ordinal)),
        InstrumentContract.RuleSetVersion);

    private static InstrumentImportStatusResult Unknown(InstrumentFileResult? file, string reason) => new(
        InstrumentStatusDecisions.Unknown, [reason], file?.FileRegistrationId ?? string.Empty,
        file?.Version, null, null, InstrumentContract.RuleSetVersion);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex StableIdentifierPattern();

    [GeneratedRegex("^[a-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();
}
