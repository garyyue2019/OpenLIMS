using OpenLIMS.Contracts.Receiving;

namespace OpenLIMS.Modules.Receiving;

internal static class ReceivingReleaseRules
{
    private const int MaximumRationaleLength = 1000;
    private static readonly string[] KnownActions =
    [
        ReceivingEligibilityActions.Disassembly,
        ReceivingEligibilityActions.SamplePreparation,
        ReceivingEligibilityActions.TestAssignment
    ];

    public static void ValidateRequest(SubmitReceivingReleaseDecisionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ExpectedItemVersion < 1 ||
            string.IsNullOrWhiteSpace(request.Rationale) ||
            request.Rationale.Length > MaximumRationaleLength)
        {
            throw new ReceivingDomainException(ReceivingErrorCodes.ValidationFailed);
        }

        if (!string.Equals(
                request.RuleSetVersion,
                ReceivingReleaseContract.RuleSetVersion,
                StringComparison.Ordinal))
        {
            throw new ReceivingDomainException(ReceivingErrorCodes.ReleaseApplicabilityUnknown);
        }
    }

    public static ReceivingReleaseEvaluation Evaluate(
        ReceivingReleaseIdentitySnapshot? identity,
        IReadOnlyList<ReceivingReleaseExceptionSnapshot> exceptions,
        DateTimeOffset now)
    {
        if (identity is null ||
            !string.Equals(identity.Outcome, IdentityDecisionOutcomes.Matched, StringComparison.Ordinal))
        {
            throw new ReceivingDomainException(ReceivingErrorCodes.IdentityNotMatched);
        }

        if (!string.Equals(
                identity.RuleSetVersion,
                IdentityAssessmentContract.RuleSetVersion,
                StringComparison.Ordinal))
        {
            throw new ReceivingDomainException(ReceivingErrorCodes.ReleaseApplicabilityUnknown);
        }

        if (exceptions.Count == 0)
        {
            return new ReceivingReleaseEvaluation(
                ReceivingReleaseOutcomes.Released,
                ReceivingReleaseStates.Accepted,
                KnownActions,
                [],
                null);
        }

        var allowed = new HashSet<string>(KnownActions, StringComparer.Ordinal);
        var prohibited = new HashSet<string>(StringComparer.Ordinal);
        DateTimeOffset? validUntil = null;

        foreach (var exception in exceptions)
        {
            if (exception.Status is ReceivingExceptionStatuses.Open or
                ReceivingExceptionStatuses.AwaitingCustomer or
                ReceivingExceptionStatuses.Rejected or
                ReceivingExceptionStatuses.SafetyHold)
            {
                throw new ReceivingDomainException(ReceivingErrorCodes.BlockingException);
            }

            if (!string.Equals(
                    exception.Status,
                    ReceivingExceptionStatuses.ConditionallyAccepted,
                    StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(exception.DecisionId) ||
                exception.DecisionVersion < 1 ||
                !string.Equals(
                    exception.DecisionType,
                    ReceivingExceptionDecisionTypes.ConditionalAccept,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    exception.MatrixVersion,
                    ReceivingReleaseContract.ExceptionMatrixVersion,
                    StringComparison.Ordinal) ||
                exception.ValidUntil is null || exception.ValidUntil <= now ||
                exception.AllowedActions.Count == 0 ||
                exception.AllowedActions.Any(action => !IsKnownAction(action)) ||
                exception.ProhibitedActions.Any(action => !IsKnownAction(action)))
            {
                throw new ReceivingDomainException(ReceivingErrorCodes.ReleaseApplicabilityUnknown);
            }

            allowed.IntersectWith(exception.AllowedActions);
            prohibited.UnionWith(exception.ProhibitedActions);
            validUntil = validUntil is null || exception.ValidUntil < validUntil
                ? exception.ValidUntil
                : validUntil;
        }

        allowed.ExceptWith(prohibited);
        if (allowed.Count == 0 || validUntil is null)
        {
            throw new ReceivingDomainException(ReceivingErrorCodes.ReleaseApplicabilityUnknown);
        }

        return new ReceivingReleaseEvaluation(
            ReceivingReleaseOutcomes.ReleasedWithConstraints,
            ReceivingReleaseStates.ConditionallyAccepted,
            OrderActions(allowed),
            OrderActions(prohibited),
            validUntil);
    }

    public static bool IsKnownAction(string action) => KnownActions.Contains(action, StringComparer.Ordinal);

    private static IReadOnlyList<string> OrderActions(IEnumerable<string> actions) =>
        KnownActions.Where(actions.Contains).ToArray();
}

internal sealed record ReceivingReleaseIdentitySnapshot(
    string DecisionId,
    long DecisionVersion,
    string Outcome,
    string RuleSetVersion);

internal sealed record ReceivingReleaseExceptionSnapshot(
    string ExceptionId,
    string Status,
    long ExceptionVersion,
    string? DecisionId,
    long? DecisionVersion,
    string? DecisionType,
    string? MatrixVersion,
    IReadOnlyList<string> AllowedActions,
    IReadOnlyList<string> ProhibitedActions,
    DateTimeOffset? ValidUntil);

internal sealed record ReceivingReleaseEvaluation(
    string Outcome,
    string State,
    IReadOnlyList<string> AllowedActions,
    IReadOnlyList<string> ProhibitedActions,
    DateTimeOffset? ConstraintsValidUntil);

internal sealed record ReceivingReleaseSnapshot(
    ReceivingReleaseDecisionResult Decision,
    string AssessmentState,
    string? CurrentIdentityDecisionId,
    long? CurrentIdentityDecisionVersion);
