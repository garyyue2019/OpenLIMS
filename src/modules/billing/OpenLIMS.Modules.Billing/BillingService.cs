using Microsoft.Extensions.Logging;
using Npgsql;
using OpenLIMS.Contracts.Billing;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Result;

namespace OpenLIMS.Modules.Billing;

public interface IBillingEvidenceService
{
    Task<BillingEvidenceResult> CreateAsync(CreateBillingEvidenceRequest request, string correlationId, CancellationToken cancellationToken = default);
    Task<BillingAdjustmentResult> AddAdjustmentAsync(string billingEvidenceId, AddBillingAdjustmentRequest request, string correlationId, CancellationToken cancellationToken = default);
    Task<BillingEvidenceResult> GetAsync(string billingEvidenceId, string correlationId, CancellationToken cancellationToken = default);
}

internal sealed class BillingEvidenceService(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IIdGenerator idGenerator,
    IBillingAuthorizationPort authorizationPort,
    IResultAdoptionPort resultAdoptionPort,
    ITransactionCoordinator transactionCoordinator,
    BillingStore store,
    BillingAttemptAuditWriter attemptAuditWriter,
    ILogger<BillingEvidenceService> logger) : IBillingEvidenceService
{
    public async Task<BillingEvidenceResult> CreateAsync(
        CreateBillingEvidenceRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        var evidenceId = Guid.Parse(idGenerator.NewId());
        var (organizationGroupId, actorId) = await RequireActorAsync(evidenceId.ToString("N"), correlationId, cancellationToken);
        try
        {
            var validated = BillingRules.ValidateEvidence(request);
            var adoptionTargetId = await EvaluateAdoptionGateAsync(
                organizationGroupId, validated, correlationId, cancellationToken);
            BillingEvidenceResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await AuthorizeAsync(organizationGroupId, actorId, validated.ObjectScope, transactionToken);
                if (await store.DuplicateExistsAsync(organizationGroupId, validated, adoptionTargetId, transactionToken))
                    throw new BillingDomainException(BillingErrorCodes.DuplicateBilling);
                result = await store.InsertEvidenceAsync(
                    evidenceId, organizationGroupId, validated, adoptionTargetId,
                    actorId, clock.UtcNow, correlationId, transactionToken);
            }, cancellationToken);
            BillingTelemetry.RecordEvidence(validated.Amount == 0);
            return result ?? throw new InvalidOperationException("BIL.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is BillingDomainException or NpgsqlException)
        {
            throw await FailAsync("CreateBillingEvidence", actorId, organizationGroupId,
                evidenceId.ToString("N"), correlationId, exception, cancellationToken);
        }
    }

    public async Task<BillingAdjustmentResult> AddAdjustmentAsync(
        string billingEvidenceId, AddBillingAdjustmentRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(billingEvidenceId, correlationId, cancellationToken);
        try
        {
            var id = ParseEvidenceId(billingEvidenceId);
            var validated = BillingRules.ValidateAdjustment(request);
            BillingAdjustmentResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var evidence = await store.LoadEvidenceAsync(organizationGroupId, id, transactionToken)
                    ?? throw new BillingDomainException(BillingErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(organizationGroupId, actorId, evidence.ObjectScope, transactionToken);
                result = await store.InsertAdjustmentAsync(
                    id, organizationGroupId, validated, actorId, clock.UtcNow, correlationId, transactionToken);
            }, cancellationToken);
            BillingTelemetry.RecordAdjustment(request.Amount > 0);
            return result ?? throw new InvalidOperationException("BIL.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is BillingDomainException or NpgsqlException)
        {
            throw await FailAsync("AddBillingAdjustment", actorId, organizationGroupId,
                billingEvidenceId, correlationId, exception, cancellationToken);
        }
    }

    public async Task<BillingEvidenceResult> GetAsync(
        string billingEvidenceId, string correlationId, CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(billingEvidenceId, correlationId, cancellationToken);
        try
        {
            var id = ParseEvidenceId(billingEvidenceId);
            BillingEvidenceResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                result = await store.LoadEvidenceAsync(organizationGroupId, id, transactionToken)
                    ?? throw new BillingDomainException(BillingErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(organizationGroupId, actorId, result.ObjectScope, transactionToken);
                await store.WriteReadAuditAsync(
                    result.BillingEvidenceId, organizationGroupId, actorId,
                    "READ_BILLING_EVIDENCE", correlationId, clock.UtcNow, transactionToken);
            }, cancellationToken);
            return result ?? throw new InvalidOperationException("BIL.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is BillingDomainException or NpgsqlException)
        {
            throw await FailAsync("GetBillingEvidence", actorId, organizationGroupId,
                billingEvidenceId, correlationId, exception, cancellationToken);
        }
    }

    private async Task<string> EvaluateAdoptionGateAsync(
        string organizationGroupId,
        CreateBillingEvidenceRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ResultAdoptionStatusResult result;
        try
        {
            result = await resultAdoptionPort.EvaluateAsync(new ResultAdoptionStatusRequest(
                organizationGroupId, request.ResultGroupId, request.ExpectedGroupVersion,
                ResultContract.RuleSetVersion)
            {
                CorrelationId = correlationId
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            BillingTelemetry.RecordGate("UNKNOWN");
            throw new BillingDomainException(BillingErrorCodes.ApplicabilityUnknown, "RESULT");
        }

        BillingTelemetry.RecordGate(result.Decision);
        return result.Decision switch
        {
            ResultAdoptionDecisions.Allowed when result.EffectiveTargetId is not null => result.EffectiveTargetId,
            ResultAdoptionDecisions.Blocked => throw new BillingDomainException(BillingErrorCodes.EligibilityBlocked, "RESULT"),
            _ => throw new BillingDomainException(BillingErrorCodes.ApplicabilityUnknown, "RESULT")
        };
    }

    private async Task<(string OrganizationGroupId, string ActorId)> RequireActorAsync(
        string? target, string correlationId, CancellationToken cancellationToken)
    {
        var organizationGroupId = organizationContext.Current.OrganizationGroupId;
        var actor = actorContext.Current;
        if (actor is not null &&
            string.Equals(actor.OrganizationGroupId, organizationGroupId, StringComparison.Ordinal))
        {
            return (organizationGroupId, actor.ActorId);
        }

        await WriteAttemptOrFailClosedAsync("BillingCommand", actor?.ActorId, organizationGroupId,
            target, correlationId, BillingErrorCodes.NotAuthorized, cancellationToken);
        throw new BillingDomainException(BillingErrorCodes.NotAuthorized);
    }

    private async Task AuthorizeAsync(
        string organizationGroupId, string actorId, BillingObjectContext objectScope, CancellationToken cancellationToken)
    {
        var decision = await authorizationPort.AuthorizeAsync(new BillingAuthorizationRequest(
            organizationGroupId, actorId, objectScope, BillingCapabilities.Record), cancellationToken);
        if (!decision.Allowed)
            throw new BillingDomainException(BillingErrorCodes.NotAuthorized);
    }

    private async Task<BillingDomainException> FailAsync(
        string commandType, string actorId, string organizationGroupId,
        string? target, string correlationId, Exception exception, CancellationToken cancellationToken)
    {
        var (code, gateSource) = exception switch
        {
            BillingDomainException domain => (domain.ErrorCode, domain.GateSource),
            PostgresException { SqlState: "23505" } => (BillingErrorCodes.DuplicateBilling, null),
            _ => (BillingErrorCodes.PersistenceUnavailable, (string?)null)
        };
        BillingTelemetry.RecordRejected(code);
        logger.LogWarning(
            "Billing command {CommandType} failed closed with {ErrorCode} (gate {GateSource}); correlation {CorrelationId}",
            commandType, code, gateSource ?? "-", correlationId);
        await WriteAttemptOrFailClosedAsync(commandType, actorId, organizationGroupId,
            target, correlationId, code, cancellationToken);
        return new BillingDomainException(code, gateSource);
    }

    private async Task WriteAttemptOrFailClosedAsync(
        string commandType, string? actorId, string organizationGroupId,
        string? target, string correlationId, string code, CancellationToken cancellationToken)
    {
        try
        {
            await attemptAuditWriter.WriteAsync(commandType, actorId, organizationGroupId,
                BillingRules.HashTarget(string.IsNullOrWhiteSpace(target) ? "unresolved-target" : target),
                correlationId, code, clock.UtcNow, cancellationToken);
        }
        catch (Exception auditException) when (auditException is NpgsqlException or InvalidOperationException)
        {
            throw new BillingDomainException(BillingErrorCodes.PersistenceUnavailable);
        }
    }

    private static Guid ParseEvidenceId(string value) =>
        Guid.TryParseExact(value, "N", out var id) || Guid.TryParse(value, out id)
            ? id
            : throw new BillingDomainException(BillingErrorCodes.ObjectNotAccessible);
}

internal sealed class BillingEvidencePort(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IBillingAuthorizationPort authorizationPort,
    ITransactionCoordinator transactionCoordinator,
    BillingStore store,
    BillingAttemptAuditWriter attemptAuditWriter,
    ILogger<BillingEvidencePort> logger) : IBillingEvidencePort
{
    public async ValueTask<BillingEvidenceStatusResult> EvaluateAsync(
        BillingEvidenceStatusRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var organizationGroupId = organizationContext.Current.OrganizationGroupId;
        var actor = actorContext.Current;
        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : request.CorrelationId;
        if (actor is null ||
            !string.Equals(actor.OrganizationGroupId, organizationGroupId, StringComparison.Ordinal) ||
            !string.Equals(request.OrganizationGroupId, organizationGroupId, StringComparison.Ordinal))
        {
            await WriteDeniedAsync(actor?.ActorId, organizationGroupId, request.BillingEvidenceId, correlationId, cancellationToken);
            throw new BillingDomainException(BillingErrorCodes.NotAuthorized);
        }

        if (!Guid.TryParseExact(request.BillingEvidenceId, "N", out var evidenceId) &&
            !Guid.TryParse(request.BillingEvidenceId, out evidenceId))
        {
            return Record(BillingRules.EvaluateStatus(request, null));
        }

        try
        {
            BillingEvidenceStatusResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var evidence = await store.LoadEvidenceAsync(organizationGroupId, evidenceId, transactionToken);
                if (evidence is null)
                {
                    result = BillingRules.EvaluateStatus(request, null);
                    return;
                }

                var authorization = await authorizationPort.AuthorizeAsync(new BillingAuthorizationRequest(
                    organizationGroupId, actor.ActorId, evidence.ObjectScope, BillingCapabilities.Record), transactionToken);
                if (!authorization.Allowed)
                    throw new BillingDomainException(BillingErrorCodes.NotAuthorized);

                result = BillingRules.EvaluateStatus(request, evidence);
                await store.WriteReadAuditAsync(
                    evidence.BillingEvidenceId, organizationGroupId, actor.ActorId,
                    "EVALUATE_BILLING_EVIDENCE", correlationId, clock.UtcNow, transactionToken);
            }, cancellationToken);
            return Record(result ?? BillingRules.EvaluateStatus(request, null));
        }
        catch (BillingDomainException exception)
            when (string.Equals(exception.ErrorCode, BillingErrorCodes.NotAuthorized, StringComparison.Ordinal))
        {
            await WriteDeniedAsync(actor.ActorId, organizationGroupId, request.BillingEvidenceId, correlationId, cancellationToken);
            throw;
        }
        catch (NpgsqlException)
        {
            logger.LogWarning("Billing evidence status failed closed because persistence is unavailable");
            return Record(new BillingEvidenceStatusResult(
                BillingStatusDecisions.Unknown,
                [BillingStatusReasons.BillingUnavailable],
                request.BillingEvidenceId, null, null, null, BillingContract.RuleSetVersion));
        }
    }

    private BillingEvidenceStatusResult Record(BillingEvidenceStatusResult result)
    {
        BillingTelemetry.RecordGate(result.Decision);
        if (string.Equals(result.Decision, BillingStatusDecisions.Unknown, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Billing evidence status failed closed with reasons {ReasonCodes}",
                string.Join(',', result.ReasonCodes));
        }
        return result;
    }

    private async Task WriteDeniedAsync(
        string? actorId, string organizationGroupId, string target, string correlationId, CancellationToken cancellationToken)
    {
        try
        {
            await attemptAuditWriter.WriteAsync("EvaluateBillingEvidence", actorId, organizationGroupId,
                BillingRules.HashTarget(target), correlationId, BillingErrorCodes.NotAuthorized,
                clock.UtcNow, cancellationToken);
        }
        catch (NpgsqlException)
        {
            throw new BillingDomainException(BillingErrorCodes.PersistenceUnavailable);
        }
    }
}
