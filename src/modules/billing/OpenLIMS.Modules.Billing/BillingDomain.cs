using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using OpenLIMS.Contracts.Billing;

namespace OpenLIMS.Modules.Billing;

internal sealed class BillingDomainException(string errorCode, string? gateSource = null) : Exception(errorCode)
{
    public string ErrorCode { get; } = errorCode;
    public string? GateSource { get; } = gateSource;
}

internal static class BillingRules
{
    private static readonly decimal MaximumAmount = 1_000_000_000_000m;
    private static readonly Regex StableIdentifier = new(
        "^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static BillingObjectContext NormalizeObjectScope(BillingObjectContext? value)
    {
        if (value is null)
            throw new BillingDomainException(BillingErrorCodes.ValidationFailed);
        return new BillingObjectContext(
            Identifier(value.LegalEntityId),
            Identifier(value.LaboratoryId),
            Identifier(value.CustomerId),
            Identifier(value.ServiceOrderId),
            Identifier(value.ProductCategory));
    }

    public static CreateBillingEvidenceRequest ValidateEvidence(CreateBillingEvidenceRequest? request)
    {
        if (request is null)
            throw new BillingDomainException(BillingErrorCodes.ValidationFailed);
        if (!string.Equals(request.RuleSetVersion, BillingContract.RuleSetVersion, StringComparison.Ordinal))
            throw new BillingDomainException(BillingErrorCodes.ApplicabilityUnknown);
        if (request.ExpectedGroupVersion < 1 ||
            request.Amount < 0 || request.Amount >= MaximumAmount)
        {
            throw new BillingDomainException(BillingErrorCodes.ValidationFailed);
        }

        var zeroReason = string.IsNullOrWhiteSpace(request.ZeroAmountReason) ? null : request.ZeroAmountReason.Trim();
        if (request.Amount == 0 && zeroReason is null)
            throw new BillingDomainException(BillingErrorCodes.ValidationFailed);
        if (request.Amount != 0 && zeroReason is not null)
            throw new BillingDomainException(BillingErrorCodes.ValidationFailed);
        if (zeroReason is { Length: > 500 })
            throw new BillingDomainException(BillingErrorCodes.ValidationFailed);

        return request with
        {
            ObjectScope = NormalizeObjectScope(request.ObjectScope),
            ResultGroupId = Identifier(request.ResultGroupId),
            ContractBaseline = Reference(request.ContractBaseline),
            ChargeDimension = Identifier(request.ChargeDimension),
            BillingRuleVersion = Identifier(request.BillingRuleVersion),
            Currency = Reference(request.Currency),
            ZeroAmountReason = zeroReason
        };
    }

    public static AddBillingAdjustmentRequest ValidateAdjustment(AddBillingAdjustmentRequest? request)
    {
        if (request is null)
            throw new BillingDomainException(BillingErrorCodes.ValidationFailed);
        if (!string.Equals(request.RuleSetVersion, BillingContract.RuleSetVersion, StringComparison.Ordinal))
            throw new BillingDomainException(BillingErrorCodes.ApplicabilityUnknown);
        if (request.Amount == 0 || Math.Abs(request.Amount) >= MaximumAmount)
            throw new BillingDomainException(BillingErrorCodes.ValidationFailed);
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length is < 1 or > 500)
            throw new BillingDomainException(BillingErrorCodes.ValidationFailed);
        return request with { Reason = reason };
    }

    public static BillingEvidenceStatusResult EvaluateStatus(
        BillingEvidenceStatusRequest request,
        BillingEvidenceResult? evidence)
    {
        if (!string.Equals(request.RuleSetVersion, BillingContract.RuleSetVersion, StringComparison.Ordinal))
        {
            return new BillingEvidenceStatusResult(
                BillingStatusDecisions.Unknown,
                [BillingStatusReasons.RuleSetVersionUnknown],
                evidence?.BillingEvidenceId, evidence?.Stage, evidence?.Amount,
                evidence?.Adjustments.Count, BillingContract.RuleSetVersion);
        }

        if (evidence is null)
        {
            return new BillingEvidenceStatusResult(
                BillingStatusDecisions.Blocked,
                [BillingStatusReasons.EvidenceRequired],
                null, null, null, null, BillingContract.RuleSetVersion);
        }

        return new BillingEvidenceStatusResult(
            BillingStatusDecisions.Allowed,
            [],
            evidence.BillingEvidenceId,
            evidence.Stage,
            evidence.Amount,
            evidence.Adjustments.Count,
            BillingContract.RuleSetVersion);
    }

    public static string HashTarget(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static BillingVersionedReference Reference(BillingVersionedReference? value)
    {
        if (value is null || value.Version < 1)
            throw new BillingDomainException(BillingErrorCodes.ValidationFailed);
        return new BillingVersionedReference(Identifier(value.Id), value.Version);
    }

    private static string Identifier(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (!StableIdentifier.IsMatch(trimmed))
            throw new BillingDomainException(BillingErrorCodes.ValidationFailed);
        return trimmed;
    }
}
