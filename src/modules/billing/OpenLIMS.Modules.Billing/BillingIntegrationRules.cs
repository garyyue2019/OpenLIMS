using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using OpenLIMS.Contracts.Billing;

namespace OpenLIMS.Modules.Billing;

internal static class BillingIntegrationRules
{
    private static readonly Regex IdentifierPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static CreateBillingExportBatchRequest ValidateExport(CreateBillingExportBatchRequest? request)
    {
        if (request is null ||
            !string.Equals(request.RuleSetVersion, BillingContract.ExportRuleSetVersion, StringComparison.Ordinal) ||
            request.BillingEvidenceIds is null ||
            request.BillingEvidenceIds.Count is < 1 or > 500)
        {
            throw new BillingDomainException(BillingErrorCodes.ValidationFailed);
        }

        var evidenceIds = request.BillingEvidenceIds
            .Select(ParseEvidenceId)
            .Select(value => value.ToString("N"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (evidenceIds.Count != request.BillingEvidenceIds.Count)
            throw new BillingDomainException(BillingErrorCodes.ValidationFailed);

        return request with
        {
            BillingEvidenceIds = evidenceIds,
            ExportSchemaVersion = Identifier(request.ExportSchemaVersion),
            IdempotencyKey = Identifier(request.IdempotencyKey)
        };
    }

    public static CreateBillingHandoffRequest ValidateHandoff(CreateBillingHandoffRequest? request)
    {
        if (request is null ||
            !string.Equals(request.RuleSetVersion, BillingContract.HandoffRuleSetVersion, StringComparison.Ordinal) ||
            !BillingExternalSystems.All.Contains(request.ExternalSystem, StringComparer.Ordinal) ||
            !BillingHandoffModes.All.Contains(request.Mode, StringComparer.Ordinal) ||
            request.Endpoint is null || request.Endpoint.Version < 1)
        {
            throw new BillingDomainException(BillingErrorCodes.ValidationFailed);
        }

        return request with
        {
            Endpoint = new BillingVersionedReference(Identifier(request.Endpoint.Id), request.Endpoint.Version),
            IdempotencyKey = Identifier(request.IdempotencyKey)
        };
    }

    public static RecordBillingHandoffAttemptRequest ValidateAttempt(
        string externalSystem,
        RecordBillingHandoffAttemptRequest? request)
    {
        if (request is null ||
            !string.Equals(request.RuleSetVersion, BillingContract.HandoffRuleSetVersion, StringComparison.Ordinal) ||
            !BillingHandoffOutcomes.Attempts.Contains(request.Outcome, StringComparer.Ordinal))
        {
            throw new BillingDomainException(BillingErrorCodes.ValidationFailed);
        }

        var externalReference = Optional(request.ExternalReference, 200);
        var detailCode = Optional(request.DetailCode, 200);
        var succeeded = string.Equals(request.Outcome, BillingHandoffOutcomes.Succeeded, StringComparison.Ordinal);
        if (succeeded && externalReference is null)
            throw new BillingDomainException(BillingErrorCodes.HandoffConfirmationInvalid);
        if (!succeeded && request.ErpPosting is not null)
            throw new BillingDomainException(BillingErrorCodes.HandoffConfirmationInvalid);

        ErpPostingConfirmation? posting = null;
        if (string.Equals(externalSystem, BillingExternalSystems.Erp, StringComparison.Ordinal))
        {
            if (succeeded)
                posting = ValidateErpPosting(request.ErpPosting);
        }
        else if (request.ErpPosting is not null)
        {
            throw new BillingDomainException(BillingErrorCodes.HandoffConfirmationInvalid);
        }

        return request with
        {
            IdempotencyKey = Identifier(request.IdempotencyKey),
            ExternalReference = externalReference,
            DetailCode = detailCode,
            ErpPosting = posting
        };
    }

    public static IReadOnlyList<BillingExportItemResult> BuildItems(IReadOnlyList<BillingEvidenceResult> evidence)
    {
        if (evidence.Count == 0)
            throw new BillingDomainException(BillingErrorCodes.ValidationFailed);
        var first = evidence[0];
        if (evidence.Any(item => item.ObjectScope != first.ObjectScope || item.Currency != first.Currency))
            throw new BillingDomainException(BillingErrorCodes.ExportScopeMismatch);

        return evidence
            .OrderBy(item => item.BillingEvidenceId, StringComparer.Ordinal)
            .Select(item =>
            {
                var adjustments = item.Adjustments.Sum(entry => entry.Amount);
                return new BillingExportItemResult(
                    item.BillingEvidenceId, item.ResultGroupId, item.GroupVersion,
                    item.Amount, adjustments, item.Amount + adjustments, item.Currency);
            })
            .ToList();
    }

    public static string Canonicalize(
        BillingObjectContext objectScope,
        string exportSchemaVersion,
        IReadOnlyList<BillingExportItemResult> items)
    {
        var builder = new StringBuilder();
        builder.Append("ruleSet=").Append(BillingContract.ExportRuleSetVersion).Append('\n');
        builder.Append("schema=").Append(exportSchemaVersion).Append('\n');
        builder.Append("scope=")
            .Append(objectScope.LegalEntityId).Append('|')
            .Append(objectScope.LaboratoryId).Append('|')
            .Append(objectScope.CustomerId).Append('|')
            .Append(objectScope.ServiceOrderId).Append('|')
            .Append(objectScope.ProductCategory).Append('\n');
        foreach (var item in items.OrderBy(entry => entry.BillingEvidenceId, StringComparer.Ordinal))
        {
            builder.Append("item=").Append(item.BillingEvidenceId).Append('|')
                .Append(item.ResultGroupId).Append('@')
                .Append(item.GroupVersion.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(item.BaseAmount.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(item.AdjustmentAmount.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(item.NetAmount.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(item.Currency.Id).Append('@')
                .Append(item.Currency.Version.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }
        return builder.ToString();
    }

    public static string ComputeHash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static string ResolveHandoffStatus(IReadOnlyList<BillingHandoffAttemptResult> attempts) =>
        attempts.OrderBy(entry => entry.AttemptNumber).LastOrDefault()?.Outcome ?? BillingHandoffOutcomes.Pending;

    public static string RequestHash(CreateBillingExportBatchRequest request) =>
        ComputeHash($"{request.ExportSchemaVersion}\n{string.Join('\n', request.BillingEvidenceIds)}");

    private static ErpPostingConfirmation ValidateErpPosting(ErpPostingConfirmation? posting)
    {
        if (posting is null || posting.FiscalYear is < 2000 or > 9999 || posting.Period is < 1 or > 16)
            throw new BillingDomainException(BillingErrorCodes.HandoffConfirmationInvalid);
        return posting with
        {
            VoucherNumber = Identifier(posting.VoucherNumber),
            CompanyCode = Identifier(posting.CompanyCode)
        };
    }

    private static Guid ParseEvidenceId(string? value) =>
        Guid.TryParseExact(value, "N", out var id) || Guid.TryParse(value, out id)
            ? id
            : throw new BillingDomainException(BillingErrorCodes.ValidationFailed);

    private static string Identifier(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (!IdentifierPattern.IsMatch(trimmed))
            throw new BillingDomainException(BillingErrorCodes.ValidationFailed);
        return trimmed;
    }

    private static string? Optional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        if (trimmed.Length > maximumLength)
            throw new BillingDomainException(BillingErrorCodes.ValidationFailed);
        return trimmed;
    }
}
