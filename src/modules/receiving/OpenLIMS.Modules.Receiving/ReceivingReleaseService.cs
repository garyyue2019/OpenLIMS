using System.Text.Json;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Receiving;

namespace OpenLIMS.Modules.Receiving;

internal sealed class ReceivingReleaseService(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IReceivingAuthorizationPort authorizationPort,
    ITransactionCoordinator transactionCoordinator,
    ReceivingReleaseStore store,
    ReceivingAttemptAuditWriter attemptAuditWriter,
    ILogger<ReceivingReleaseService> logger) : IReceivingReleaseService
{
    public async Task<ReceivingReleaseDecisionResult> SubmitAsync(
        string receivedItemId,
        SubmitReceivingReleaseDecisionRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (organizationGroupId, actorId) = await RequireActorAsync(
            receivedItemId, correlationId, cancellationToken);
        try
        {
            ReceivingReleaseDecisionResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var item = await store.LoadItemAsync(
                    organizationGroupId, receivedItemId, true, transactionToken)
                    ?? throw new ReceivingDomainException(ReceivingErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(item, actorId, transactionToken);
                if (!string.Equals(item.CurrentState, "QUARANTINED", StringComparison.Ordinal) ||
                    item.ItemVersion != request.ExpectedItemVersion)
                {
                    throw new ReceivingDomainException(ReceivingErrorCodes.ExpectedVersionConflict);
                }

                ReceivingReleaseRules.ValidateRequest(request);
                var inputs = await store.LoadInputsAsync(item.ReceivedItemId, transactionToken);
                var evaluation = ReceivingReleaseRules.Evaluate(inputs.Identity, inputs.Exceptions, clock.UtcNow);
                result = await store.InsertAsync(
                    item,
                    inputs.Identity!,
                    inputs.Exceptions,
                    evaluation,
                    request,
                    actorId,
                    clock.UtcNow,
                    correlationId,
                    transactionToken);
            }, cancellationToken);
            var completed = result ?? throw new InvalidOperationException("REC.RELEASE_RESULT_MISSING");
            ReceivingReleaseTelemetry.RecordRelease(completed.Outcome, "RECORDED");
            return completed;
        }
        catch (Exception exception) when (exception is ReceivingDomainException or NpgsqlException)
        {
            var code = exception is ReceivingDomainException domain
                ? domain.ErrorCode
                : ReceivingErrorCodes.PersistenceUnavailable;
            ReceivingReleaseTelemetry.RecordRelease("BLOCKED", code);
            logger.LogWarning(
                "Receiving release failed closed with {ErrorCode}; correlation {CorrelationId}",
                code,
                correlationId);
            await WriteFailedAttemptOrFailClosedAsync(
                actorId,
                organizationGroupId,
                receivedItemId,
                correlationId,
                code,
                cancellationToken);
            throw new ReceivingDomainException(code);
        }
    }

    private async Task<(string OrganizationGroupId, string ActorId)> RequireActorAsync(
        string? target,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var organizationGroupId = organizationContext.Current.OrganizationGroupId;
        var actor = actorContext.Current;
        if (actor is not null &&
            string.Equals(actor.OrganizationGroupId, organizationGroupId, StringComparison.Ordinal))
        {
            return (organizationGroupId, actor.ActorId);
        }

        await WriteFailedAttemptOrFailClosedAsync(
            actor?.ActorId,
            organizationGroupId,
            target,
            correlationId,
            ReceivingErrorCodes.ReleaseNotAuthorized,
            cancellationToken);
        throw new ReceivingDomainException(ReceivingErrorCodes.ReleaseNotAuthorized);
    }

    private async Task AuthorizeAsync(
        IdentityItemScope item,
        string actorId,
        CancellationToken cancellationToken)
    {
        var decision = await authorizationPort.AuthorizeAsync(
            new ReceivingAuthorizationRequest(
                item.OrganizationGroupId,
                actorId,
                item.LegalEntityId,
                item.LaboratoryId,
                item.CustomerId,
                item.ServiceOrderId,
                ReceivingCapabilities.ReleaseApprove)
            {
                ProductCategory = item.ProductCategory
            }, cancellationToken);
        if (decision.Outcome != ReceivingAuthorizationOutcome.Allowed)
            throw new ReceivingDomainException(ReceivingErrorCodes.ReleaseNotAuthorized);
    }

    private async Task WriteFailedAttemptOrFailClosedAsync(
        string? actorId,
        string organizationGroupId,
        string? target,
        string correlationId,
        string code,
        CancellationToken cancellationToken)
    {
        try
        {
            await attemptAuditWriter.WriteAsync(
                "SubmitReceivingReleaseDecision",
                actorId,
                organizationGroupId,
                ReceivingRules.Hash(string.IsNullOrWhiteSpace(target) ? "unresolved-target" : target),
                correlationId,
                code,
                clock.UtcNow,
                cancellationToken);
        }
        catch (Exception auditException) when (auditException is NpgsqlException or InvalidOperationException)
        {
            throw new ReceivingDomainException(ReceivingErrorCodes.PersistenceUnavailable);
        }
    }
}

internal sealed class ReceivingEligibilityPortV2(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IReceivingAuthorizationPort authorizationPort,
    ITransactionCoordinator transactionCoordinator,
    ReceivingReleaseStore store,
    ILogger<ReceivingEligibilityPortV2> logger) : IReceivingEligibilityPortV2
{
    public async ValueTask<ReceivingEligibilityV2Result> EvaluateAsync(
        ReceivingEligibilityV2Request request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var organizationGroupId = organizationContext.Current.OrganizationGroupId;
        var actor = actorContext.Current;
        if (actor is null || !string.Equals(actor.OrganizationGroupId, organizationGroupId, StringComparison.Ordinal))
            throw new ReceivingDomainException(ReceivingErrorCodes.AuthorizationDenied);

        try
        {
            ReceivingEligibilityV2Result? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var item = await store.LoadItemAsync(
                    organizationGroupId, request.ReceivedItemId, false, transactionToken)
                    ?? throw new ReceivingDomainException(ReceivingErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(item, actor.ActorId, request.LaboratoryId, transactionToken);
                var release = await store.LoadCurrentReleaseAsync(item, transactionToken);
                result = EvaluateKnownState(request, item, release, clock.UtcNow);
                await store.WriteReadAuditAsync(
                    item,
                    actor.ActorId,
                    Guid.NewGuid().ToString("N"),
                    clock.UtcNow,
                    JsonSerializer.Serialize(new
                    {
                        request.RequestedAction,
                        request.ExpectedItemVersion,
                        request.RuleSetVersion,
                        result.Decision,
                        result.ReasonCodes,
                        result.ReleaseDecisionId,
                        result.ReleaseDecisionVersion
                    }, ReceivingJson.Options),
                    transactionToken);
            }, cancellationToken);
            var evaluated = result ?? Unknown(ReceivingEligibilityV2Reasons.ReceivingUnavailable);
            RecordGate(request, evaluated);
            return evaluated;
        }
        catch (NpgsqlException)
        {
            var unavailable = Unknown(ReceivingEligibilityV2Reasons.ReceivingUnavailable);
            RecordGate(request, unavailable);
            return unavailable;
        }
    }

    internal static ReceivingEligibilityV2Result EvaluateKnownState(
        ReceivingEligibilityV2Request request,
        IdentityItemScope item,
        ReceivingReleaseSnapshot? release,
        DateTimeOffset now)
    {
        if (!string.Equals(request.RuleSetVersion, ReceivingEligibilityV2Contract.RuleSetVersion, StringComparison.Ordinal))
            return UnknownFor(item, release, ReceivingEligibilityV2Reasons.RuleSetVersionUnknown);
        if (!ReceivingReleaseRules.IsKnownAction(request.RequestedAction))
            return UnknownFor(item, release, ReceivingEligibilityV2Reasons.RequestedActionUnknown);
        if (request.ExpectedItemVersion != item.ItemVersion)
            return UnknownFor(item, release, ReceivingEligibilityV2Reasons.ItemVersionMismatch);
        if (release is null)
            return BlockedFor(item, null, ReceivingEligibilityV2Reasons.ReleaseDecisionRequired);

        var decision = release.Decision;
        var pinnedInputsCurrent =
            decision.BoundItemVersion + 1 == item.ItemVersion &&
            string.Equals(decision.ReleaseRuleVersion, ReceivingReleaseContract.RuleSetVersion, StringComparison.Ordinal) &&
            string.Equals(decision.ExceptionMatrixVersion, ReceivingReleaseContract.ExceptionMatrixVersion, StringComparison.Ordinal) &&
            string.Equals(release.AssessmentState, IdentityAssessmentStates.Matched, StringComparison.Ordinal) &&
            string.Equals(release.CurrentIdentityDecisionId, decision.IdentityDecisionId, StringComparison.Ordinal) &&
            release.CurrentIdentityDecisionVersion == decision.IdentityDecisionVersion;
        if (!pinnedInputsCurrent)
            return UnknownFor(item, release, ReceivingEligibilityV2Reasons.ReleaseDecisionNotCurrent);

        if (string.Equals(decision.Outcome, ReceivingReleaseOutcomes.Released, StringComparison.Ordinal) &&
            string.Equals(item.CurrentState, ReceivingReleaseStates.Accepted, StringComparison.Ordinal))
        {
            return decision.ConstraintsValidUntil is null &&
                   decision.AllowedActions.Contains(request.RequestedAction, StringComparer.Ordinal) &&
                   !decision.ProhibitedActions.Contains(request.RequestedAction, StringComparer.Ordinal)
                ? AllowedFor(item, release)
                : UnknownFor(item, release, ReceivingEligibilityV2Reasons.ReleaseDecisionNotCurrent);
        }

        if (!string.Equals(decision.Outcome, ReceivingReleaseOutcomes.ReleasedWithConstraints, StringComparison.Ordinal) ||
            !string.Equals(item.CurrentState, ReceivingReleaseStates.ConditionallyAccepted, StringComparison.Ordinal))
        {
            return UnknownFor(item, release, ReceivingEligibilityV2Reasons.ReleaseDecisionNotCurrent);
        }

        if (decision.ConstraintsValidUntil is null || decision.ConstraintsValidUntil <= now)
            return BlockedFor(item, release, ReceivingEligibilityV2Reasons.ConstraintsExpired);

        return decision.AllowedActions.Contains(request.RequestedAction, StringComparer.Ordinal) &&
               !decision.ProhibitedActions.Contains(request.RequestedAction, StringComparer.Ordinal)
            ? AllowedFor(item, release)
            : BlockedFor(item, release, ReceivingEligibilityV2Reasons.ActionNotAllowed);
    }

    private async Task AuthorizeAsync(
        IdentityItemScope item,
        string actorId,
        string laboratoryId,
        CancellationToken cancellationToken)
    {
        var request = new ReceivingAuthorizationRequest(
            item.OrganizationGroupId,
            actorId,
            item.LegalEntityId,
            laboratoryId,
            item.CustomerId,
            item.ServiceOrderId,
            ReceivingCapabilities.EligibilityEvaluate)
        {
            ProductCategory = item.ProductCategory
        };
        if (!string.Equals(laboratoryId, item.LaboratoryId, StringComparison.Ordinal) ||
            (await authorizationPort.AuthorizeAsync(request, cancellationToken)).Outcome != ReceivingAuthorizationOutcome.Allowed)
        {
            throw new ReceivingDomainException(ReceivingErrorCodes.AuthorizationDenied);
        }
    }

    private static ReceivingEligibilityV2Result AllowedFor(
        IdentityItemScope item,
        ReceivingReleaseSnapshot release) =>
        Result(ReceivingEligibilityDecisions.Allowed, item, release, []);

    private static ReceivingEligibilityV2Result BlockedFor(
        IdentityItemScope item,
        ReceivingReleaseSnapshot? release,
        string reason) =>
        Result(ReceivingEligibilityDecisions.Blocked, item, release, [reason]);

    private static ReceivingEligibilityV2Result UnknownFor(
        IdentityItemScope item,
        ReceivingReleaseSnapshot? release,
        string reason) =>
        Result(ReceivingEligibilityDecisions.Unknown, item, release, [reason]);

    private static ReceivingEligibilityV2Result Result(
        string decision,
        IdentityItemScope item,
        ReceivingReleaseSnapshot? release,
        IReadOnlyList<string> reasons) => new(
        decision,
        item.CurrentState,
        release?.AssessmentState,
        release?.Decision.IdentityDecisionId,
        release?.Decision.ReleaseDecisionId,
        reasons,
        item.ItemVersion,
        release?.Decision.Version,
        ReceivingEligibilityV2Contract.RuleSetVersion,
        release?.Decision.AllowedActions ?? [],
        release?.Decision.ProhibitedActions ?? [],
        release?.Decision.ConstraintsValidUntil);

    private static ReceivingEligibilityV2Result Unknown(string reason) => new(
        ReceivingEligibilityDecisions.Unknown,
        null,
        null,
        null,
        null,
        [reason],
        null,
        null,
        ReceivingEligibilityV2Contract.RuleSetVersion,
        [],
        [],
        null);

    private void RecordGate(ReceivingEligibilityV2Request request, ReceivingEligibilityV2Result result)
    {
        ReceivingReleaseTelemetry.RecordGate(request.RequestedAction, result.Decision, result.CurrentState);
        if (string.Equals(result.Decision, ReceivingEligibilityDecisions.Unknown, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Receiving eligibility v2 failed closed for action {Action}; reason codes {ReasonCodes}",
                request.RequestedAction,
                string.Join(',', result.ReasonCodes));
        }
    }
}
