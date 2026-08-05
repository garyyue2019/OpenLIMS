using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using OpenLIMS.Contracts.Ai;

namespace OpenLIMS.Modules.Ai;

internal sealed class AiDomainException(string errorCode) : Exception(errorCode)
{
    public string ErrorCode { get; } = errorCode;
}

internal sealed record EvaluatedAiOutcome(
    string Status,
    string ProviderStatus,
    string? ProviderExternalReference,
    string? ProviderFailureCode,
    AiStructuredOutput? OriginalOutput,
    AiValidationResult? Validation,
    bool HumanReviewRequired,
    bool ManualFallbackRequired);

internal static class AiRuntimeRules
{
    private static readonly Regex IdentifierPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static CreateAiRunRequest ValidateRun(CreateAiRunRequest? request, IAiOutputValidator validator)
    {
        ArgumentNullException.ThrowIfNull(validator);
        if (request is null ||
            !string.Equals(request.RuleSetVersion, AiContract.RuntimeRuleSetVersion, StringComparison.Ordinal) ||
            request.Envelope is null || request.ValidationProfile is null || request.ValidationProfile.Version < 1 ||
            request.AllowedFields is null || request.AllowedFields.Count is < 1 or > 200 ||
            request.AllowedUnits is null || request.AllowedUnits.Count > 200)
        {
            throw new AiDomainException(AiErrorCodes.ValidationFailed);
        }

        var fields = NormalizeSet(request.AllowedFields, requireOne: true);
        var units = NormalizeSet(request.AllowedUnits, requireOne: false);
        var normalized = request with
        {
            ObjectScope = NormalizeObjectScope(request.ObjectScope),
            ValidationProfile = new AiVersionedReference(
                Identifier(request.ValidationProfile.Id), request.ValidationProfile.Version),
            AllowedFields = fields,
            AllowedUnits = units,
            IdempotencyKey = Identifier(request.IdempotencyKey)
        };
        try
        {
            validator.Validate(
                new AiStructuredOutput(AiContract.RuleSetVersion, normalized.Envelope, [], []),
                fields.ToHashSet(StringComparer.Ordinal), units.ToHashSet(StringComparer.Ordinal));
        }
        catch (AiContractException)
        {
            throw new AiDomainException(AiErrorCodes.ValidationFailed);
        }
        return normalized;
    }

    public static RecordAiDispositionRequest ValidateDispositionRequest(RecordAiDispositionRequest? request)
    {
        if (request is null || request.ExpectedRunVersion < 1 ||
            !string.Equals(request.RuleSetVersion, AiContract.RuntimeRuleSetVersion, StringComparison.Ordinal))
        {
            throw new AiDomainException(AiErrorCodes.ValidationFailed);
        }
        return request with
        {
            CandidateId = Identifier(request.CandidateId),
            IdempotencyKey = Identifier(request.IdempotencyKey),
            Reason = RequiredText(request.Reason, 1000),
            HumanValue = OptionalText(request.HumanValue, 4000)
        };
    }

    public static EvaluatedAiOutcome EvaluateProviderResponse(
        CreateAiRunRequest request,
        AiProviderResponse? response,
        IAiOutputValidator validator)
    {
        ArgumentNullException.ThrowIfNull(validator);
        if (response is null)
            return ProviderFailed(AiValidationErrorCodes.ProviderResponseInvalid);
        if (string.Equals(response.Status, AiProviderStatuses.Disabled, StringComparison.Ordinal))
            return response.Output is null && response.ExternalReference is null &&
                   TryOptionalProviderText(response.FailureCode, 200, out var disabledFailureCode)
                ? new EvaluatedAiOutcome(
                    AiRunStatuses.ProviderDisabled, AiProviderStatuses.Disabled, null,
                    disabledFailureCode, null, null, false, true)
                : ProviderFailed(AiValidationErrorCodes.ProviderResponseInvalid);
        if (string.Equals(response.Status, AiProviderStatuses.Failed, StringComparison.Ordinal))
            return response.Output is null && response.ExternalReference is null &&
                   TryOptionalProviderText(response.FailureCode, 200, out var providerFailureCode)
                ? ProviderFailed(providerFailureCode ?? "PROVIDER_FAILED")
                : ProviderFailed(AiValidationErrorCodes.ProviderResponseInvalid);
        if (!string.Equals(response.Status, AiProviderStatuses.Completed, StringComparison.Ordinal) ||
            response.Output is null ||
            !TryRequiredProviderText(response.ExternalReference, 200, out var externalReference))
        {
            return ProviderFailed(AiValidationErrorCodes.ProviderResponseInvalid);
        }

        if (!EnvelopeEquals(request.Envelope, response.Output.Envelope))
        {
            return Quarantined(
                response, externalReference,
                new AiValidationError("envelope", AiValidationErrorCodes.EnvelopeMismatch,
                    "provider output envelope does not match the requested run envelope"));
        }

        try
        {
            var validation = validator.Validate(
                response.Output,
                request.AllowedFields.ToHashSet(StringComparer.Ordinal),
                request.AllowedUnits.ToHashSet(StringComparer.Ordinal));
            return string.Equals(validation.Decision, AiValidationDecisions.Accepted, StringComparison.Ordinal)
                ? new EvaluatedAiOutcome(
                    AiRunStatuses.Accepted, AiProviderStatuses.Completed, externalReference,
                    null, response.Output, validation, true, false)
                : new EvaluatedAiOutcome(
                    AiRunStatuses.Quarantined, AiProviderStatuses.Completed, externalReference,
                    null, response.Output, validation, true, true);
        }
        catch (AiContractException exception)
        {
            var code = string.Equals(exception.ErrorCode, AiErrorCodes.FactClassPromotionRejected, StringComparison.Ordinal)
                ? AiValidationErrorCodes.FactClassPromotion
                : AiValidationErrorCodes.ProviderResponseInvalid;
            return Quarantined(
                response, externalReference,
                new AiValidationError("output", code, exception.ErrorCode));
        }
    }

    public static AiDisposition BuildDisposition(
        Guid dispositionId,
        RecordAiDispositionRequest request,
        AiFieldCandidate candidate,
        string actorId,
        IAiOutputValidator validator)
    {
        var disposition = new AiDisposition(
            dispositionId.ToString("N"), candidate.CandidateId, request.Kind,
            candidate.Value, request.Reason, actorId, request.HumanValue);
        try
        {
            validator.ValidateDisposition(disposition, candidate);
        }
        catch (AiContractException exception)
        {
            throw new AiDomainException(exception.ErrorCode);
        }
        return disposition;
    }

    public static string RequestHash(CreateAiRunRequest request)
    {
        var envelope = request.Envelope;
        var value = new StringBuilder()
            .Append(request.RuleSetVersion).Append('\n')
            .Append(request.ObjectScope.LegalEntityId).Append('\n')
            .Append(request.ObjectScope.LaboratoryId).Append('\n')
            .Append(request.ObjectScope.CustomerId).Append('\n')
            .Append(request.ObjectScope.ServiceOrderId).Append('\n')
            .Append(request.ObjectScope.ProductCategory).Append('\n')
            .Append(envelope.Model.Id).Append('@').Append(envelope.Model.Version).Append('\n')
            .Append(envelope.GatewayRoute).Append('\n')
            .Append(envelope.PromptTemplate.Id).Append('@').Append(envelope.PromptTemplate.Version).Append('\n')
            .Append(envelope.OutputSchema.Id).Append('@').Append(envelope.OutputSchema.Version).Append('\n')
            .Append(string.Join('\n', envelope.InputRefs.Select(entry => $"{entry.Id}@{entry.Version}"))).Append('\n')
            .Append(request.ValidationProfile.Id).Append('@').Append(request.ValidationProfile.Version).Append('\n')
            .Append(string.Join('\n', request.AllowedFields)).Append('\n')
            .Append(string.Join('\n', request.AllowedUnits))
            .ToString();
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    public static string TargetHash(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "unresolved-target" : value.Trim();
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    private static EvaluatedAiOutcome Quarantined(
        AiProviderResponse response,
        string externalReference,
        AiValidationError error) => new(
        AiRunStatuses.Quarantined, AiProviderStatuses.Completed, externalReference, null,
        response.Output,
        new AiValidationResult(
            AiValidationDecisions.Quarantined, [error], [], [], AiContract.RuleSetVersion),
        true, true);

    private static EvaluatedAiOutcome ProviderFailed(string failureCode) => new(
        AiRunStatuses.ProviderFailed, AiProviderStatuses.Failed, null, failureCode,
        null, null, false, true);

    private static bool EnvelopeEquals(AiRunEnvelope expected, AiRunEnvelope? actual) =>
        actual is not null && actual.InputRefs is not null &&
        expected.Model == actual.Model &&
        string.Equals(expected.GatewayRoute, actual.GatewayRoute, StringComparison.Ordinal) &&
        expected.PromptTemplate == actual.PromptTemplate &&
        expected.OutputSchema == actual.OutputSchema &&
        expected.InputRefs.SequenceEqual(actual.InputRefs);

    private static AiObjectContext NormalizeObjectScope(AiObjectContext? value)
    {
        if (value is null)
            throw new AiDomainException(AiErrorCodes.ValidationFailed);
        return new AiObjectContext(
            Identifier(value.LegalEntityId), Identifier(value.LaboratoryId),
            Identifier(value.CustomerId), Identifier(value.ServiceOrderId),
            Identifier(value.ProductCategory));
    }

    private static IReadOnlyList<string> NormalizeSet(IReadOnlyList<string> values, bool requireOne)
    {
        var normalized = values.Select(Identifier).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if ((requireOne && normalized.Count == 0) || normalized.Count != values.Count)
            throw new AiDomainException(AiErrorCodes.ValidationFailed);
        return normalized;
    }

    private static string Identifier(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (!IdentifierPattern.IsMatch(trimmed))
            throw new AiDomainException(AiErrorCodes.ValidationFailed);
        return trimmed;
    }

    private static string RequiredText(string? value, int maximumLength)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length is < 1 || trimmed.Length > maximumLength)
            throw new AiDomainException(AiErrorCodes.ValidationFailed);
        return trimmed;
    }

    private static string? OptionalText(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        if (trimmed.Length > maximumLength)
            throw new AiDomainException(AiErrorCodes.ValidationFailed);
        return trimmed;
    }

    private static bool TryOptionalProviderText(string? value, int maximumLength, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(value))
            return true;
        var trimmed = value.Trim();
        if (trimmed.Length > maximumLength)
            return false;
        normalized = trimmed;
        return true;
    }

    private static bool TryRequiredProviderText(string? value, int maximumLength, out string normalized)
    {
        normalized = value?.Trim() ?? string.Empty;
        return normalized.Length is > 0 && normalized.Length <= maximumLength;
    }
}
