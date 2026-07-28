using Microsoft.Extensions.Logging;
using Npgsql;
using OpenLIMS.Contracts.Allocation;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Quantity;
using OpenLIMS.Contracts.Toy;

namespace OpenLIMS.Modules.Toy;

internal sealed class ToyTestUnitPlanService(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IToyAuthorizationPort authorizationPort,
    ITransactionCoordinator transactionCoordinator,
    ToyStore productStore,
    ToyTestUnitPlanStore planStore,
    ToyAttemptAuditWriter attemptAuditWriter,
    IQuantityAvailabilityPort quantityAvailabilityPort,
    IAllocationStatusPort allocationStatusPort,
    ILogger<ToyTestUnitPlanService> logger) : IToyTestUnitPlanService
{
    public async Task<ToyTestUnitPlanResult> CreatePlanAsync(
        string productId,
        CreateToyTestUnitPlanRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(
            "CreateToyTestUnitPlan", productId, correlationId, cancellationToken);
        try
        {
            var calculation = ToyTestUnitPlanDomain.CalculateDraft(request);
            var productKey = ParseId(productId);
            ToyTestUnitPlanResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await planStore.AcquirePlanLockAsync(productKey, transactionToken);
                var product = await productStore.LoadProductAsync(organizationGroupId, productKey, transactionToken)
                    ?? throw new ToyDomainException(ToyErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(
                    organizationGroupId, actorId, product.ObjectScope, ToyCapabilities.Manage, transactionToken);
                ValidatePinnedProduct(request, product);
                var currentVersion = await planStore.CurrentPlanVersionAsync(
                    organizationGroupId, productKey, transactionToken);
                if (request.ExpectedCurrentVersion != currentVersion)
                    throw new ToyDomainException(ToyErrorCodes.ExpectedVersionConflict);

                await planStore.InsertPlanAsync(
                    productKey,
                    currentVersion + 1,
                    request,
                    calculation,
                    organizationGroupId,
                    actorId,
                    clock.UtcNow,
                    correlationId,
                    transactionToken);
                result = await planStore.LoadAsync(
                    organizationGroupId, productKey, currentVersion + 1, transactionToken);
            }, cancellationToken);
            ToyTelemetry.RecordTestUnitPlan();
            return result ?? throw new InvalidOperationException("TOY.TEST_UNIT_PLAN_RESULT_MISSING");
        }
        catch (Exception exception) when (exception is ToyDomainException or NpgsqlException)
        {
            throw await FailAsync(
                "CreateToyTestUnitPlan", actorId, organizationGroupId,
                productId, correlationId, exception, cancellationToken);
        }
    }

    public async Task<ToyTestUnitPlanResult> ApproveAsync(
        string productId,
        long planVersion,
        ApproveToySampleRequirementRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(
            "ApproveToySampleRequirement", productId, correlationId, cancellationToken);
        try
        {
            if (request is null ||
                planVersion < 1 ||
                request.ExpectedCurrentVersion != planVersion ||
                !string.Equals(
                    request.RuleSetVersion,
                    ToyTestUnitPlanContract.RuleSetVersion,
                    StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(request.InputHash) ||
                string.IsNullOrWhiteSpace(request.ApprovalComment))
            {
                throw new ToyDomainException(ToyErrorCodes.ValidationFailed);
            }

            var productKey = ParseId(productId);
            ToyTestUnitPlanResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await planStore.AcquirePlanLockAsync(productKey, transactionToken);
                var plan = await planStore.LoadAsync(
                    organizationGroupId, productKey, planVersion, transactionToken)
                    ?? throw new ToyDomainException(ToyErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(
                    organizationGroupId, actorId, plan.ObjectScope, ToyCapabilities.Manage, transactionToken);
                await AuthorizeAsync(
                    organizationGroupId,
                    actorId,
                    plan.ObjectScope,
                    ToyCapabilities.SampleDemandApprove,
                    transactionToken);
                var currentVersion = await planStore.CurrentPlanVersionAsync(
                    organizationGroupId, productKey, transactionToken);
                if (currentVersion != planVersion)
                    throw new ToyDomainException(ToyErrorCodes.ExpectedVersionConflict);
                ToyTestUnitPlanDomain.RequireApprovable(
                    plan.Requirement.Decision, plan.InputHash, request.InputHash);
                await planStore.InsertApprovalAsync(
                    plan,
                    request,
                    organizationGroupId,
                    actorId,
                    clock.UtcNow,
                    correlationId,
                    transactionToken);
                result = await planStore.LoadAsync(
                    organizationGroupId, productKey, planVersion, transactionToken);
            }, cancellationToken);
            ToyTelemetry.RecordSampleDemandApproval();
            return result ?? throw new InvalidOperationException("TOY.TEST_UNIT_PLAN_RESULT_MISSING");
        }
        catch (Exception exception) when (exception is ToyDomainException or NpgsqlException)
        {
            throw await FailAsync(
                "ApproveToySampleRequirement", actorId, organizationGroupId,
                productId, correlationId, exception, cancellationToken);
        }
    }

    public async Task<ToyTestUnitPlanResult> RequestAllocationAsync(
        string productId,
        long planVersion,
        RequestToyAllocationRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(
            "RequestToyAllocation", productId, correlationId, cancellationToken);
        try
        {
            if (request is null ||
                planVersion < 1 ||
                request.ExpectedCurrentVersion != planVersion ||
                !string.Equals(
                    request.RuleSetVersion,
                    ToyTestUnitPlanContract.RuleSetVersion,
                    StringComparison.Ordinal))
            {
                throw new ToyDomainException(ToyErrorCodes.ValidationFailed);
            }

            var productKey = ParseId(productId);
            ToyTestUnitPlanResult? pinned = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                pinned = await planStore.LoadAsync(
                    organizationGroupId, productKey, planVersion, transactionToken)
                    ?? throw new ToyDomainException(ToyErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(
                    organizationGroupId, actorId, pinned.ObjectScope, ToyCapabilities.Manage, transactionToken);
                var currentVersion = await planStore.CurrentPlanVersionAsync(
                    organizationGroupId, productKey, transactionToken);
                if (currentVersion != planVersion)
                    throw new ToyDomainException(ToyErrorCodes.ExpectedVersionConflict);
                ToyTestUnitPlanDomain.ValidateDownstreamRequest(
                    pinned.Requirement.Decision, pinned.Requirement.Totals, request.QuantityChecks);
                ToyTestUnitPlanDomain.ValidateAllocationChecks(pinned.TestUnits, request.AllocationChecks);
            }, cancellationToken);

            var quantityDecisions = await EvaluateQuantityAsync(
                organizationGroupId, request.QuantityChecks, correlationId, cancellationToken);
            var allocationDecisions = await EvaluateAllocationAsync(
                organizationGroupId, request.AllocationChecks, correlationId, cancellationToken);

            ToyTestUnitPlanResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await planStore.AcquirePlanLockAsync(productKey, transactionToken);
                var current = await planStore.LoadAsync(
                    organizationGroupId, productKey, planVersion, transactionToken)
                    ?? throw new ToyDomainException(ToyErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(
                    organizationGroupId, actorId, current.ObjectScope, ToyCapabilities.Manage, transactionToken);
                var currentVersion = await planStore.CurrentPlanVersionAsync(
                    organizationGroupId, productKey, transactionToken);
                if (currentVersion != planVersion ||
                    !string.Equals(current.InputHash, pinned!.InputHash, StringComparison.Ordinal))
                {
                    throw new ToyDomainException(ToyErrorCodes.ExpectedVersionConflict);
                }
                ToyTestUnitPlanDomain.ValidateDownstreamRequest(
                    current.Requirement.Decision, current.Requirement.Totals, request.QuantityChecks);
                await planStore.InsertDownstreamDecisionAsync(
                    current,
                    quantityDecisions,
                    allocationDecisions,
                    organizationGroupId,
                    actorId,
                    clock.UtcNow,
                    correlationId,
                    transactionToken);
                result = await planStore.LoadAsync(
                    organizationGroupId, productKey, planVersion, transactionToken);
            }, cancellationToken);
            ToyTelemetry.RecordDownstreamDecision("ALLOWED");
            return result ?? throw new InvalidOperationException("TOY.TEST_UNIT_PLAN_RESULT_MISSING");
        }
        catch (Exception exception) when (exception is ToyDomainException or NpgsqlException)
        {
            throw await FailAsync(
                "RequestToyAllocation", actorId, organizationGroupId,
                productId, correlationId, exception, cancellationToken);
        }
    }

    public async Task<ToyTestUnitPlanResult> GetAsync(
        string productId,
        long planVersion,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(
            "GetToyTestUnitPlan", productId, correlationId, cancellationToken);
        try
        {
            var productKey = ParseId(productId);
            ToyTestUnitPlanResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                result = await planStore.LoadAsync(
                    organizationGroupId, productKey, planVersion, transactionToken)
                    ?? throw new ToyDomainException(ToyErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(
                    organizationGroupId, actorId, result.ObjectScope, ToyCapabilities.Manage, transactionToken);
                await planStore.WriteReadAuditAsync(
                    result,
                    organizationGroupId,
                    actorId,
                    "READ_TOY_TEST_UNIT_PLAN",
                    correlationId,
                    clock.UtcNow,
                    transactionToken);
            }, cancellationToken);
            return result ?? throw new InvalidOperationException("TOY.TEST_UNIT_PLAN_RESULT_MISSING");
        }
        catch (Exception exception) when (exception is ToyDomainException or NpgsqlException)
        {
            throw await FailAsync(
                "GetToyTestUnitPlan", actorId, organizationGroupId,
                productId, correlationId, exception, cancellationToken);
        }
    }

    private static void ValidatePinnedProduct(
        CreateToyTestUnitPlanRequest request,
        ToyProductOverview product)
    {
        if (request.ObjectScope != product.ObjectScope ||
            request.ProductVersion != product.Version ||
            product.EffectiveDecision?.VersionNumber != request.AgeGradeDecisionVersion ||
            !product.Assessments.Any(item => item.VersionNumber == request.AccessibilityAssessmentVersion) ||
            !string.Equals(
                product.AccessibilityStatus,
                ToyAccessibilityStatuses.Settled,
                StringComparison.Ordinal))
        {
            throw new ToyDomainException(ToyErrorCodes.SampleRequirementUnknown);
        }
    }

    private async Task<IReadOnlyList<ToyQuantityDecisionEntry>> EvaluateQuantityAsync(
        string organizationGroupId,
        IReadOnlyList<ToyQuantityGateInput> checks,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var result = new List<ToyQuantityDecisionEntry>(checks.Count);
        foreach (var check in checks)
        {
            QuantityAvailabilityResult decision;
            try
            {
                decision = await quantityAvailabilityPort.EvaluateAsync(new QuantityAvailabilityRequest(
                    organizationGroupId,
                    check.QuantityAccountId,
                    check.ExpectedAccountVersion,
                    check.RuleSetVersion,
                    check.Amount)
                {
                    CorrelationId = correlationId
                }, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw new ToyDomainException(ToyErrorCodes.DownstreamEligibilityBlocked);
            }

            if (!string.Equals(decision.Decision, QuantityAvailabilityDecisions.Allowed, StringComparison.Ordinal) ||
                !string.Equals(decision.QuantityAccountId, check.QuantityAccountId, StringComparison.Ordinal) ||
                decision.CurrentAccountVersion != check.ExpectedAccountVersion ||
                !string.Equals(decision.RuleSetVersion, check.RuleSetVersion, StringComparison.Ordinal) ||
                decision.AvailableAmount is null || decision.AvailableAmount < check.Amount)
            {
                ToyTelemetry.RecordDownstreamDecision(decision.Decision);
                throw new ToyDomainException(ToyErrorCodes.DownstreamEligibilityBlocked);
            }

            result.Add(new ToyQuantityDecisionEntry(
                check.QuantityAccountId,
                check.ExpectedAccountVersion,
                decision.CurrentAccountVersion.Value,
                check.Amount,
                decision.AvailableAmount.Value,
                check.Dimension,
                check.Unit,
                check.ReservationRef,
                decision.Decision,
                decision.ReasonCodes,
                decision.RuleSetVersion));
        }

        return result;
    }

    private async Task<IReadOnlyList<ToyAllocationDecisionEntry>> EvaluateAllocationAsync(
        string organizationGroupId,
        IReadOnlyList<ToyAllocationGateInput> checks,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var result = new List<ToyAllocationDecisionEntry>(checks.Count);
        foreach (var check in checks)
        {
            AllocationStatusResult decision;
            try
            {
                decision = await allocationStatusPort.EvaluateAsync(new AllocationStatusRequest(
                    organizationGroupId,
                    check.AllocationId,
                    check.ExpectedSubjectAllocationVersion,
                    check.RuleSetVersion)
                {
                    CorrelationId = correlationId
                }, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw new ToyDomainException(ToyErrorCodes.DownstreamEligibilityBlocked);
            }

            if (!string.Equals(decision.Decision, AllocationStatusDecisions.Allowed, StringComparison.Ordinal) ||
                !string.Equals(decision.AllocationId, check.AllocationId, StringComparison.Ordinal) ||
                decision.CurrentSubjectAllocationVersion != check.ExpectedSubjectAllocationVersion ||
                !string.Equals(decision.RuleSetVersion, check.RuleSetVersion, StringComparison.Ordinal) ||
                !string.Equals(decision.State, AllocationStates.Active, StringComparison.Ordinal))
            {
                ToyTelemetry.RecordDownstreamDecision(decision.Decision);
                throw new ToyDomainException(ToyErrorCodes.DownstreamEligibilityBlocked);
            }

            result.Add(new ToyAllocationDecisionEntry(
                check.AllocationId,
                check.ExpectedSubjectAllocationVersion,
                decision.CurrentSubjectAllocationVersion.Value,
                decision.State!,
                check.TestUnitId,
                check.SequenceStepId,
                decision.Decision,
                decision.ReasonCodes,
                decision.RuleSetVersion));
        }

        return result;
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

        await WriteAttemptOrFailClosedAsync(
            commandType,
            actor?.ActorId,
            organizationGroupId,
            target,
            correlationId,
            ToyErrorCodes.NotAuthorized,
            cancellationToken);
        throw new ToyDomainException(ToyErrorCodes.NotAuthorized);
    }

    private async Task AuthorizeAsync(
        string organizationGroupId,
        string actorId,
        ToyObjectContext objectScope,
        string capability,
        CancellationToken cancellationToken)
    {
        var decision = await authorizationPort.AuthorizeAsync(new ToyAuthorizationRequest(
            organizationGroupId, actorId, objectScope, capability), cancellationToken);
        if (!decision.Allowed)
            throw new ToyDomainException(ToyErrorCodes.NotAuthorized);
    }

    private async Task<ToyDomainException> FailAsync(
        string commandType,
        string actorId,
        string organizationGroupId,
        string? target,
        string correlationId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var code = exception switch
        {
            ToyDomainException domain => domain.ErrorCode,
            PostgresException { SqlState: "23505" } postgres
                when postgres.ConstraintName?.StartsWith(
                    "destructive_test_unit_usage_", StringComparison.Ordinal) == true =>
                ToyErrorCodes.DestructiveTestUnitConflict,
            PostgresException { SqlState: "23505" } postgres
                when postgres.ConstraintName?.StartsWith(
                    "test_unit_plan_product_id_plan_version", StringComparison.Ordinal) == true =>
                ToyErrorCodes.ExpectedVersionConflict,
            PostgresException { SqlState: "23505" or "23514" } => ToyErrorCodes.TestUnitPlanInvalid,
            _ => ToyErrorCodes.PersistenceUnavailable
        };
        ToyTelemetry.RecordRejected(code);
        logger.LogWarning(
            "Toy TestUnit command {CommandType} failed closed with {ErrorCode}; correlation {CorrelationId}",
            commandType, code, correlationId);
        await WriteAttemptOrFailClosedAsync(
            commandType,
            actorId,
            organizationGroupId,
            target,
            correlationId,
            code,
            cancellationToken);
        return new ToyDomainException(code);
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
                ToyDomain.HashTarget(string.IsNullOrWhiteSpace(target) ? "unresolved-target" : target),
                correlationId,
                code,
                clock.UtcNow,
                cancellationToken);
        }
        catch (Exception auditException) when (auditException is NpgsqlException or InvalidOperationException)
        {
            throw new ToyDomainException(ToyErrorCodes.PersistenceUnavailable);
        }
    }

    private static Guid ParseId(string value) =>
        Guid.TryParseExact(value, "N", out var id) || Guid.TryParse(value, out id)
            ? id
            : throw new ToyDomainException(ToyErrorCodes.ObjectNotAccessible);
}

internal sealed class ToyTestUnitPlanStatusPort(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IToyAuthorizationPort authorizationPort,
    ITransactionCoordinator transactionCoordinator,
    ToyTestUnitPlanStore planStore,
    ToyAttemptAuditWriter attemptAuditWriter,
    ILogger<ToyTestUnitPlanStatusPort> logger) : IToyTestUnitPlanStatusPort
{
    public async ValueTask<ToyTestUnitPlanStatusResult> EvaluateAsync(
        ToyTestUnitPlanStatusRequest request,
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
                actor?.ActorId, organizationGroupId, request.ProductId, correlationId, cancellationToken);
            throw new ToyDomainException(ToyErrorCodes.NotAuthorized);
        }

        if (!string.Equals(
                request.RuleSetVersion,
                ToyTestUnitPlanContract.RuleSetVersion,
                StringComparison.Ordinal))
        {
            return Record(Unknown(request, ToyTestUnitPlanStatusReasons.RuleSetVersionUnknown, null));
        }
        if ((!Guid.TryParseExact(request.ProductId, "N", out var productId) &&
             !Guid.TryParse(request.ProductId, out productId)) || request.PlanVersion < 1)
        {
            return Record(Unknown(request, ToyTestUnitPlanStatusReasons.PlanRequired, null));
        }

        try
        {
            ToyTestUnitPlanStatusResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var plan = await planStore.LoadAsync(
                    organizationGroupId, productId, request.PlanVersion, transactionToken);
                if (plan is null)
                {
                    var currentVersion = await planStore.CurrentPlanVersionAsync(
                        organizationGroupId, productId, transactionToken);
                    result = Unknown(
                        request,
                        currentVersion == 0
                            ? ToyTestUnitPlanStatusReasons.PlanRequired
                            : ToyTestUnitPlanStatusReasons.PlanVersionMismatch,
                        currentVersion == 0 ? null : currentVersion);
                    return;
                }

                var authorization = await authorizationPort.AuthorizeAsync(new ToyAuthorizationRequest(
                    organizationGroupId,
                    actor.ActorId,
                    plan.ObjectScope,
                    ToyCapabilities.Manage), transactionToken);
                if (!authorization.Allowed)
                    throw new ToyDomainException(ToyErrorCodes.NotAuthorized);

                var reasons = new List<string>();
                if (!string.Equals(
                        plan.Requirement.Decision,
                        ToySampleRequirementDecisions.Approved,
                        StringComparison.Ordinal))
                {
                    reasons.Add(ToyTestUnitPlanStatusReasons.RequirementNotApproved);
                }
                if (plan.DownstreamDecisions.Count == 0)
                    reasons.Add(ToyTestUnitPlanStatusReasons.DownstreamAllocationRequired);

                result = new ToyTestUnitPlanStatusResult(
                    reasons.Count == 0
                        ? ToyTestUnitPlanStatusDecisions.Allowed
                        : ToyTestUnitPlanStatusDecisions.Blocked,
                    reasons,
                    request.ProductId,
                    plan.PlanVersion,
                    plan.Requirement.RequirementId,
                    plan.Requirement.RequirementVersion,
                    plan.DownstreamDecisions
                        .SelectMany(item => item.QuantityDecisions)
                        .Select(item => item.ReservationRef)
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .ToArray(),
                    plan.DownstreamDecisions
                        .SelectMany(item => item.AllocationDecisions)
                        .Select(item => item.AllocationId)
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .ToArray(),
                    plan.RuleSetVersion);
                await planStore.WriteReadAuditAsync(
                    plan,
                    organizationGroupId,
                    actor.ActorId,
                    "EVALUATE_TOY_TEST_UNIT_PLAN",
                    correlationId,
                    clock.UtcNow,
                    transactionToken);
            }, cancellationToken);
            return Record(result ?? Unknown(request, ToyTestUnitPlanStatusReasons.ToyUnavailable, null));
        }
        catch (ToyDomainException exception)
            when (string.Equals(exception.ErrorCode, ToyErrorCodes.NotAuthorized, StringComparison.Ordinal))
        {
            await WriteDeniedAsync(
                actor.ActorId, organizationGroupId, request.ProductId, correlationId, cancellationToken);
            throw;
        }
        catch (NpgsqlException)
        {
            logger.LogWarning("Toy TestUnit plan status failed closed because persistence is unavailable");
            return Record(Unknown(request, ToyTestUnitPlanStatusReasons.ToyUnavailable, null));
        }
    }

    private static ToyTestUnitPlanStatusResult Unknown(
        ToyTestUnitPlanStatusRequest request,
        string reason,
        long? currentVersion) => new(
        ToyTestUnitPlanStatusDecisions.Unknown,
        [reason],
        request.ProductId,
        currentVersion,
        null,
        null,
        [],
        [],
        ToyTestUnitPlanContract.RuleSetVersion);

    private static ToyTestUnitPlanStatusResult Record(ToyTestUnitPlanStatusResult result)
    {
        ToyTelemetry.RecordTestUnitPlanStatus(result.Decision);
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
                "EvaluateToyTestUnitPlan",
                actorId,
                organizationGroupId,
                ToyDomain.HashTarget(target),
                correlationId,
                ToyErrorCodes.NotAuthorized,
                clock.UtcNow,
                cancellationToken);
        }
        catch (NpgsqlException)
        {
            throw new ToyDomainException(ToyErrorCodes.PersistenceUnavailable);
        }
    }
}
