using Microsoft.Extensions.Logging;
using Npgsql;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Scope;

namespace OpenLIMS.Modules.Scope;

public interface IScopeMatrixService
{
    Task<ScopeMatrixVersionResult> CreateAsync(
        SubmitScopeMatrixVersionRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<ScopeMatrixVersionResult> ReviseAsync(
        string scopeMatrixId,
        SubmitScopeMatrixVersionRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<ScopeMatrixVersionResult> GetVersionAsync(
        string scopeMatrixId,
        long version,
        string correlationId,
        CancellationToken cancellationToken = default);
}

internal sealed class ScopeMatrixService(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IIdGenerator idGenerator,
    IScopeAuthorizationPort authorizationPort,
    ITransactionCoordinator transactionCoordinator,
    ScopeStore store,
    ScopeAttemptAuditWriter attemptAuditWriter,
    ILogger<ScopeMatrixService> logger) : IScopeMatrixService
{
    public async Task<ScopeMatrixVersionResult> CreateAsync(
        SubmitScopeMatrixVersionRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var matrixId = Guid.Parse(idGenerator.NewId());
        var (organizationGroupId, actorId) = await RequireActorAsync(
            matrixId.ToString("N"), correlationId, cancellationToken);
        try
        {
            if (request is null || request.ExpectedCurrentVersion != 0)
                throw new ScopeDomainException(ScopeErrorCodes.ExpectedVersionConflict);
            var objectScope = ScopeRules.NormalizeObjectScope(request.ObjectScope);
            var lines = ScopeRules.ValidateAndNormalize(request);
            ScopeMatrixVersionResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await AuthorizeAsync(organizationGroupId, actorId, objectScope, transactionToken);
                result = await store.InsertAsync(
                    matrixId,
                    1,
                    organizationGroupId,
                    objectScope,
                    lines,
                    actorId,
                    clock.UtcNow,
                    correlationId,
                    transactionToken);
            }, cancellationToken);
            ScopeTelemetry.RecordApproved("initial");
            return result ?? throw new InvalidOperationException("SCP.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is ScopeDomainException or NpgsqlException)
        {
            throw await FailAsync(
                "CreateScopeMatrix",
                actorId,
                organizationGroupId,
                matrixId.ToString("N"),
                correlationId,
                exception,
                cancellationToken);
        }
    }

    public async Task<ScopeMatrixVersionResult> ReviseAsync(
        string scopeMatrixId,
        SubmitScopeMatrixVersionRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(
            scopeMatrixId, correlationId, cancellationToken);
        try
        {
            var matrixId = ParseMatrixId(scopeMatrixId);
            if (request is null || request.ExpectedCurrentVersion < 1)
                throw new ScopeDomainException(ScopeErrorCodes.ExpectedVersionConflict);
            var requestedScope = ScopeRules.NormalizeObjectScope(request.ObjectScope);
            var lines = ScopeRules.ValidateAndNormalize(request);
            ScopeMatrixVersionResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await store.AcquireMatrixLockAsync(matrixId, transactionToken);
                var current = await store.LoadCurrentHeaderAsync(
                    organizationGroupId, matrixId, false, transactionToken)
                    ?? throw new ScopeDomainException(ScopeErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(organizationGroupId, actorId, current.ObjectScope, transactionToken);
                if (current.ObjectScope != requestedScope)
                    throw new ScopeDomainException(ScopeErrorCodes.ValidationFailed);
                if (current.Version != request.ExpectedCurrentVersion)
                    throw new ScopeDomainException(ScopeErrorCodes.ExpectedVersionConflict);

                result = await store.InsertAsync(
                    matrixId,
                    current.Version + 1,
                    organizationGroupId,
                    current.ObjectScope,
                    lines,
                    actorId,
                    clock.UtcNow,
                    correlationId,
                    transactionToken);
            }, cancellationToken);
            ScopeTelemetry.RecordApproved("revision");
            return result ?? throw new InvalidOperationException("SCP.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is ScopeDomainException or NpgsqlException)
        {
            throw await FailAsync(
                "ReviseScopeMatrix",
                actorId,
                organizationGroupId,
                scopeMatrixId,
                correlationId,
                exception,
                cancellationToken);
        }
    }

    public async Task<ScopeMatrixVersionResult> GetVersionAsync(
        string scopeMatrixId,
        long version,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(
            scopeMatrixId, correlationId, cancellationToken);
        try
        {
            if (version < 1)
                throw new ScopeDomainException(ScopeErrorCodes.ValidationFailed);
            var matrixId = ParseMatrixId(scopeMatrixId);
            ScopeMatrixVersionResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                result = await store.LoadVersionAsync(
                    organizationGroupId, matrixId, version, transactionToken)
                    ?? throw new ScopeDomainException(ScopeErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(organizationGroupId, actorId, result.ObjectScope, transactionToken);
                await store.WriteReadAuditAsync(
                    result,
                    organizationGroupId,
                    actorId,
                    "READ_SCOPE_MATRIX_VERSION",
                    correlationId,
                    clock.UtcNow,
                    transactionToken);
            }, cancellationToken);
            return result ?? throw new InvalidOperationException("SCP.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is ScopeDomainException or NpgsqlException)
        {
            throw await FailAsync(
                "GetScopeMatrixVersion",
                actorId,
                organizationGroupId,
                scopeMatrixId,
                correlationId,
                exception,
                cancellationToken);
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

        await WriteAttemptOrFailClosedAsync(
            "ScopeCommand",
            actor?.ActorId,
            organizationGroupId,
            target,
            correlationId,
            ScopeErrorCodes.NotAuthorized,
            cancellationToken);
        throw new ScopeDomainException(ScopeErrorCodes.NotAuthorized);
    }

    private async Task AuthorizeAsync(
        string organizationGroupId,
        string actorId,
        ScopeObjectContext objectScope,
        CancellationToken cancellationToken)
    {
        var decision = await authorizationPort.AuthorizeAsync(new ScopeAuthorizationRequest(
            organizationGroupId,
            actorId,
            objectScope,
            ScopeCapabilities.Approve), cancellationToken);
        if (!decision.Allowed)
            throw new ScopeDomainException(ScopeErrorCodes.NotAuthorized);
    }

    private async Task<ScopeDomainException> FailAsync(
        string commandType,
        string actorId,
        string organizationGroupId,
        string? target,
        string correlationId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var code = exception is ScopeDomainException domain
            ? domain.ErrorCode
            : ScopeErrorCodes.PersistenceUnavailable;
        ScopeTelemetry.RecordRejected(code);
        logger.LogWarning(
            "Scope command {CommandType} failed closed with {ErrorCode}; correlation {CorrelationId}",
            commandType,
            code,
            correlationId);
        await WriteAttemptOrFailClosedAsync(
            commandType,
            actorId,
            organizationGroupId,
            target,
            correlationId,
            code,
            cancellationToken);
        return new ScopeDomainException(code);
    }

    private async Task WriteAttemptOrFailClosedAsync(
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
                commandType,
                actorId,
                organizationGroupId,
                ScopeRules.HashTarget(string.IsNullOrWhiteSpace(target) ? "unresolved-target" : target),
                correlationId,
                code,
                clock.UtcNow,
                cancellationToken);
        }
        catch (Exception auditException) when (auditException is NpgsqlException or InvalidOperationException)
        {
            throw new ScopeDomainException(ScopeErrorCodes.PersistenceUnavailable);
        }
    }

    private static Guid ParseMatrixId(string value) =>
        Guid.TryParseExact(value, "N", out var id) || Guid.TryParse(value, out id)
            ? id
            : throw new ScopeDomainException(ScopeErrorCodes.ObjectNotAccessible);
}

internal sealed class ScopeProductionEligibilityPort(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IScopeAuthorizationPort authorizationPort,
    ITransactionCoordinator transactionCoordinator,
    ScopeStore store,
    ScopeAttemptAuditWriter attemptAuditWriter,
    ILogger<ScopeProductionEligibilityPort> logger) : IScopeProductionEligibilityPort
{
    public async ValueTask<ScopeProductionEligibilityResult> EvaluateAsync(
        ScopeProductionEligibilityRequest request,
        CancellationToken cancellationToken = default)
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
            await WriteDeniedAsync(
                actor?.ActorId,
                organizationGroupId,
                request.ScopeMatrixId,
                correlationId,
                cancellationToken);
            throw new ScopeDomainException(ScopeErrorCodes.NotAuthorized);
        }

        if (!Guid.TryParseExact(request.ScopeMatrixId, "N", out var matrixId) &&
            !Guid.TryParse(request.ScopeMatrixId, out matrixId))
        {
            return Record(ScopeRules.Evaluate(request, null));
        }

        try
        {
            ScopeProductionEligibilityResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var current = await store.LoadCurrentAsync(
                    organizationGroupId, matrixId, false, transactionToken);
                if (current is null)
                {
                    result = ScopeRules.Evaluate(request, null);
                    return;
                }

                var authorization = await authorizationPort.AuthorizeAsync(new ScopeAuthorizationRequest(
                    organizationGroupId,
                    actor.ActorId,
                    current.ObjectScope,
                    ScopeCapabilities.Approve), transactionToken);
                if (!authorization.Allowed)
                    throw new ScopeDomainException(ScopeErrorCodes.NotAuthorized);

                result = ScopeRules.Evaluate(request, current);
                await store.WriteReadAuditAsync(
                    current,
                    organizationGroupId,
                    actor.ActorId,
                    "EVALUATE_SCOPE_PRODUCTION_ELIGIBILITY",
                    correlationId,
                    clock.UtcNow,
                    transactionToken);
            }, cancellationToken);
            return Record(result ?? ScopeRules.Evaluate(request, null));
        }
        catch (ScopeDomainException exception)
            when (string.Equals(exception.ErrorCode, ScopeErrorCodes.NotAuthorized, StringComparison.Ordinal))
        {
            await WriteDeniedAsync(
                actor.ActorId,
                organizationGroupId,
                request.ScopeMatrixId,
                correlationId,
                cancellationToken);
            throw;
        }
        catch (NpgsqlException)
        {
            logger.LogWarning("Scope production eligibility failed closed because persistence is unavailable");
            return Record(new ScopeProductionEligibilityResult(
                ScopeEligibilityDecisions.Unknown,
                [ScopeEligibilityReasons.ScopeUnavailable],
                request.ScopeMatrixId,
                null,
                ScopeContract.RuleSetVersion));
        }
    }

    private ScopeProductionEligibilityResult Record(ScopeProductionEligibilityResult result)
    {
        ScopeTelemetry.RecordGate(result.Decision);
        if (string.Equals(result.Decision, ScopeEligibilityDecisions.Unknown, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Scope production eligibility failed closed with reasons {ReasonCodes}",
                string.Join(',', result.ReasonCodes));
        }
        return result;
    }

    private async Task WriteDeniedAsync(
        string? actorId,
        string organizationGroupId,
        string target,
        string correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            await attemptAuditWriter.WriteAsync(
                "EvaluateScopeProductionEligibility",
                actorId,
                organizationGroupId,
                ScopeRules.HashTarget(target),
                correlationId,
                ScopeErrorCodes.NotAuthorized,
                clock.UtcNow,
                cancellationToken);
        }
        catch (NpgsqlException)
        {
            throw new ScopeDomainException(ScopeErrorCodes.PersistenceUnavailable);
        }
    }
}
