using Npgsql;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Receiving;

namespace OpenLIMS.Modules.Receiving;

internal sealed class ReceiptRegistrationService(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IIdGenerator idGenerator,
    IReceivingAuthorizationPort authorizationPort,
    ITransactionCoordinator transactionCoordinator,
    ReceivingRegistrationStore store,
    ReceivingAttemptAuditWriter attemptAuditWriter) : IReceiptRegistrationService
{
    public async Task<ReceiptRegistrationResult> RegisterAsync(
        RegisterReceiptRequest request,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var organizationGroupId = organizationContext.Current.OrganizationGroupId;
        var actor = actorContext.Current;
        if (actor is null || !string.Equals(actor.OrganizationGroupId, organizationGroupId, StringComparison.Ordinal))
        {
            throw new ReceivingDomainException(ReceivingErrorCodes.AuthorizationDenied);
        }

        try
        {
            ReceivingRules.Validate(request, idempotencyKey, clock.UtcNow);
            var authorization = await authorizationPort.AuthorizeAsync(
                new ReceivingAuthorizationRequest(
                    organizationGroupId,
                    actor.ActorId,
                    request.LegalEntityId,
                    request.LaboratoryId,
                    request.CustomerId,
                    request.ServiceOrderId,
                    ReceivingCapabilities.Register),
                cancellationToken);

            if (authorization.Outcome == ReceivingAuthorizationOutcome.Denied)
            {
                throw new ReceivingDomainException(ReceivingErrorCodes.AuthorizationDenied);
            }

            if (authorization.Outcome == ReceivingAuthorizationOutcome.ServiceOrderNotReceivable)
            {
                throw new ReceivingDomainException(ReceivingErrorCodes.ServiceOrderNotReceivable);
            }

            var keyHash = ReceivingRules.Hash(idempotencyKey);
            var requestHash = ReceivingRules.RequestHash(request);
            ReceiptRegistrationResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var reservation = await store.ReserveIdempotencyAsync(
                    organizationGroupId,
                    actor.ActorId,
                    keyHash,
                    requestHash,
                    clock.UtcNow,
                    transactionToken);

                if (reservation.Kind == IdempotencyReservationKind.Conflict)
                {
                    throw new ReceivingDomainException(ReceivingErrorCodes.IdempotencyConflict);
                }

                if (reservation.Kind == IdempotencyReservationKind.Replay)
                {
                    result = reservation.Result ?? throw new InvalidOperationException("REC.IDEMPOTENCY_RESULT_MISSING");
                    return;
                }

                var plan = ReceivingRules.CreatePlan(
                    request,
                    idGenerator,
                    organizationGroupId,
                    actor.ActorId,
                    clock.UtcNow,
                    authorization.LaboratoryCode
                        ?? throw new ReceivingDomainException(ReceivingErrorCodes.AuthorizationDenied));
                result = await store.InsertRegistrationAsync(
                    plan,
                    keyHash,
                    correlationId,
                    transactionToken);
                await store.CompleteIdempotencyAsync(
                    organizationGroupId,
                    keyHash,
                    plan.Id,
                    result,
                    transactionToken);
            }, cancellationToken);

            return result ?? throw new InvalidOperationException("REC.REGISTRATION_RESULT_MISSING");
        }
        catch (ReceivingDomainException exception)
        {
            await WriteFailedAttemptOrFailClosedAsync(
                actor.ActorId,
                organizationGroupId,
                request.ServiceOrderId,
                correlationId,
                exception.ErrorCode,
                cancellationToken);
            throw;
        }
        catch (NpgsqlException)
        {
            await WriteFailedAttemptOrFailClosedAsync(
                actor.ActorId,
                organizationGroupId,
                request.ServiceOrderId,
                correlationId,
                ReceivingErrorCodes.PersistenceUnavailable,
                cancellationToken);
            throw new ReceivingDomainException(ReceivingErrorCodes.PersistenceUnavailable);
        }
    }

    private async Task WriteFailedAttemptOrFailClosedAsync(
        string actorId,
        string organizationGroupId,
        string serviceOrderId,
        string correlationId,
        string decisionCode,
        CancellationToken cancellationToken)
    {
        try
        {
            await attemptAuditWriter.WriteAsync(
                actorId,
                organizationGroupId,
                ReceivingRules.Hash(string.IsNullOrWhiteSpace(serviceOrderId) ? "unresolved-target" : serviceOrderId),
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
