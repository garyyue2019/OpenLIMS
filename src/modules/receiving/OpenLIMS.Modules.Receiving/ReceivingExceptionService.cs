using Microsoft.Extensions.Logging;
using Npgsql;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Receiving;

namespace OpenLIMS.Modules.Receiving;

internal sealed class ReceivingExceptionService(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IReceivingAuthorizationPort authorizationPort,
    ITransactionCoordinator transactionCoordinator,
    ReceivingExceptionStore store,
    ReceivingAttemptAuditWriter attemptAuditWriter,
    ILogger<ReceivingExceptionService> logger) : IReceivingExceptionService
{
    public async Task<ReceivingExceptionResult> CreateAsync(
        CreateReceivingExceptionRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (organizationGroupId, actorId) = await RequireActorAsync(
            "CreateReceivingException", request.ReceivedItemId, correlationId, cancellationToken);
        try
        {
            ReceivingExceptionResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var item = await store.LoadItemAsync(
                    organizationGroupId, request.ReceivedItemId, true, transactionToken)
                    ?? throw new ReceivingDomainException(ReceivingErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(item, actorId, ReceivingCapabilities.ExceptionCreate, transactionToken);
                EnsureQuarantined(item);
                if (item.ItemVersion != request.ExpectedItemVersion)
                    throw new ReceivingDomainException(ReceivingErrorCodes.ExpectedVersionConflict);
                var severity = ReceivingExceptionRules.ValidateCreate(request, clock.UtcNow);
                var assessmentState = await store.LoadAssessmentStateAsync(item.ReceivedItemId, transactionToken);
                ReceivingExceptionRules.ValidateIdentityState(request.Type, assessmentState);
                result = await store.InsertAsync(
                    item, request, severity, actorId, clock.UtcNow, correlationId, transactionToken);
            }, cancellationToken);
            var completed = result ?? throw new InvalidOperationException("REC.EXCEPTION_RESULT_MISSING");
            ReceivingExceptionTelemetry.RecordCreated(completed.Type, completed.Severity);
            return completed;
        }
        catch (Exception exception) when (exception is ReceivingDomainException or NpgsqlException)
        {
            throw await RecordFailureAsync(
                "CreateReceivingException", actorId, organizationGroupId, request.ReceivedItemId,
                correlationId, exception, cancellationToken);
        }
    }

    public async Task<ReceivingExceptionResult> GetAsync(
        string exceptionId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(
            "GetReceivingException", exceptionId, correlationId, cancellationToken);
        try
        {
            ReceivingExceptionResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var scope = await store.LoadScopeAsync(
                    organizationGroupId, exceptionId, false, transactionToken)
                    ?? throw new ReceivingDomainException(ReceivingErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(scope.Item, actorId, ReceivingCapabilities.ExceptionRead, transactionToken);
                result = await store.LoadResultAsync(scope, transactionToken);
                await store.WriteReadAuditAsync(scope, actorId, correlationId, clock.UtcNow, transactionToken);
            }, cancellationToken);
            return result ?? throw new InvalidOperationException("REC.EXCEPTION_RESULT_MISSING");
        }
        catch (Exception exception) when (exception is ReceivingDomainException or NpgsqlException)
        {
            throw await RecordFailureAsync(
                "GetReceivingException", actorId, organizationGroupId, exceptionId,
                correlationId, exception, cancellationToken);
        }
    }

    public async Task<ReceivingExceptionResult> SubmitDecisionAsync(
        string exceptionId,
        SubmitReceivingExceptionDecisionRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(
            "SubmitReceivingExceptionDecision", exceptionId, correlationId, cancellationToken);
        try
        {
            ReceivingExceptionResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var scope = await store.LoadScopeAsync(
                    organizationGroupId, exceptionId, true, transactionToken)
                    ?? throw new ReceivingDomainException(ReceivingErrorCodes.ObjectNotAccessible);
                EnsureQuarantined(scope.Item);
                if (scope.Version != request.ExpectedVersion)
                    throw new ReceivingDomainException(ReceivingErrorCodes.ExpectedVersionConflict);
                var capability = ReceivingExceptionRules.ValidateDecision(
                    request, scope.Severity, scope.CreatedBy, actorId, clock.UtcNow);
                await AuthorizeAsync(scope.Item, actorId, capability, transactionToken);
                var status = ReceivingExceptionRules.StatusFor(request.DecisionType);
                result = await store.InsertDecisionAsync(
                    scope, request, status, actorId, clock.UtcNow, correlationId, transactionToken);
            }, cancellationToken);
            var completed = result ?? throw new InvalidOperationException("REC.EXCEPTION_RESULT_MISSING");
            ReceivingExceptionTelemetry.RecordDecision(request.DecisionType, "RECORDED");
            return completed;
        }
        catch (Exception exception) when (exception is ReceivingDomainException or NpgsqlException)
        {
            ReceivingExceptionTelemetry.RecordDecision(request.DecisionType ?? "UNKNOWN", "BLOCKED");
            throw await RecordFailureAsync(
                "SubmitReceivingExceptionDecision", actorId, organizationGroupId, exceptionId,
                correlationId, exception, cancellationToken);
        }
    }

    private async Task<(string OrganizationGroupId, string ActorId)> RequireActorAsync(
        string commandType,
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
            commandType, actor?.ActorId, organizationGroupId, target, correlationId,
            ReceivingErrorCodes.AuthorizationDenied, cancellationToken);
        throw new ReceivingDomainException(ReceivingErrorCodes.AuthorizationDenied);
    }

    private async Task AuthorizeAsync(
        IdentityItemScope item,
        string actorId,
        string capability,
        CancellationToken cancellationToken)
    {
        var decision = await authorizationPort.AuthorizeAsync(
            new ReceivingAuthorizationRequest(
                item.OrganizationGroupId, actorId, item.LegalEntityId, item.LaboratoryId,
                item.CustomerId, item.ServiceOrderId, capability)
            {
                ProductCategory = item.ProductCategory
            }, cancellationToken);
        if (decision.Outcome != ReceivingAuthorizationOutcome.Allowed)
            throw new ReceivingDomainException(ReceivingErrorCodes.DecisionNotAuthorized);
    }

    private static void EnsureQuarantined(IdentityItemScope item)
    {
        if (!string.Equals(item.CurrentState, "QUARANTINED", StringComparison.Ordinal))
            throw new ReceivingDomainException(ReceivingErrorCodes.ReceivingPortUnavailable);
    }

    private async Task<ReceivingDomainException> RecordFailureAsync(
        string commandType,
        string actorId,
        string organizationGroupId,
        string? target,
        string correlationId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var code = exception is ReceivingDomainException domain
            ? domain.ErrorCode
            : ReceivingErrorCodes.PersistenceUnavailable;
        logger.LogWarning(
            "Receiving exception command {CommandType} failed closed with {ErrorCode}; correlation {CorrelationId}",
            commandType, code, correlationId);
        await WriteFailedAttemptOrFailClosedAsync(
            commandType, actorId, organizationGroupId, target, correlationId, code, cancellationToken);
        return new ReceivingDomainException(code);
    }

    private async Task WriteFailedAttemptOrFailClosedAsync(
        string commandType,
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
                commandType, actorId, organizationGroupId,
                ReceivingRules.Hash(string.IsNullOrWhiteSpace(target) ? "unresolved-target" : target),
                correlationId, code, clock.UtcNow, cancellationToken);
        }
        catch (Exception auditException) when (auditException is NpgsqlException or InvalidOperationException)
        {
            throw new ReceivingDomainException(ReceivingErrorCodes.PersistenceUnavailable);
        }
    }
}
