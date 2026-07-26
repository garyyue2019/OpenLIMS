using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using OpenLIMS.Contracts.Batch;

namespace OpenLIMS.Modules.Batch;

internal sealed class BatchDomainException(string errorCode, string? gateSource = null) : Exception(errorCode)
{
    public string ErrorCode { get; } = errorCode;
    public string? GateSource { get; } = gateSource;
}

internal static class BatchRules
{
    private static readonly Regex StableIdentifier = new(
        "^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex Sha256Hex = new(
        "^[a-f0-9]{64}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static BatchObjectContext NormalizeObjectScope(BatchObjectContext? value)
    {
        if (value is null)
            throw new BatchDomainException(BatchErrorCodes.ValidationFailed);
        return new BatchObjectContext(Identifier(value.LegalEntityId), Identifier(value.LaboratoryId));
    }

    public static string ValidateBatchType(string? value) => value?.Trim() is
        BatchTypes.Preparation or BatchTypes.Preconditioning or
        BatchTypes.Analytical or BatchTypes.InstrumentRun
        ? value.Trim()
        : throw new BatchDomainException(BatchErrorCodes.ValidationFailed);

    public static void RequireRuleSet(string? value)
    {
        if (!string.Equals(value, BatchContract.RuleSetVersion, StringComparison.Ordinal))
            throw new BatchDomainException(BatchErrorCodes.ApplicabilityUnknown);
    }

    public static AddBatchMemberRequest ValidateMember(AddBatchMemberRequest? request)
    {
        if (request is null || request.ExpectedCurrentVersion < 1)
            throw new BatchDomainException(BatchErrorCodes.ExpectedVersionConflict);
        RequireRuleSet(request.RuleSetVersion);
        var memberType = request.MemberType?.Trim();
        var validated = request with
        {
            MemberType = memberType ?? string.Empty,
            CustomerId = Identifier(request.CustomerId),
            ServiceOrderId = Identifier(request.ServiceOrderId),
            ProductCategory = Identifier(request.ProductCategory)
        };
        switch (memberType)
        {
            case BatchMemberTypes.Specimen:
                if (string.IsNullOrWhiteSpace(request.AllocationId) ||
                    request.ExpectedSubjectAllocationVersion is null or < 1 ||
                    request.QcRef is not null)
                {
                    throw new BatchDomainException(BatchErrorCodes.ValidationFailed);
                }
                return validated with { AllocationId = Identifier(request.AllocationId) };
            case BatchMemberTypes.QcSample:
                if (request.AllocationId is not null ||
                    request.ExpectedSubjectAllocationVersion is not null ||
                    request.QcRef is null)
                {
                    throw new BatchDomainException(BatchErrorCodes.ValidationFailed);
                }
                return validated with { QcRef = Reference(request.QcRef) };
            default:
                throw new BatchDomainException(BatchErrorCodes.ValidationFailed);
        }
    }

    public static AddBatchEvidenceRequest ValidateEvidence(AddBatchEvidenceRequest? request)
    {
        if (request is null || request.ExpectedCurrentVersion < 1)
            throw new BatchDomainException(BatchErrorCodes.ExpectedVersionConflict);
        RequireRuleSet(request.RuleSetVersion);
        if (request.SourceSystem?.Trim() is not
            (BatchEvidenceSources.Cds or BatchEvidenceSources.Eln or BatchEvidenceSources.Instrument))
        {
            throw new BatchDomainException(BatchErrorCodes.ValidationFailed);
        }
        var sha = request.Sha256?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!Sha256Hex.IsMatch(sha))
            throw new BatchDomainException(BatchErrorCodes.ValidationFailed);
        return request with
        {
            SourceSystem = request.SourceSystem.Trim(),
            ExternalRef = Reference(request.ExternalRef),
            Sha256 = sha
        };
    }

    public static FreezeBatchRequest ValidateFreeze(FreezeBatchRequest? request)
    {
        if (request is null || request.ExpectedCurrentVersion < 1)
            throw new BatchDomainException(BatchErrorCodes.ExpectedVersionConflict);
        RequireRuleSet(request.RuleSetVersion);
        if (request.Cause?.Trim() is not
            (BatchFreezeCauses.QcFailure or BatchFreezeCauses.EnvironmentOutOfTolerance or
             BatchFreezeCauses.CalibrationInvalid))
        {
            throw new BatchDomainException(BatchErrorCodes.ValidationFailed);
        }
        return request with
        {
            Cause = request.Cause.Trim(),
            ApprovedFollowUpRef = request.ApprovedFollowUpRef is null ? null : Reference(request.ApprovedFollowUpRef)
        };
    }

    public static void RequireActive(BatchResult batch)
    {
        if (!string.Equals(batch.State, BatchStates.Active, StringComparison.Ordinal))
            throw new BatchDomainException(BatchErrorCodes.BatchFrozen);
    }

    public static void RequireVersion(long expected, long current)
    {
        if (expected != current)
            throw new BatchDomainException(BatchErrorCodes.ExpectedVersionConflict);
    }

    public static BatchStatusResult EvaluateStatus(BatchStatusRequest request, BatchResult? batch)
    {
        if (!string.Equals(request.RuleSetVersion, BatchContract.RuleSetVersion, StringComparison.Ordinal))
            return Unknown(batch, BatchStatusReasons.RuleSetVersionUnknown);
        if (batch is null)
            return Blocked(null, BatchStatusReasons.BatchRequired);
        if (request.ExpectedBatchVersion != batch.Version)
            return Unknown(batch, BatchStatusReasons.BatchVersionMismatch);
        if (string.Equals(batch.State, BatchStates.Frozen, StringComparison.Ordinal))
            return Blocked(batch, BatchStatusReasons.BatchFrozen);

        return new BatchStatusResult(
            BatchStatusDecisions.Allowed,
            [],
            batch.BatchId,
            batch.State,
            batch.Version,
            BatchContract.RuleSetVersion);
    }

    public static string HashTarget(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static BatchVersionedReference Reference(BatchVersionedReference? value)
    {
        if (value is null || value.Version < 1)
            throw new BatchDomainException(BatchErrorCodes.ValidationFailed);
        return new BatchVersionedReference(Identifier(value.Id), value.Version);
    }

    private static string Identifier(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (!StableIdentifier.IsMatch(trimmed))
            throw new BatchDomainException(BatchErrorCodes.ValidationFailed);
        return trimmed;
    }

    private static BatchStatusResult Blocked(BatchResult? batch, string reason) => new(
        BatchStatusDecisions.Blocked, [reason], batch?.BatchId, batch?.State, batch?.Version,
        BatchContract.RuleSetVersion);

    private static BatchStatusResult Unknown(BatchResult? batch, string reason) => new(
        BatchStatusDecisions.Unknown, [reason], batch?.BatchId, batch?.State, batch?.Version,
        BatchContract.RuleSetVersion);
}
