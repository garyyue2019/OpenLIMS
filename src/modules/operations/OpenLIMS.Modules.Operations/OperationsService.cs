using Microsoft.Extensions.Logging;
using Npgsql;
using OpenLIMS.Contracts.Operations;
using OpenLIMS.Contracts.Platform;

namespace OpenLIMS.Modules.Operations;

public interface IOperationsService
{
    Task<LineageEdgeResult> CreateLineageEdgeAsync(
        CreateLineageEdgeRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<LineageGraphResult> GetLineageAsync(
        string objectId,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<CustodyEventResult> RecordCustodyEventAsync(
        RecordCustodyEventRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<CustodyChainResult> GetCustodyAsync(
        string objectId,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<WorkPlanResult> CreateWorkPlanAsync(
        CreateWorkPlanRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<WorkPlanResult> GetWorkPlanAsync(
        string workPlanId,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<WorkPlanResult> ChangeTaskStateAsync(
        string workPlanId,
        string taskId,
        ChangeWorkTaskStateRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<WorkPlanResult> ReserveResourceAsync(
        string workPlanId,
        ReserveResourceRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<WorkQueueResult> GetWorkQueueAsync(
        string workCenterId,
        string? state,
        string correlationId,
        CancellationToken cancellationToken = default);
}

internal sealed class OperationsService(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IIdGenerator idGenerator,
    IOperationsAuthorizationPort authorizationPort,
    ITransactionCoordinator transactionCoordinator,
    OperationsStore store,
    OperationsAttemptAuditWriter attemptAuditWriter,
    ILogger<OperationsService> logger) : IOperationsService
{
    public async Task<LineageEdgeResult> CreateLineageEdgeAsync(
        CreateLineageEdgeRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var edgeId = GeneratedId();
        var actor = await RequireActorAsync("CreateLineageEdge", request?.TargetObjectId, correlationId, cancellationToken);
        try
        {
            LineageEdgeResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await store.AcquireLockAsync("lineage", transactionToken);
                var edges = await store.LoadAllEdgesAsync(actor.OrganizationGroupId, transactionToken);
                result = OperationsRules.CreateLineageEdge(
                    edgeId.ToString("N"), request, edges, actor.ActorId, clock.UtcNow);
                await AuthorizeAsync(actor, result.ObjectScope, OperationsCapabilities.Write, transactionToken);
                await store.InsertLineageEdgeAsync(result, actor.OrganizationGroupId, correlationId, transactionToken);
            }, cancellationToken);
            OperationsTelemetry.Record("lineage-edge");
            return result ?? throw new InvalidOperationException("OPS.RESULT_MISSING");
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            throw await FailAsync("CreateLineageEdge", actor, request?.TargetObjectId, correlationId, exception, cancellationToken);
        }
    }

    public async Task<LineageGraphResult> GetLineageAsync(
        string objectId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var actor = await RequireActorAsync("GetLineage", objectId, correlationId, cancellationToken);
        try
        {
            var target = IdentifierForLookup(objectId);
            LineageGraphResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var all = await store.LoadAllEdgesAsync(actor.OrganizationGroupId, transactionToken);
                var connected = ConnectedEdges(target, all);
                if (connected.Count == 0)
                    throw new OperationsDomainException(OperationsErrorCodes.ObjectNotAccessible);
                foreach (var scope in connected.Select(edge => edge.ObjectScope).Distinct())
                    await AuthorizeAsync(actor, scope, OperationsCapabilities.Read, transactionToken);
                result = new LineageGraphResult(target, OperationsContract.RuleSetVersion, connected);
                await store.WriteReadAuditAsync(
                    target, actor.OrganizationGroupId, actor.ActorId, "READ_LINEAGE", "1",
                    correlationId, clock.UtcNow, transactionToken);
            }, cancellationToken);
            return result ?? throw new InvalidOperationException("OPS.RESULT_MISSING");
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            throw await FailAsync("GetLineage", actor, objectId, correlationId, exception, cancellationToken);
        }
    }

    public async Task<CustodyEventResult> RecordCustodyEventAsync(
        RecordCustodyEventRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var eventId = GeneratedId();
        var actor = await RequireActorAsync("RecordCustodyEvent", request.ObjectId, correlationId, cancellationToken);
        try
        {
            CustodyEventResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var objectId = IdentifierForLookup(request.ObjectId);
                await store.AcquireLockAsync($"custody:{objectId}", transactionToken);
                var current = await store.LoadCurrentCustodyAsync(actor.OrganizationGroupId, objectId, transactionToken);
                result = OperationsRules.CreateCustodyEvent(
                    eventId.ToString("N"), request, current, actor.ActorId, clock.UtcNow);
                if (current is not null && current.ObjectScope != result.ObjectScope)
                    throw new OperationsDomainException(OperationsErrorCodes.ValidationFailed);
                await AuthorizeAsync(actor, result.ObjectScope, OperationsCapabilities.Write, transactionToken);
                await store.InsertCustodyEventAsync(result, actor.OrganizationGroupId, correlationId, transactionToken);
            }, cancellationToken);
            OperationsTelemetry.Record("custody-event");
            return result ?? throw new InvalidOperationException("OPS.RESULT_MISSING");
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            throw await FailAsync("RecordCustodyEvent", actor, request?.ObjectId, correlationId, exception, cancellationToken);
        }
    }

    public async Task<CustodyChainResult> GetCustodyAsync(
        string objectId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var actor = await RequireActorAsync("GetCustody", objectId, correlationId, cancellationToken);
        try
        {
            var target = IdentifierForLookup(objectId);
            CustodyChainResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var events = await store.LoadCustodyChainAsync(actor.OrganizationGroupId, target, transactionToken);
                if (events.Count == 0)
                    throw new OperationsDomainException(OperationsErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(actor, events[0].ObjectScope, OperationsCapabilities.Read, transactionToken);
                result = new CustodyChainResult(target, OperationsContract.RuleSetVersion, events);
                await store.WriteReadAuditAsync(
                    target, actor.OrganizationGroupId, actor.ActorId, "READ_CUSTODY_CHAIN",
                    events[^1].Sequence.ToString(), correlationId, clock.UtcNow, transactionToken);
            }, cancellationToken);
            return result ?? throw new InvalidOperationException("OPS.RESULT_MISSING");
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            throw await FailAsync("GetCustody", actor, objectId, correlationId, exception, cancellationToken);
        }
    }

    public async Task<WorkPlanResult> CreateWorkPlanAsync(
        CreateWorkPlanRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var workPlanId = GeneratedId();
        var actor = await RequireActorAsync("CreateWorkPlan", workPlanId.ToString("N"), correlationId, cancellationToken);
        try
        {
            var result = OperationsRules.CreateWorkPlan(
                workPlanId.ToString("N"), request, actor.ActorId, clock.UtcNow);
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await AuthorizeAsync(actor, result.ObjectScope, OperationsCapabilities.Write, transactionToken);
                await store.InsertWorkPlanAsync(
                    result, actor.OrganizationGroupId, correlationId, "OperationsWorkPlanCreated", transactionToken);
            }, cancellationToken);
            OperationsTelemetry.Record("work-plan");
            return result;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            throw await FailAsync(
                "CreateWorkPlan", actor, workPlanId.ToString("N"), correlationId, exception, cancellationToken);
        }
    }

    public async Task<WorkPlanResult> GetWorkPlanAsync(
        string workPlanId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var actor = await RequireActorAsync("GetWorkPlan", workPlanId, correlationId, cancellationToken);
        try
        {
            var id = ParseId(workPlanId);
            WorkPlanResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                result = await store.LoadWorkPlanAsync(actor.OrganizationGroupId, id, transactionToken)
                    ?? throw new OperationsDomainException(OperationsErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(actor, result.ObjectScope, OperationsCapabilities.Read, transactionToken);
                await store.WriteReadAuditAsync(
                    result.WorkPlanId, actor.OrganizationGroupId, actor.ActorId, "READ_WORK_PLAN",
                    result.Version.ToString(), correlationId, clock.UtcNow, transactionToken);
            }, cancellationToken);
            return result ?? throw new InvalidOperationException("OPS.RESULT_MISSING");
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            throw await FailAsync("GetWorkPlan", actor, workPlanId, correlationId, exception, cancellationToken);
        }
    }

    public Task<WorkPlanResult> ChangeTaskStateAsync(
        string workPlanId,
        string taskId,
        ChangeWorkTaskStateRequest request,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        MutateWorkPlanAsync(
            "ChangeWorkTaskState",
            "OperationsWorkTaskStateChanged",
            workPlanId,
            correlationId,
            (current, actor, now) => OperationsRules.ChangeTaskState(current, taskId, request, actor, now),
            null,
            cancellationToken);

    public Task<WorkPlanResult> ReserveResourceAsync(
        string workPlanId,
        ReserveResourceRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var reservationId = GeneratedId().ToString("N");
        return MutateWorkPlanAsync(
            "ReserveResource",
            "OperationsResourceReserved",
            workPlanId,
            correlationId,
            (current, actor, now) => OperationsRules.AddReservation(
                current, request, reservationId, actor, now),
            async (current, result, actor, transactionToken) =>
            {
                var reservation = result.Reservations[^1];
                await store.AcquireLockAsync(
                    $"resource:{reservation.ResourceKind}:{reservation.ResourceId}", transactionToken);
                if (await store.HasResourceConflictAsync(
                    actor.OrganizationGroupId,
                    reservation.ResourceKind,
                    reservation.ResourceId,
                    reservation.StartsAt,
                    reservation.EndsAt,
                    transactionToken))
                {
                    throw new OperationsDomainException(OperationsErrorCodes.ResourceConflict);
                }
                await store.InsertReservationAsync(
                    Guid.Parse(current.WorkPlanId),
                    result.Version,
                    reservation,
                    actor.OrganizationGroupId,
                    correlationId,
                    transactionToken);
            },
            cancellationToken);
    }

    public async Task<WorkQueueResult> GetWorkQueueAsync(
        string workCenterId,
        string? state,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var actor = await RequireActorAsync("GetWorkQueue", workCenterId, correlationId, cancellationToken);
        try
        {
            WorkQueueResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var plans = await store.LoadCurrentWorkPlansAsync(actor.OrganizationGroupId, transactionToken);
                var authorized = new List<WorkPlanResult>();
                foreach (var plan in plans)
                {
                    var decision = await authorizationPort.AuthorizeAsync(new OperationsAuthorizationRequest(
                        actor.OrganizationGroupId,
                        actor.ActorId,
                        plan.ObjectScope,
                        OperationsCapabilities.Read), transactionToken);
                    if (decision.Allowed)
                        authorized.Add(plan);
                }
                result = OperationsRules.BuildQueue(workCenterId, state, authorized);
                foreach (var planId in result.Items.Select(item => item.WorkPlanId).Distinct(StringComparer.Ordinal))
                {
                    var plan = authorized.Single(candidate => candidate.WorkPlanId == planId);
                    await store.WriteReadAuditAsync(
                        plan.WorkPlanId, actor.OrganizationGroupId, actor.ActorId, "READ_WORK_QUEUE",
                        plan.Version.ToString(), correlationId, clock.UtcNow, transactionToken);
                }
            }, cancellationToken);
            return result ?? throw new InvalidOperationException("OPS.RESULT_MISSING");
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            throw await FailAsync("GetWorkQueue", actor, workCenterId, correlationId, exception, cancellationToken);
        }
    }

    private async Task<WorkPlanResult> MutateWorkPlanAsync(
        string commandType,
        string eventType,
        string workPlanId,
        string correlationId,
        Func<WorkPlanResult, string, DateTimeOffset, WorkPlanResult> mutate,
        Func<WorkPlanResult, WorkPlanResult, ActorScope, CancellationToken, Task>? beforeInsert,
        CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(commandType, workPlanId, correlationId, cancellationToken);
        try
        {
            var id = ParseId(workPlanId);
            WorkPlanResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await store.AcquireLockAsync($"work-plan:{id:N}", transactionToken);
                var current = await store.LoadWorkPlanAsync(actor.OrganizationGroupId, id, transactionToken)
                    ?? throw new OperationsDomainException(OperationsErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(actor, current.ObjectScope, OperationsCapabilities.Write, transactionToken);
                result = mutate(current, actor.ActorId, clock.UtcNow);
                if (beforeInsert is not null)
                    await beforeInsert(current, result, actor, transactionToken);
                await store.InsertWorkPlanAsync(
                    result, actor.OrganizationGroupId, correlationId, eventType, transactionToken);
            }, cancellationToken);
            OperationsTelemetry.Record(commandType);
            return result ?? throw new InvalidOperationException("OPS.RESULT_MISSING");
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            throw await FailAsync(commandType, actor, workPlanId, correlationId, exception, cancellationToken);
        }
    }

    private async Task<ActorScope> RequireActorAsync(
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
            return new ActorScope(organizationGroupId, actor.ActorId);
        }
        await WriteAttemptOrFailClosedAsync(
            commandType, actor?.ActorId, organizationGroupId, target, correlationId,
            OperationsErrorCodes.NotAuthorized, cancellationToken);
        throw new OperationsDomainException(OperationsErrorCodes.NotAuthorized);
    }

    private async Task AuthorizeAsync(
        ActorScope actor,
        OperationsObjectContext scope,
        string capability,
        CancellationToken cancellationToken)
    {
        var decision = await authorizationPort.AuthorizeAsync(new OperationsAuthorizationRequest(
            actor.OrganizationGroupId,
            actor.ActorId,
            scope,
            capability), cancellationToken);
        if (!decision.Allowed)
            throw new OperationsDomainException(OperationsErrorCodes.NotAuthorized);
    }

    private async Task<OperationsDomainException> FailAsync(
        string commandType,
        ActorScope actor,
        string? target,
        string correlationId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var code = exception is OperationsDomainException domain
            ? domain.ErrorCode
            : OperationsErrorCodes.PersistenceUnavailable;
        OperationsTelemetry.RecordRejected(code);
        logger.LogWarning(
            "Operations command {CommandType} failed closed with {ErrorCode}; correlation {CorrelationId}",
            commandType, code, correlationId);
        await WriteAttemptOrFailClosedAsync(
            commandType, actor.ActorId, actor.OrganizationGroupId, target, correlationId, code, cancellationToken);
        return new OperationsDomainException(code);
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
                OperationsRules.HashTarget(string.IsNullOrWhiteSpace(target) ? "unresolved-target" : target),
                correlationId,
                code,
                clock.UtcNow,
                cancellationToken);
        }
        catch (Exception auditException) when (auditException is NpgsqlException or InvalidOperationException)
        {
            throw new OperationsDomainException(OperationsErrorCodes.PersistenceUnavailable);
        }
    }

    private static IReadOnlyList<LineageEdgeResult> ConnectedEdges(
        string objectId,
        IReadOnlyList<LineageEdgeResult> edges)
    {
        var objects = new HashSet<string>(StringComparer.Ordinal) { objectId };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var edge in edges)
            {
                if (objects.Contains(edge.SourceObjectId) && objects.Add(edge.TargetObjectId)) changed = true;
                if (objects.Contains(edge.TargetObjectId) && objects.Add(edge.SourceObjectId)) changed = true;
            }
        }
        return edges
            .Where(edge => objects.Contains(edge.SourceObjectId) && objects.Contains(edge.TargetObjectId))
            .OrderBy(edge => edge.RecordedAt)
            .ThenBy(edge => edge.EdgeId, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsExpected(Exception exception) =>
        exception is OperationsDomainException or NpgsqlException or InvalidOperationException;

    private Guid GeneratedId()
    {
        var value = idGenerator.NewId();
        return Guid.TryParse(value, out var id)
            ? id
            : throw new InvalidOperationException("OPS.ID_GENERATOR_INVALID");
    }

    private static Guid ParseId(string value) =>
        Guid.TryParseExact(value, "N", out var id) || Guid.TryParse(value, out id)
            ? id
            : throw new OperationsDomainException(OperationsErrorCodes.ObjectNotAccessible);

    private static string IdentifierForLookup(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized.Length is > 0 and <= 128
            ? normalized
            : throw new OperationsDomainException(OperationsErrorCodes.ObjectNotAccessible);
    }

    private sealed record ActorScope(string OrganizationGroupId, string ActorId);
}
