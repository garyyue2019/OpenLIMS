using Microsoft.Extensions.Logging;
using Npgsql;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Textile;

namespace OpenLIMS.Modules.Textile;

internal sealed class TextileRuntimeService(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    ITextileAuthorizationPort authorizationPort,
    ITransactionCoordinator transactionCoordinator,
    TextileStore store,
    TextileAttemptAuditWriter attemptAuditWriter,
    ILogger<TextileRuntimeService> logger) : ITextileRuntimeService
{
    public async Task<TextileSampleRequirementRecord> CalculateSampleRequirementAsync(
        CreateTextileSampleRequirementRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var target = request?.RequirementId ?? "unresolved-requirement";
        var (organizationGroupId, actorId) = await RequireActorAsync(
            "CalculateTextileSampleRequirement",
            target,
            correlationId,
            cancellationToken);
        try
        {
            if (request is null)
                throw new TextileOperationException(TextileErrorCodes.ValidationFailed);
            var draft = TextileRuntimeDomain.CalculateRequirement(request);
            TextileSampleRequirementRecord? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await store.AcquireRequirementLockAsync(
                    organizationGroupId,
                    request.RequirementId,
                    transactionToken);
                await AuthorizeAsync(
                    organizationGroupId,
                    actorId,
                    request.ObjectScope,
                    TextileCapabilities.SampleRequirementManage,
                    transactionToken);
                var currentVersion = await store.CurrentRequirementVersionAsync(
                    organizationGroupId,
                    request.RequirementId,
                    transactionToken);
                if (currentVersion != request.ExpectedCurrentVersion)
                    throw new TextileOperationException(TextileErrorCodes.ExpectedVersionConflict);
                result = await store.InsertRequirementAsync(
                    organizationGroupId,
                    request,
                    currentVersion + 1,
                    draft,
                    actorId,
                    clock.UtcNow,
                    correlationId,
                    transactionToken);
            }, cancellationToken);
            TextileTelemetry.RecordRequirement(draft.Result.Decision);
            return result ?? throw new InvalidOperationException("TEX.REQUIREMENT_RESULT_MISSING");
        }
        catch (Exception exception) when (
            exception is TextileOperationException or TextileContractException or NpgsqlException)
        {
            throw await FailAsync(
                "CalculateTextileSampleRequirement",
                actorId,
                organizationGroupId,
                target,
                correlationId,
                exception,
                cancellationToken);
        }
    }

    public async Task<TextileCuttingPlanResult> CreateCuttingPlanAsync(
        CreateTextileCuttingPlanRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var target = request?.CuttingPlanId ?? "unresolved-plan";
        var (organizationGroupId, actorId) = await RequireActorAsync(
            "CreateTextileCuttingPlan",
            target,
            correlationId,
            cancellationToken);
        try
        {
            if (request is null)
                throw new TextileOperationException(TextileErrorCodes.ValidationFailed);
            TextileCuttingPlanResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await store.AcquirePlanLockAsync(
                    organizationGroupId,
                    request.CuttingPlanId,
                    transactionToken);
                var requirement = await store.LoadRequirementAsync(
                    organizationGroupId,
                    request.SampleRequirementId,
                    request.SampleRequirementVersion,
                    transactionToken)
                    ?? throw new TextileOperationException(TextileErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(
                    organizationGroupId,
                    actorId,
                    requirement.ObjectScope,
                    TextileCapabilities.SampleRequirementManage,
                    transactionToken);
                TextileRuntimeDomain.ValidatePlan(request, requirement);
                var currentVersion = await store.CurrentPlanVersionAsync(
                    organizationGroupId,
                    request.CuttingPlanId,
                    transactionToken);
                if (currentVersion != request.ExpectedCurrentVersion)
                    throw new TextileOperationException(TextileErrorCodes.ExpectedVersionConflict);
                result = await store.InsertPlanAsync(
                    organizationGroupId,
                    request,
                    currentVersion + 1,
                    requirement,
                    TextileRuntimeDomain.PlanInputHash(request),
                    actorId,
                    clock.UtcNow,
                    correlationId,
                    transactionToken);
            }, cancellationToken);
            TextileTelemetry.RecordPlan(TextileCuttingPlanStates.Draft);
            return result ?? throw new InvalidOperationException("TEX.CUTTING_PLAN_RESULT_MISSING");
        }
        catch (Exception exception) when (
            exception is TextileOperationException or TextileContractException or NpgsqlException)
        {
            throw await FailAsync(
                "CreateTextileCuttingPlan",
                actorId,
                organizationGroupId,
                target,
                correlationId,
                exception,
                cancellationToken);
        }
    }

    public async Task<TextileCuttingPlanResult> ApproveCuttingPlanAsync(
        string cuttingPlanId,
        long version,
        ApproveTextileCuttingPlanRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var target = string.IsNullOrWhiteSpace(cuttingPlanId) ? "unresolved-plan" : cuttingPlanId;
        var (organizationGroupId, actorId) = await RequireActorAsync(
            "ApproveTextileCuttingPlan",
            target,
            correlationId,
            cancellationToken);
        try
        {
            if (string.IsNullOrWhiteSpace(cuttingPlanId) || version < 1 || request is null)
                throw new TextileOperationException(TextileErrorCodes.ValidationFailed);
            TextileCuttingPlanResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await store.AcquirePlanLockAsync(
                    organizationGroupId,
                    cuttingPlanId,
                    transactionToken);
                var plan = await store.LoadPlanAsync(
                    organizationGroupId,
                    cuttingPlanId,
                    version,
                    transactionToken)
                    ?? throw new TextileOperationException(TextileErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(
                    organizationGroupId,
                    actorId,
                    plan.ObjectScope,
                    TextileCapabilities.SampleRequirementManage,
                    transactionToken);
                await AuthorizeAsync(
                    organizationGroupId,
                    actorId,
                    plan.ObjectScope,
                    TextileCapabilities.CuttingPlanApprove,
                    transactionToken);
                var currentVersion = await store.CurrentPlanVersionAsync(
                    organizationGroupId,
                    cuttingPlanId,
                    transactionToken);
                if (currentVersion != version)
                    throw new TextileOperationException(TextileErrorCodes.ExpectedVersionConflict);
                TextileRuntimeDomain.RequireApprovable(plan, request);
                await store.InsertApprovalAsync(
                    organizationGroupId,
                    plan,
                    request,
                    actorId,
                    clock.UtcNow,
                    correlationId,
                    transactionToken);
                result = await store.LoadPlanAsync(
                    organizationGroupId,
                    cuttingPlanId,
                    version,
                    transactionToken);
            }, cancellationToken);
            TextileTelemetry.RecordPlan(TextileCuttingPlanStates.Approved);
            return result ?? throw new InvalidOperationException("TEX.CUTTING_PLAN_RESULT_MISSING");
        }
        catch (Exception exception) when (
            exception is TextileOperationException or TextileContractException or NpgsqlException)
        {
            throw await FailAsync(
                "ApproveTextileCuttingPlan",
                actorId,
                organizationGroupId,
                target,
                correlationId,
                exception,
                cancellationToken);
        }
    }

    public async Task<TextileCuttingPlanResult> GetCuttingPlanAsync(
        string cuttingPlanId,
        long version,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var target = string.IsNullOrWhiteSpace(cuttingPlanId) ? "unresolved-plan" : cuttingPlanId;
        var (organizationGroupId, actorId) = await RequireActorAsync(
            "GetTextileCuttingPlan",
            target,
            correlationId,
            cancellationToken);
        try
        {
            if (string.IsNullOrWhiteSpace(cuttingPlanId) || version < 1)
                throw new TextileOperationException(TextileErrorCodes.ObjectNotAccessible);
            TextileCuttingPlanResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                result = await store.LoadPlanAsync(
                    organizationGroupId,
                    cuttingPlanId,
                    version,
                    transactionToken)
                    ?? throw new TextileOperationException(TextileErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(
                    organizationGroupId,
                    actorId,
                    result.ObjectScope,
                    TextileCapabilities.SampleRequirementManage,
                    transactionToken);
                await store.WriteReadAuditAsync(
                    result,
                    organizationGroupId,
                    actorId,
                    "READ_TEXTILE_CUTTING_PLAN",
                    correlationId,
                    clock.UtcNow,
                    transactionToken);
            }, cancellationToken);
            return result ?? throw new InvalidOperationException("TEX.CUTTING_PLAN_RESULT_MISSING");
        }
        catch (Exception exception) when (
            exception is TextileOperationException or TextileContractException or NpgsqlException)
        {
            throw await FailAsync(
                "GetTextileCuttingPlan",
                actorId,
                organizationGroupId,
                target,
                correlationId,
                exception,
                cancellationToken);
        }
    }

    private async Task<(string OrganizationGroupId, string ActorId)> RequireActorAsync(
        string commandType,
        string target,
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
            commandType,
            actor?.ActorId,
            organizationGroupId,
            target,
            correlationId,
            TextileErrorCodes.NotAuthorized,
            cancellationToken);
        throw new TextileOperationException(TextileErrorCodes.NotAuthorized);
    }

    private async Task AuthorizeAsync(
        string organizationGroupId,
        string actorId,
        TextileObjectScope objectScope,
        string capability,
        CancellationToken cancellationToken)
    {
        var decision = await authorizationPort.AuthorizeAsync(new TextileAuthorizationRequest(
            organizationGroupId,
            actorId,
            objectScope,
            capability), cancellationToken);
        if (!decision.Allowed)
            throw new TextileOperationException(TextileErrorCodes.NotAuthorized);
    }

    private async Task<TextileOperationException> FailAsync(
        string commandType,
        string actorId,
        string organizationGroupId,
        string target,
        string correlationId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var code = exception switch
        {
            TextileOperationException operation => operation.ErrorCode,
            TextileContractException contract => contract.ErrorCode,
            PostgresException { SqlState: "23505" } => TextileErrorCodes.ExpectedVersionConflict,
            _ => TextileErrorCodes.PersistenceUnavailable
        };
        TextileTelemetry.RecordRejected(code);
        logger.LogWarning(
            "Textile command {CommandType} failed closed with {ErrorCode}; correlation {CorrelationId}",
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
        return new TextileOperationException(code, exception);
    }

    private async Task WriteAttemptOrFailClosedAsync(
        string commandType,
        string? actorId,
        string organizationGroupId,
        string target,
        string correlationId,
        string outcome,
        CancellationToken cancellationToken)
    {
        try
        {
            await attemptAuditWriter.WriteAsync(
                commandType,
                actorId,
                organizationGroupId,
                target,
                correlationId,
                outcome,
                clock.UtcNow,
                cancellationToken);
        }
        catch (Exception auditException) when (
            auditException is NpgsqlException or InvalidOperationException)
        {
            throw new TextileOperationException(TextileErrorCodes.PersistenceUnavailable, auditException);
        }
    }
}

internal sealed class TextileCuttingPlanStatusPort(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    ITextileAuthorizationPort authorizationPort,
    ITransactionCoordinator transactionCoordinator,
    TextileStore store,
    TextileAttemptAuditWriter attemptAuditWriter,
    ILogger<TextileCuttingPlanStatusPort> logger) : ITextileCuttingPlanStatusPort
{
    public async ValueTask<TextileCuttingPlanStatusDecision> EvaluateAsync(
        TextileCuttingPlanStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var organizationGroupId = organizationContext.Current.OrganizationGroupId;
        var actor = actorContext.Current;
        var correlationId = Guid.NewGuid().ToString("N");
        if (actor is null ||
            !string.Equals(actor.OrganizationGroupId, organizationGroupId, StringComparison.Ordinal) ||
            !string.Equals(request.OrganizationGroupId, organizationGroupId, StringComparison.Ordinal))
        {
            await WriteDeniedAsync(
                actor?.ActorId,
                organizationGroupId,
                request.CuttingPlanId,
                correlationId,
                cancellationToken);
            throw new TextileOperationException(TextileErrorCodes.NotAuthorized);
        }

        if (string.IsNullOrWhiteSpace(request.CuttingPlanId) || request.Version < 1)
            return TextileRuntimeDomain.EvaluateStatus(null, request.RuleSetVersion);

        try
        {
            TextileCuttingPlanStatusDecision? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var plan = await store.LoadPlanAsync(
                    organizationGroupId,
                    request.CuttingPlanId,
                    request.Version,
                    transactionToken);
                if (plan is null)
                {
                    result = TextileRuntimeDomain.EvaluateStatus(null, request.RuleSetVersion);
                    return;
                }

                var authorization = await authorizationPort.AuthorizeAsync(
                    new TextileAuthorizationRequest(
                        organizationGroupId,
                        actor.ActorId,
                        plan.ObjectScope,
                        TextileCapabilities.SampleRequirementManage),
                    transactionToken);
                if (!authorization.Allowed)
                    throw new TextileOperationException(TextileErrorCodes.NotAuthorized);
                result = TextileRuntimeDomain.EvaluateStatus(plan, request.RuleSetVersion);
                await store.WriteReadAuditAsync(
                    plan,
                    organizationGroupId,
                    actor.ActorId,
                    "EVALUATE_TEXTILE_CUTTING_PLAN",
                    correlationId,
                    clock.UtcNow,
                    transactionToken);
            }, cancellationToken);
            return result ?? TextileRuntimeDomain.EvaluateStatus(null, request.RuleSetVersion);
        }
        catch (TextileOperationException exception)
            when (string.Equals(
                exception.ErrorCode,
                TextileErrorCodes.NotAuthorized,
                StringComparison.Ordinal))
        {
            await WriteDeniedAsync(
                actor.ActorId,
                organizationGroupId,
                request.CuttingPlanId,
                correlationId,
                cancellationToken);
            throw;
        }
        catch (NpgsqlException)
        {
            logger.LogWarning(
                "Textile cutting plan status failed closed because persistence is unavailable");
            return TextileRuntimeDomain.EvaluateStatus(null, request.RuleSetVersion);
        }
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
                "EvaluateTextileCuttingPlan",
                actorId,
                organizationGroupId,
                target,
                correlationId,
                TextileErrorCodes.NotAuthorized,
                clock.UtcNow,
                cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            throw new TextileOperationException(TextileErrorCodes.PersistenceUnavailable, exception);
        }
    }
}
