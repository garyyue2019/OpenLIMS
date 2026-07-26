using Microsoft.Extensions.Logging;
using Npgsql;
using OpenLIMS.Contracts.Allocation;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Quantity;
using OpenLIMS.Contracts.Receiving;
using OpenLIMS.Contracts.Scope;

namespace OpenLIMS.Modules.Allocation;

public interface ITestObjectAllocationService
{
    Task<TestObjectAllocationResult> CreateAsync(
        CreateTestObjectAllocationRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<AllocationReleaseResult> ReleaseAsync(
        string allocationId,
        ReleaseTestObjectAllocationRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<TestObjectAllocationResult> GetAsync(
        string allocationId,
        string correlationId,
        CancellationToken cancellationToken = default);
}

internal sealed class TestObjectAllocationService(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IIdGenerator idGenerator,
    IAllocationAuthorizationPort authorizationPort,
    IReceivingEligibilityPortV2 receivingEligibilityPort,
    IScopeProductionEligibilityPort scopeEligibilityPort,
    IQuantityAvailabilityPort quantityAvailabilityPort,
    ITransactionCoordinator transactionCoordinator,
    AllocationStore store,
    AllocationAttemptAuditWriter attemptAuditWriter,
    ILogger<TestObjectAllocationService> logger) : ITestObjectAllocationService
{
    public async Task<TestObjectAllocationResult> CreateAsync(
        CreateTestObjectAllocationRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var allocationId = Guid.Parse(idGenerator.NewId());
        var (organizationGroupId, actorId) = await RequireActorAsync(
            allocationId.ToString("N"), correlationId, cancellationToken);
        try
        {
            var validated = AllocationRules.ValidateRequest(request, clock.UtcNow);

            var (receivingGate, scopeGate, quantityGate, availableAmount) = await EvaluateGatesAsync(
                organizationGroupId, validated, correlationId, cancellationToken);

            TestObjectAllocationResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await AuthorizeAsync(organizationGroupId, actorId, validated.ObjectScope, transactionToken);
                await store.AcquireSubjectLockAsync(
                    organizationGroupId,
                    validated.Subject.SubjectType,
                    validated.Subject.Id,
                    transactionToken);
                var subjectState = await store.LoadSubjectStateAsync(
                    organizationGroupId,
                    validated.Subject.SubjectType,
                    validated.Subject.Id,
                    transactionToken);
                AllocationRules.RequirePostable(validated.ExpectedCurrentVersion, subjectState);
                result = await store.InsertAllocationAsync(
                    allocationId,
                    subjectState.CurrentVersion + 1,
                    organizationGroupId,
                    validated,
                    receivingGate,
                    scopeGate,
                    quantityGate,
                    availableAmount,
                    actorId,
                    clock.UtcNow,
                    correlationId,
                    transactionToken);
            }, cancellationToken);
            AllocationTelemetry.RecordAssigned(validated.Destructive);
            return result ?? throw new InvalidOperationException("ALC.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is AllocationDomainException or NpgsqlException)
        {
            throw await FailAsync(
                "CreateTestObjectAllocation",
                actorId,
                organizationGroupId,
                allocationId.ToString("N"),
                correlationId,
                exception,
                cancellationToken);
        }
    }

    public async Task<AllocationReleaseResult> ReleaseAsync(
        string allocationId,
        ReleaseTestObjectAllocationRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(
            allocationId, correlationId, cancellationToken);
        try
        {
            var id = ParseAllocationId(allocationId);
            var reason = AllocationRules.Text(request?.Reason);
            AllocationReleaseResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var allocation = await store.LoadAllocationAsync(organizationGroupId, id, transactionToken)
                    ?? throw new AllocationDomainException(AllocationErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(organizationGroupId, actorId, allocation.ObjectScope, transactionToken);
                await store.AcquireSubjectLockAsync(
                    organizationGroupId,
                    allocation.Subject.SubjectType,
                    allocation.Subject.Id,
                    transactionToken);
                if (string.Equals(allocation.State, AllocationStates.Released, StringComparison.Ordinal))
                    throw new AllocationDomainException(AllocationErrorCodes.ValidationFailed);
                var subjectState = await store.LoadSubjectStateAsync(
                    organizationGroupId,
                    allocation.Subject.SubjectType,
                    allocation.Subject.Id,
                    transactionToken);
                result = await store.InsertReleaseAsync(
                    allocation,
                    subjectState.CurrentVersion + 1,
                    organizationGroupId,
                    reason,
                    actorId,
                    clock.UtcNow,
                    correlationId,
                    transactionToken);
            }, cancellationToken);
            return result ?? throw new InvalidOperationException("ALC.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is AllocationDomainException or NpgsqlException)
        {
            throw await FailAsync(
                "ReleaseTestObjectAllocation",
                actorId,
                organizationGroupId,
                allocationId,
                correlationId,
                exception,
                cancellationToken);
        }
    }

    public async Task<TestObjectAllocationResult> GetAsync(
        string allocationId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(
            allocationId, correlationId, cancellationToken);
        try
        {
            var id = ParseAllocationId(allocationId);
            TestObjectAllocationResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                result = await store.LoadAllocationAsync(organizationGroupId, id, transactionToken)
                    ?? throw new AllocationDomainException(AllocationErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(organizationGroupId, actorId, result.ObjectScope, transactionToken);
                await store.WriteReadAuditAsync(
                    result.AllocationId,
                    result.SubjectAllocationVersion,
                    organizationGroupId,
                    actorId,
                    "READ_TEST_OBJECT_ALLOCATION",
                    correlationId,
                    clock.UtcNow,
                    transactionToken);
            }, cancellationToken);
            return result ?? throw new InvalidOperationException("ALC.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is AllocationDomainException or NpgsqlException)
        {
            throw await FailAsync(
                "GetTestObjectAllocation",
                actorId,
                organizationGroupId,
                allocationId,
                correlationId,
                exception,
                cancellationToken);
        }
    }

    private async Task<(AllocationGateResult Receiving, AllocationGateResult Scope, AllocationGateResult Quantity, decimal AvailableAmount)>
        EvaluateGatesAsync(
            string organizationGroupId,
            CreateTestObjectAllocationRequest request,
            string correlationId,
            CancellationToken cancellationToken)
    {
        var receiving = await EvaluateGateAsync(AllocationGateSources.Receiving, async () =>
        {
            var result = await receivingEligibilityPort.EvaluateAsync(new ReceivingEligibilityV2Request(
                request.ObjectScope.LaboratoryId,
                request.ReceivedItemId,
                ReceivingEligibilityActions.TestAssignment,
                request.ExpectedReceivedItemVersion,
                ReceivingEligibilityV2Contract.RuleSetVersion), cancellationToken);
            return AllocationRules.RequireAllowed(
                AllocationGateSources.Receiving,
                result.Decision,
                result.ItemVersion,
                result.RuleSetVersion,
                result.ReasonCodes);
        });

        var scope = await EvaluateGateAsync(AllocationGateSources.Scope, async () =>
        {
            var result = await scopeEligibilityPort.EvaluateAsync(new ScopeProductionEligibilityRequest(
                organizationGroupId,
                request.ScopeMatrixId,
                request.ExpectedScopeMatrixVersion,
                ScopeContract.RuleSetVersion)
            {
                CorrelationId = correlationId
            }, cancellationToken);
            return AllocationRules.RequireAllowed(
                AllocationGateSources.Scope,
                result.Decision,
                result.CurrentMatrixVersion,
                result.RuleSetVersion,
                result.ReasonCodes);
        });

        decimal availableAmount = 0m;
        var quantity = await EvaluateGateAsync(AllocationGateSources.Quantity, async () =>
        {
            var result = await quantityAvailabilityPort.EvaluateAsync(new QuantityAvailabilityRequest(
                organizationGroupId,
                request.QuantityAccountId,
                request.ExpectedQuantityAccountVersion,
                QuantityContract.RuleSetVersion,
                request.RequestedAmount)
            {
                CorrelationId = correlationId
            }, cancellationToken);
            availableAmount = result.AvailableAmount ?? 0m;
            return AllocationRules.RequireAllowed(
                AllocationGateSources.Quantity,
                result.Decision,
                result.CurrentAccountVersion,
                result.RuleSetVersion,
                result.ReasonCodes);
        });

        return (receiving, scope, quantity, availableAmount);
    }

    private static async Task<AllocationGateResult> EvaluateGateAsync(
        string source,
        Func<Task<AllocationGateResult>> evaluate)
    {
        AllocationGateResult result;
        try
        {
            result = await evaluate();
        }
        catch (AllocationDomainException exception)
        {
            AllocationTelemetry.RecordGate(
                source,
                string.Equals(exception.ErrorCode, AllocationErrorCodes.EligibilityBlocked, StringComparison.Ordinal)
                    ? "BLOCKED"
                    : "UNKNOWN");
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AllocationTelemetry.RecordGate(source, "UNKNOWN");
            throw new AllocationDomainException(AllocationErrorCodes.ApplicabilityUnknown, source);
        }

        AllocationTelemetry.RecordGate(source, result.Decision);
        return result;
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
            "AllocationCommand",
            actor?.ActorId,
            organizationGroupId,
            target,
            correlationId,
            AllocationErrorCodes.NotAuthorized,
            cancellationToken);
        throw new AllocationDomainException(AllocationErrorCodes.NotAuthorized);
    }

    private async Task AuthorizeAsync(
        string organizationGroupId,
        string actorId,
        AllocationObjectContext objectScope,
        CancellationToken cancellationToken)
    {
        var decision = await authorizationPort.AuthorizeAsync(new AllocationAuthorizationRequest(
            organizationGroupId,
            actorId,
            objectScope,
            AllocationCapabilities.Assign), cancellationToken);
        if (!decision.Allowed)
            throw new AllocationDomainException(AllocationErrorCodes.NotAuthorized);
    }

    private async Task<AllocationDomainException> FailAsync(
        string commandType,
        string actorId,
        string organizationGroupId,
        string? target,
        string correlationId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (code, gateSource) = exception switch
        {
            AllocationDomainException domain => (domain.ErrorCode, domain.GateSource),
            PostgresException { SqlState: "23505" } => (AllocationErrorCodes.ValidationFailed, null),
            _ => (AllocationErrorCodes.PersistenceUnavailable, (string?)null)
        };
        AllocationTelemetry.RecordRejected(code);
        logger.LogWarning(
            "Allocation command {CommandType} failed closed with {ErrorCode} (gate {GateSource}); correlation {CorrelationId}",
            commandType,
            code,
            gateSource ?? "-",
            correlationId);
        await WriteAttemptOrFailClosedAsync(
            commandType,
            actorId,
            organizationGroupId,
            target,
            correlationId,
            code,
            cancellationToken);
        return new AllocationDomainException(code, gateSource);
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
                AllocationRules.HashTarget(string.IsNullOrWhiteSpace(target) ? "unresolved-target" : target),
                correlationId,
                code,
                clock.UtcNow,
                cancellationToken);
        }
        catch (Exception auditException) when (auditException is NpgsqlException or InvalidOperationException)
        {
            throw new AllocationDomainException(AllocationErrorCodes.PersistenceUnavailable);
        }
    }

    private static Guid ParseAllocationId(string value) =>
        Guid.TryParseExact(value, "N", out var id) || Guid.TryParse(value, out id)
            ? id
            : throw new AllocationDomainException(AllocationErrorCodes.ObjectNotAccessible);
}

internal sealed class AllocationStatusPort(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IAllocationAuthorizationPort authorizationPort,
    ITransactionCoordinator transactionCoordinator,
    AllocationStore store,
    AllocationAttemptAuditWriter attemptAuditWriter,
    ILogger<AllocationStatusPort> logger) : IAllocationStatusPort
{
    public async ValueTask<AllocationStatusResult> EvaluateAsync(
        AllocationStatusRequest request,
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
                request.AllocationId,
                correlationId,
                cancellationToken);
            throw new AllocationDomainException(AllocationErrorCodes.NotAuthorized);
        }

        if (!Guid.TryParseExact(request.AllocationId, "N", out var allocationId) &&
            !Guid.TryParse(request.AllocationId, out allocationId))
        {
            return Record(AllocationRules.EvaluateStatus(request, null, null, clock.UtcNow));
        }

        try
        {
            AllocationStatusResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var allocation = await store.LoadAllocationAsync(organizationGroupId, allocationId, transactionToken);
                if (allocation is null)
                {
                    result = AllocationRules.EvaluateStatus(request, null, null, clock.UtcNow);
                    return;
                }

                var authorization = await authorizationPort.AuthorizeAsync(new AllocationAuthorizationRequest(
                    organizationGroupId,
                    actor.ActorId,
                    allocation.ObjectScope,
                    AllocationCapabilities.Assign), transactionToken);
                if (!authorization.Allowed)
                    throw new AllocationDomainException(AllocationErrorCodes.NotAuthorized);

                var subjectState = await store.LoadSubjectStateAsync(
                    organizationGroupId,
                    allocation.Subject.SubjectType,
                    allocation.Subject.Id,
                    transactionToken);
                result = AllocationRules.EvaluateStatus(
                    request, allocation, subjectState.CurrentVersion, clock.UtcNow);
                await store.WriteReadAuditAsync(
                    allocation.AllocationId,
                    subjectState.CurrentVersion,
                    organizationGroupId,
                    actor.ActorId,
                    "EVALUATE_ALLOCATION_STATUS",
                    correlationId,
                    clock.UtcNow,
                    transactionToken);
            }, cancellationToken);
            return Record(result ?? AllocationRules.EvaluateStatus(request, null, null, clock.UtcNow));
        }
        catch (AllocationDomainException exception)
            when (string.Equals(exception.ErrorCode, AllocationErrorCodes.NotAuthorized, StringComparison.Ordinal))
        {
            await WriteDeniedAsync(
                actor.ActorId,
                organizationGroupId,
                request.AllocationId,
                correlationId,
                cancellationToken);
            throw;
        }
        catch (NpgsqlException)
        {
            logger.LogWarning("Allocation status failed closed because persistence is unavailable");
            return Record(new AllocationStatusResult(
                AllocationStatusDecisions.Unknown,
                [AllocationStatusReasons.AllocationUnavailable],
                request.AllocationId,
                null,
                null,
                AllocationContract.RuleSetVersion));
        }
    }

    private AllocationStatusResult Record(AllocationStatusResult result)
    {
        AllocationTelemetry.RecordGate("STATUS", result.Decision);
        if (string.Equals(result.Decision, AllocationStatusDecisions.Unknown, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Allocation status failed closed with reasons {ReasonCodes}",
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
                "EvaluateAllocationStatus",
                actorId,
                organizationGroupId,
                AllocationRules.HashTarget(target),
                correlationId,
                AllocationErrorCodes.NotAuthorized,
                clock.UtcNow,
                cancellationToken);
        }
        catch (NpgsqlException)
        {
            throw new AllocationDomainException(AllocationErrorCodes.PersistenceUnavailable);
        }
    }
}
