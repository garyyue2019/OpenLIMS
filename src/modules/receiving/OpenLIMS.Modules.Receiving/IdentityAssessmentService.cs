using System.Text.Json;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Receiving;

namespace OpenLIMS.Modules.Receiving;

internal sealed class IdentityAssessmentService(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IReceivingAuthorizationPort authorizationPort,
    ITransactionCoordinator transactionCoordinator,
    IdentityAssessmentStore store,
    ReceivingAttemptAuditWriter attemptAuditWriter,
    ILogger<IdentityAssessmentService> logger) : IIdentityAssessmentService
{
    public Task<IdentityAssessmentResult> GetAsync(
        string receivedItemId,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            "GetIdentityAssessment",
            receivedItemId,
            ReceivingCapabilities.IdentityEvaluate,
            false,
            async (item, actorId, transactionToken) =>
            {
                var result = await store.LoadAssessmentAsync(item, transactionToken);
                await store.WriteReadAuditAsync(
                    item,
                    actorId,
                    "IDENTITY_ASSESSMENT_VIEWED",
                    correlationId,
                    clock.UtcNow,
                    JsonSerializer.Serialize(new
                    {
                        itemVersion = result.ItemVersion,
                        assessmentState = result.AssessmentState,
                        assessmentVersion = result.AssessmentVersion
                    }, ReceivingJson.Options),
                    transactionToken);
                return result;
            },
            correlationId,
            cancellationToken);

    public Task<IdentityAssessmentResult> AddObservationAsync(
        string receivedItemId,
        CreateIdentityObservationRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            "CreateIdentityObservation",
            receivedItemId,
            ReceivingCapabilities.IdentityEvaluate,
            true,
            (item, actorId, transactionToken) =>
            {
                IdentityAssessmentRules.ValidateObservation(request);
                EnsureExpectedVersion(item, request.ExpectedItemVersion);
                EnsureQuarantined(item);
                return store.InsertObservationAsync(
                    item,
                    request,
                    actorId,
                    clock.UtcNow,
                    correlationId,
                    transactionToken);
            },
            correlationId,
            cancellationToken);
    }

    public async Task<IdentityAssessmentResult> SubmitDecisionAsync(
        string receivedItemId,
        SubmitIdentityDecisionRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync(
            "SubmitIdentityDecision",
            receivedItemId,
            ReceivingCapabilities.IdentityEvaluate,
            true,
            async (item, actorId, transactionToken) =>
            {
                EnsureExpectedVersion(item, request.ExpectedItemVersion);
                EnsureQuarantined(item);
                var evidence = await store.LoadDecisionEvidenceAsync(
                    item.ReceivedItemId,
                    request.DeclarationSnapshotVersion,
                    request.ObservationVersion,
                    transactionToken);
                if (evidence is null)
                {
                    throw new ReceivingDomainException(ReceivingErrorCodes.IdentityEvidenceIncomplete);
                }

                IdentityAssessmentRules.ValidateDecision(request, evidence.Value.Declaration, evidence.Value.Observation);
                return await store.InsertDecisionAsync(
                    item,
                    request,
                    actorId,
                    clock.UtcNow,
                    correlationId,
                    transactionToken);
            },
            correlationId,
            cancellationToken);
        IdentityAssessmentTelemetry.RecordAssessment(request.Outcome);
        return result;
    }

    private async Task<IdentityAssessmentResult> ExecuteAsync(
        string commandType,
        string receivedItemId,
        string capability,
        bool forUpdate,
        Func<IdentityItemScope, string, CancellationToken, Task<IdentityAssessmentResult>> operation,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var organizationGroupId = organizationContext.Current.OrganizationGroupId;
        var actor = actorContext.Current;
        if (actor is null || !string.Equals(actor.OrganizationGroupId, organizationGroupId, StringComparison.Ordinal))
        {
            await WriteFailedAttemptOrFailClosedAsync(
                commandType,
                actor?.ActorId,
                organizationGroupId,
                receivedItemId,
                correlationId,
                ReceivingErrorCodes.AuthorizationDenied,
                cancellationToken);
            throw new ReceivingDomainException(ReceivingErrorCodes.AuthorizationDenied);
        }

        try
        {
            IdentityAssessmentResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var item = await store.LoadItemAsync(
                    organizationGroupId,
                    receivedItemId,
                    forUpdate,
                    transactionToken)
                    ?? throw new ReceivingDomainException(ReceivingErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(item, actor.ActorId, capability, transactionToken);
                result = await operation(item, actor.ActorId, transactionToken);
            }, cancellationToken);
            return result ?? throw new InvalidOperationException("REC.IDENTITY_RESULT_MISSING");
        }
        catch (ReceivingDomainException exception)
        {
            logger.LogWarning(
                "Identity assessment command {CommandType} failed closed with {ErrorCode}; correlation {CorrelationId}",
                commandType,
                exception.ErrorCode,
                correlationId);
            await WriteFailedAttemptOrFailClosedAsync(
                commandType,
                actor.ActorId,
                organizationGroupId,
                receivedItemId,
                correlationId,
                exception.ErrorCode,
                cancellationToken);
            throw;
        }
        catch (NpgsqlException)
        {
            logger.LogWarning(
                "Identity assessment command {CommandType} rolled back because persistence was unavailable; correlation {CorrelationId}",
                commandType,
                correlationId);
            await WriteFailedAttemptOrFailClosedAsync(
                commandType,
                actor.ActorId,
                organizationGroupId,
                receivedItemId,
                correlationId,
                ReceivingErrorCodes.PersistenceUnavailable,
                cancellationToken);
            throw new ReceivingDomainException(ReceivingErrorCodes.PersistenceUnavailable);
        }
    }

    private async Task AuthorizeAsync(
        IdentityItemScope item,
        string actorId,
        string capability,
        CancellationToken cancellationToken)
    {
        var request = new ReceivingAuthorizationRequest(
            item.OrganizationGroupId,
            actorId,
            item.LegalEntityId,
            item.LaboratoryId,
            item.CustomerId,
            item.ServiceOrderId,
            capability)
        {
            ProductCategory = item.ProductCategory
        };
        var decision = await authorizationPort.AuthorizeAsync(request, cancellationToken);
        if (decision.Outcome != ReceivingAuthorizationOutcome.Allowed)
        {
            throw new ReceivingDomainException(ReceivingErrorCodes.AuthorizationDenied);
        }
    }

    private static void EnsureExpectedVersion(IdentityItemScope item, long expectedVersion)
    {
        if (expectedVersion < 1 || item.ItemVersion != expectedVersion)
        {
            throw new ReceivingDomainException(ReceivingErrorCodes.ExpectedVersionConflict);
        }
    }

    private static void EnsureQuarantined(IdentityItemScope item)
    {
        if (!string.Equals(item.CurrentState, "QUARANTINED", StringComparison.Ordinal))
        {
            throw new ReceivingDomainException(ReceivingErrorCodes.ReceivingPortUnavailable);
        }
    }

    private async Task WriteFailedAttemptOrFailClosedAsync(
        string commandType,
        string? actorId,
        string organizationGroupId,
        string receivedItemId,
        string correlationId,
        string decisionCode,
        CancellationToken cancellationToken)
    {
        try
        {
            await attemptAuditWriter.WriteAsync(
                commandType,
                actorId,
                organizationGroupId,
                ReceivingRules.Hash(string.IsNullOrWhiteSpace(receivedItemId) ? "unresolved-target" : receivedItemId),
                correlationId,
                decisionCode,
                clock.UtcNow,
                cancellationToken);
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException)
        {
            throw new ReceivingDomainException(ReceivingErrorCodes.PersistenceUnavailable);
        }
    }
}

internal sealed class ReceivingEligibilityPort(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IReceivingAuthorizationPort authorizationPort,
    ITransactionCoordinator transactionCoordinator,
    IdentityAssessmentStore store,
    ILogger<ReceivingEligibilityPort> logger) : IReceivingEligibilityPort
{
    public async ValueTask<ReceivingEligibilityResult> EvaluateAsync(
        ReceivingEligibilityRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var organizationGroupId = organizationContext.Current.OrganizationGroupId;
        var actor = actorContext.Current;
        if (actor is null || !string.Equals(actor.OrganizationGroupId, organizationGroupId, StringComparison.Ordinal))
        {
            throw new ReceivingDomainException(ReceivingErrorCodes.AuthorizationDenied);
        }

        try
        {
            ReceivingEligibilityResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var item = await store.LoadItemAsync(
                    organizationGroupId,
                    request.ReceivedItemId,
                    false,
                    transactionToken)
                    ?? throw new ReceivingDomainException(ReceivingErrorCodes.ObjectNotAccessible);
                var authorizationRequest = new ReceivingAuthorizationRequest(
                    organizationGroupId,
                    actor.ActorId,
                    item.LegalEntityId,
                    request.LaboratoryId,
                    item.CustomerId,
                    item.ServiceOrderId,
                    ReceivingCapabilities.EligibilityEvaluate)
                {
                    ProductCategory = item.ProductCategory
                };
                if (!string.Equals(request.LaboratoryId, item.LaboratoryId, StringComparison.Ordinal) ||
                    (await authorizationPort.AuthorizeAsync(authorizationRequest, transactionToken)).Outcome != ReceivingAuthorizationOutcome.Allowed)
                {
                    throw new ReceivingDomainException(ReceivingErrorCodes.AuthorizationDenied);
                }

                var assessment = await store.LoadAssessmentAsync(item, transactionToken);
                result = EvaluateKnownState(request, assessment);
                await store.WriteReadAuditAsync(
                    item,
                    actor.ActorId,
                    "RECEIVING_ELIGIBILITY_EVALUATED",
                    Guid.NewGuid().ToString("N"),
                    clock.UtcNow,
                    JsonSerializer.Serialize(new
                    {
                        request.RequestedAction,
                        request.ExpectedItemVersion,
                        request.RuleSetVersion,
                        result.Decision,
                        result.ReasonCodes,
                        assessment.AssessmentVersion
                    }, ReceivingJson.Options),
                    transactionToken);
            }, cancellationToken);
            var evaluated = result ?? Unknown(ReceivingEligibilityReasons.ReceivingUnavailable);
            RecordGate(request, evaluated, logger);
            return evaluated;
        }
        catch (NpgsqlException)
        {
            var unavailable = Unknown(ReceivingEligibilityReasons.ReceivingUnavailable);
            RecordGate(request, unavailable, logger);
            return unavailable;
        }
    }

    private static ReceivingEligibilityResult EvaluateKnownState(
        ReceivingEligibilityRequest request,
        IdentityAssessmentResult assessment)
    {
        string? unknownReason = null;
        if (!string.Equals(request.RuleSetVersion, IdentityAssessmentContract.RuleSetVersion, StringComparison.Ordinal))
        {
            unknownReason = ReceivingEligibilityReasons.RuleSetVersionUnknown;
        }
        else if (!IdentityAssessmentRules.IsKnownEligibilityAction(request.RequestedAction))
        {
            unknownReason = ReceivingEligibilityReasons.RequestedActionUnknown;
        }
        else if (request.ExpectedItemVersion != assessment.ItemVersion)
        {
            unknownReason = ReceivingEligibilityReasons.ItemVersionMismatch;
        }
        else if (!string.Equals(assessment.CurrentState, "QUARANTINED", StringComparison.Ordinal))
        {
            unknownReason = ReceivingEligibilityReasons.ReceivingUnavailable;
        }

        var hasCurrentDecision = assessment.AssessmentState is
            IdentityAssessmentStates.Matched or
            IdentityAssessmentStates.Mismatched or
            IdentityAssessmentStates.Indeterminate;
        var latestDecision = hasCurrentDecision ? assessment.Decisions.LastOrDefault() : null;
        return unknownReason is not null
            ? new ReceivingEligibilityResult(
                ReceivingEligibilityDecisions.Unknown,
                assessment.CurrentState,
                assessment.AssessmentState,
                latestDecision?.DecisionId,
                [unknownReason],
                assessment.ItemVersion,
                latestDecision?.Version,
                IdentityAssessmentContract.RuleSetVersion)
            : new ReceivingEligibilityResult(
                ReceivingEligibilityDecisions.Blocked,
                assessment.CurrentState,
                assessment.AssessmentState,
                latestDecision?.DecisionId,
                [ReceivingEligibilityReasons.ReleaseDecisionRequired],
                assessment.ItemVersion,
                latestDecision?.Version,
                IdentityAssessmentContract.RuleSetVersion);
    }

    private static ReceivingEligibilityResult Unknown(string reason) => new(
        ReceivingEligibilityDecisions.Unknown,
        null,
        null,
        null,
        [reason],
        null,
        null,
        IdentityAssessmentContract.RuleSetVersion);

    private static void RecordGate(
        ReceivingEligibilityRequest request,
        ReceivingEligibilityResult result,
        ILogger logger)
    {
        IdentityAssessmentTelemetry.RecordGate(request.RequestedAction, result.Decision, result.AssessmentState);
        if (string.Equals(result.Decision, ReceivingEligibilityDecisions.Unknown, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Receiving eligibility failed closed for action {Action}; reason codes {ReasonCodes}",
                request.RequestedAction,
                string.Join(',', result.ReasonCodes));
        }
    }
}
