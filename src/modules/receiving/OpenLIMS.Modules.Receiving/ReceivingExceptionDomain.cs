using System.Text.RegularExpressions;
using OpenLIMS.Contracts.Receiving;

namespace OpenLIMS.Modules.Receiving;

internal static partial class ReceivingExceptionRules
{
    private const int MaximumEvidenceItems = 50;
    private const int MaximumIdentifierLength = 200;
    private const int MaximumTextLength = 1000;

    public static string ValidateCreate(CreateReceivingExceptionRequest request, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ExpectedItemVersion < 1 ||
            request.ObservedAt == default || request.ObservedAt > now.AddMinutes(5) ||
            !ValidText(request.Description, MaximumTextLength) ||
            !ValidEvidence(request.EvidenceRefs, request.EvidenceHashes))
        {
            throw new ReceivingDomainException(ReceivingErrorCodes.DecisionEvidenceIncomplete);
        }

        return request.Type switch
        {
            ReceivingExceptionTypes.QuantityShortage or
            ReceivingExceptionTypes.TemperatureExcursion or
            ReceivingExceptionTypes.Damaged or
            ReceivingExceptionTypes.LabelConflict or
            ReceivingExceptionTypes.IdentityMismatch or
            ReceivingExceptionTypes.IdentityIndeterminate => ReceivingExceptionSeverities.Standard,
            ReceivingExceptionTypes.Contamination => ReceivingExceptionSeverities.SafetyCritical,
            _ => throw new ReceivingDomainException(ReceivingErrorCodes.ExceptionTypeUnknown)
        };
    }

    public static string ValidateDecision(
        SubmitReceivingExceptionDecisionRequest request,
        string severity,
        string createdBy,
        string actorId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ExpectedVersion < 1 ||
            !string.Equals(request.MatrixVersion, ReceivingExceptionContract.MatrixVersion, StringComparison.Ordinal) ||
            !ValidEvidence(request.EvidenceRefs, request.EvidenceHashes) ||
            !ValidText(request.Rationale, MaximumTextLength) ||
            string.Equals(createdBy, actorId, StringComparison.Ordinal))
        {
            throw new ReceivingDomainException(
                string.Equals(createdBy, actorId, StringComparison.Ordinal)
                    ? ReceivingErrorCodes.DecisionNotAuthorized
                    : ReceivingErrorCodes.DecisionEvidenceIncomplete);
        }

        var requiredCapability = severity switch
        {
            ReceivingExceptionSeverities.Standard => ValidateStandardDecision(request, now),
            ReceivingExceptionSeverities.SafetyCritical => ValidateSafetyDecision(request),
            _ => throw new ReceivingDomainException(ReceivingErrorCodes.ApplicabilityUnknown)
        };
        return requiredCapability;
    }

    public static string StatusFor(string decisionType) => decisionType switch
    {
        ReceivingExceptionDecisionTypes.AwaitCustomer => ReceivingExceptionStatuses.AwaitingCustomer,
        ReceivingExceptionDecisionTypes.ConditionalAccept => ReceivingExceptionStatuses.ConditionallyAccepted,
        ReceivingExceptionDecisionTypes.Reject => ReceivingExceptionStatuses.Rejected,
        ReceivingExceptionDecisionTypes.SafetyHold => ReceivingExceptionStatuses.SafetyHold,
        _ => throw new ReceivingDomainException(ReceivingErrorCodes.ApplicabilityUnknown)
    };

    public static void ValidateIdentityState(string type, string? assessmentState)
    {
        if (string.Equals(type, ReceivingExceptionTypes.IdentityMismatch, StringComparison.Ordinal) &&
            !string.Equals(assessmentState, IdentityAssessmentStates.Mismatched, StringComparison.Ordinal))
        {
            throw new ReceivingDomainException(ReceivingErrorCodes.ApplicabilityUnknown);
        }
        if (string.Equals(type, ReceivingExceptionTypes.IdentityIndeterminate, StringComparison.Ordinal) &&
            !string.Equals(assessmentState, IdentityAssessmentStates.Indeterminate, StringComparison.Ordinal))
        {
            throw new ReceivingDomainException(ReceivingErrorCodes.ApplicabilityUnknown);
        }
    }

    private static string ValidateStandardDecision(SubmitReceivingExceptionDecisionRequest request, DateTimeOffset now)
    {
        if (request.DecisionType is not (ReceivingExceptionDecisionTypes.AwaitCustomer or
            ReceivingExceptionDecisionTypes.ConditionalAccept or ReceivingExceptionDecisionTypes.Reject))
        {
            throw new ReceivingDomainException(ReceivingErrorCodes.DecisionNotAuthorized);
        }

        if (string.Equals(request.DecisionType, ReceivingExceptionDecisionTypes.ConditionalAccept, StringComparison.Ordinal))
        {
            if (!ValidText(request.TechnicalImpact, MaximumTextLength) ||
                request.AllowedActions is null || request.AllowedActions.Count is < 1 or > 3 ||
                request.ProhibitedActions is null || request.ProhibitedActions.Count is < 1 or > 3 ||
                request.AllowedActions.Any(action => !KnownAction(action)) ||
                request.ProhibitedActions.Any(action => !KnownAction(action)) ||
                request.AllowedActions.Intersect(request.ProhibitedActions, StringComparer.Ordinal).Any() ||
                request.ValidUntil is null || request.ValidUntil <= now || request.ValidUntil > now.AddYears(1))
            {
                throw new ReceivingDomainException(ReceivingErrorCodes.ConditionalAcceptConstraintsRequired);
            }
        }
        else if ((request.AllowedActions?.Count ?? 0) != 0 ||
                 (request.ProhibitedActions?.Count ?? 0) != 0 || request.ValidUntil is not null)
        {
            throw new ReceivingDomainException(ReceivingErrorCodes.DecisionEvidenceIncomplete);
        }

        return ReceivingCapabilities.ExceptionQualityApprove;
    }

    private static string ValidateSafetyDecision(SubmitReceivingExceptionDecisionRequest request)
    {
        if (request.DecisionType is not (ReceivingExceptionDecisionTypes.Reject or ReceivingExceptionDecisionTypes.SafetyHold) ||
            (request.AllowedActions?.Count ?? 0) != 0 || (request.ProhibitedActions?.Count ?? 0) != 0 ||
            request.ValidUntil is not null)
        {
            throw new ReceivingDomainException(ReceivingErrorCodes.DecisionNotAuthorized);
        }
        return ReceivingCapabilities.ExceptionEhsApprove;
    }

    private static bool KnownAction(string action) => action is
        ReceivingEligibilityActions.Disassembly or
        ReceivingEligibilityActions.SamplePreparation or
        ReceivingEligibilityActions.TestAssignment;

    private static bool ValidEvidence(IReadOnlyList<string>? references, IReadOnlyList<string>? hashes) =>
        references is not null && references.Count is >= 1 and <= MaximumEvidenceItems &&
        hashes is not null && hashes.Count == references.Count &&
        references.All(value => ValidText(value, MaximumIdentifierLength)) &&
        hashes.All(value => Sha256Regex().IsMatch(value));

    private static bool ValidText(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength;

    [GeneratedRegex("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Regex();
}

internal sealed record ReceivingExceptionScope(
    IdentityItemScope Item,
    Guid ExceptionId,
    string Type,
    string Severity,
    string CreatedBy,
    string Status,
    long Version);
