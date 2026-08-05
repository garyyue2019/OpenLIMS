using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using OpenLIMS.Contracts.Operations;

namespace OpenLIMS.Modules.Operations;

internal sealed class OperationsDomainException(string errorCode) : Exception(errorCode)
{
    public string ErrorCode { get; } = errorCode;
}

internal static partial class OperationsRules
{
    private static readonly Regex StableIdentifier = StableIdentifierPattern();

    public static OperationsObjectContext NormalizeScope(OperationsObjectContext? value)
    {
        if (value is null)
            throw new OperationsDomainException(OperationsErrorCodes.ValidationFailed);
        return new OperationsObjectContext(
            Identifier(value.LegalEntityId),
            Identifier(value.LaboratoryId),
            Identifier(value.CustomerId),
            Identifier(value.ServiceOrderId),
            Identifier(value.ProductCategory));
    }

    public static LineageEdgeResult CreateLineageEdge(
        string edgeId,
        CreateLineageEdgeRequest? request,
        IReadOnlyList<LineageEdgeResult> existingEdges,
        string actorId,
        DateTimeOffset now)
    {
        if (request is null || existingEdges is null)
            throw new OperationsDomainException(OperationsErrorCodes.ValidationFailed);
        var source = Identifier(request.SourceObjectId);
        var target = Identifier(request.TargetObjectId);
        if (string.Equals(source, target, StringComparison.Ordinal))
            throw new OperationsDomainException(OperationsErrorCodes.LineageCycle);
        var relation = Identifier(request.RelationKind).ToUpperInvariant();
        if (relation is not (LineageRelationKinds.DerivedFrom or LineageRelationKinds.SplitFrom or
            LineageRelationKinds.CompositeFrom or LineageRelationKinds.NonDestructiveUse))
        {
            throw new OperationsDomainException(OperationsErrorCodes.ValidationFailed);
        }
        if (existingEdges.Any(edge =>
            string.Equals(edge.SourceObjectId, source, StringComparison.Ordinal) &&
            string.Equals(edge.TargetObjectId, target, StringComparison.Ordinal) &&
            string.Equals(edge.RelationKind, relation, StringComparison.Ordinal)))
        {
            throw new OperationsDomainException(OperationsErrorCodes.ValidationFailed);
        }
        if (relation is LineageRelationKinds.DerivedFrom or LineageRelationKinds.SplitFrom &&
            existingEdges.Any(edge =>
                string.Equals(edge.TargetObjectId, target, StringComparison.Ordinal) &&
                edge.RelationKind is LineageRelationKinds.DerivedFrom or LineageRelationKinds.SplitFrom &&
                !string.Equals(edge.SourceObjectId, source, StringComparison.Ordinal)))
        {
            throw new OperationsDomainException(OperationsErrorCodes.LineageParentConflict);
        }
        if (HasPath(target, source, existingEdges))
            throw new OperationsDomainException(OperationsErrorCodes.LineageCycle);
        return new LineageEdgeResult(
            edgeId,
            source,
            target,
            relation,
            Reference(request.Basis),
            NormalizeScope(request.ObjectScope),
            actorId,
            now);
    }

    public static CustodyEventResult CreateCustodyEvent(
        string eventId,
        RecordCustodyEventRequest? request,
        CustodyEventResult? current,
        string actorId,
        DateTimeOffset now)
    {
        if (request is null)
            throw new OperationsDomainException(OperationsErrorCodes.ValidationFailed);
        var kind = Identifier(request.EventKind).ToUpperInvariant();
        if (kind is not (CustodyEventKinds.Received or CustodyEventKinds.Transferred or
            CustodyEventKinds.CheckedOut or CustodyEventKinds.Returned or
            CustodyEventKinds.Retained or CustodyEventKinds.Disposed))
        {
            throw new OperationsDomainException(OperationsErrorCodes.ValidationFailed);
        }
        var from = OptionalIdentifier(request.FromLocationId);
        var to = Identifier(request.ToLocationId);
        if (current is null)
        {
            if (!string.Equals(kind, CustodyEventKinds.Received, StringComparison.Ordinal) || from is not null)
                throw new OperationsDomainException(OperationsErrorCodes.CustodySequenceConflict);
        }
        else
        {
            if (string.Equals(current.EventKind, CustodyEventKinds.Disposed, StringComparison.Ordinal) ||
                string.Equals(kind, CustodyEventKinds.Received, StringComparison.Ordinal) ||
                !string.Equals(from, current.ToLocationId, StringComparison.Ordinal))
            {
                throw new OperationsDomainException(OperationsErrorCodes.CustodySequenceConflict);
            }
        }
        return new CustodyEventResult(
            eventId,
            Identifier(request.ObjectId),
            (current?.Sequence ?? 0) + 1,
            kind,
            from,
            to,
            Identifier(request.ResponsiblePartyId),
            Identifier(request.EvidenceRef),
            NormalizeScope(request.ObjectScope),
            actorId,
            now);
    }

    public static WorkPlanResult CreateWorkPlan(
        string workPlanId,
        CreateWorkPlanRequest? request,
        string actorId,
        DateTimeOffset now)
    {
        if (request is null || request.Tasks is null || request.Tasks.Count is < 1 or > 500)
            throw new OperationsDomainException(OperationsErrorCodes.ValidationFailed);
        var inputs = request.Tasks.ToDictionary(task => Identifier(task.TaskId), StringComparer.Ordinal);
        if (inputs.Count != request.Tasks.Count)
            throw new OperationsDomainException(OperationsErrorCodes.ValidationFailed);
        foreach (var input in inputs.Values)
        {
            if (input.Priority is < 0 or > 100 || input.Sequence < 1 ||
                input.PlannedStart is not null && input.PlannedEnd <= input.PlannedStart ||
                input.DependencyTaskIds is null || input.DependencyTaskIds.Count > 100)
            {
                throw new OperationsDomainException(OperationsErrorCodes.ValidationFailed);
            }
            foreach (var dependency in input.DependencyTaskIds.Select(Identifier))
            {
                if (!inputs.ContainsKey(dependency) || string.Equals(dependency, input.TaskId, StringComparison.Ordinal))
                    throw new OperationsDomainException(OperationsErrorCodes.ValidationFailed);
            }
        }
        EnsureAcyclic(inputs);
        var tasks = inputs.Values
            .Select(input => new WorkTaskResult(
                Identifier(input.TaskId),
                Identifier(input.ScopeLineId),
                Reference(input.Method),
                Identifier(input.WorkCenterId),
                input.Priority,
                input.Sequence,
                input.Destructive,
                input.PlannedStart,
                input.PlannedEnd,
                input.DependencyTaskIds.Select(Identifier).Distinct(StringComparer.Ordinal).ToArray(),
                input.DependencyTaskIds.Count == 0 ? WorkTaskStates.Ready : WorkTaskStates.Planned,
                null,
                null,
                null))
            .OrderBy(task => task.Sequence)
            .ThenBy(task => task.TaskId, StringComparer.Ordinal)
            .ToArray();
        return new WorkPlanResult(
            workPlanId,
            1,
            OperationsContract.RuleSetVersion,
            WorkPlanStates.Active,
            Reference(request.ScopeMatrix),
            Reference(request.SampleIdentity),
            NormalizeScope(request.ObjectScope),
            tasks,
            [],
            actorId,
            now);
    }

    public static WorkPlanResult ChangeTaskState(
        WorkPlanResult current,
        string taskId,
        ChangeWorkTaskStateRequest? request,
        string actorId,
        DateTimeOffset now)
    {
        if (request is null || request.ExpectedPlanVersion != current.Version)
            throw new OperationsDomainException(OperationsErrorCodes.ExpectedVersionConflict);
        var normalizedTaskId = Identifier(taskId);
        var task = current.Tasks.SingleOrDefault(candidate =>
            string.Equals(candidate.TaskId, normalizedTaskId, StringComparison.Ordinal))
            ?? throw new OperationsDomainException(OperationsErrorCodes.ObjectNotAccessible);
        var next = Identifier(request.State).ToUpperInvariant();
        var reason = Text(request.Reason, 1000);
        if (!AllowedTransition(task.State, next))
            throw new OperationsDomainException(OperationsErrorCodes.InvalidTaskTransition);
        if (next is WorkTaskStates.Ready or WorkTaskStates.InProgress &&
            task.DependencyTaskIds.Any(dependency =>
                current.Tasks.Single(candidate => candidate.TaskId == dependency).State != WorkTaskStates.Completed))
        {
            throw new OperationsDomainException(OperationsErrorCodes.DependencyBlocked);
        }

        var tasks = current.Tasks
            .Select(candidate => string.Equals(candidate.TaskId, normalizedTaskId, StringComparison.Ordinal)
                ? candidate with
                {
                    State = next,
                    StateReason = reason,
                    StateChangedBy = actorId,
                    StateChangedAt = now
                }
                : candidate)
            .ToArray();
        if (string.Equals(next, WorkTaskStates.Completed, StringComparison.Ordinal))
            tasks = PromoteReadyTasks(tasks, actorId, now);
        var planState = tasks.All(candidate => candidate.State is WorkTaskStates.Completed or WorkTaskStates.Cancelled)
            ? WorkPlanStates.Completed
            : WorkPlanStates.Active;
        return current with
        {
            Version = current.Version + 1,
            State = planState,
            Tasks = tasks,
            RecordedBy = actorId,
            RecordedAt = now
        };
    }

    public static WorkPlanResult AddReservation(
        WorkPlanResult current,
        ReserveResourceRequest? request,
        string reservationId,
        string actorId,
        DateTimeOffset now)
    {
        if (request is null || request.ExpectedPlanVersion != current.Version)
            throw new OperationsDomainException(OperationsErrorCodes.ExpectedVersionConflict);
        var taskId = Identifier(request.TaskId);
        var task = current.Tasks.SingleOrDefault(candidate => candidate.TaskId == taskId)
            ?? throw new OperationsDomainException(OperationsErrorCodes.ObjectNotAccessible);
        if (task.State is WorkTaskStates.Completed or WorkTaskStates.Cancelled || request.EndsAt <= request.StartsAt)
            throw new OperationsDomainException(OperationsErrorCodes.ValidationFailed);
        var kind = Identifier(request.ResourceKind).ToUpperInvariant();
        if (kind is not (ResourceKinds.Person or ResourceKinds.Equipment or ResourceKinds.Location or ResourceKinds.Fixture))
            throw new OperationsDomainException(OperationsErrorCodes.ValidationFailed);
        var result = new ResourceReservationResult(
            reservationId,
            taskId,
            kind,
            Identifier(request.ResourceId),
            request.StartsAt,
            request.EndsAt,
            actorId,
            now);
        return current with
        {
            Version = current.Version + 1,
            Reservations = current.Reservations.Append(result).ToArray(),
            RecordedBy = actorId,
            RecordedAt = now
        };
    }

    public static WorkQueueResult BuildQueue(
        string workCenterId,
        string? state,
        IReadOnlyList<WorkPlanResult> plans)
    {
        var center = Identifier(workCenterId);
        var normalizedState = string.IsNullOrWhiteSpace(state) ? null : Identifier(state).ToUpperInvariant();
        if (normalizedState is not null && normalizedState is not (
            WorkTaskStates.Planned or WorkTaskStates.Ready or WorkTaskStates.InProgress or
            WorkTaskStates.Blocked or WorkTaskStates.Completed or WorkTaskStates.Cancelled))
        {
            throw new OperationsDomainException(OperationsErrorCodes.ValidationFailed);
        }
        var items = plans
            .SelectMany(plan => plan.Tasks
                .Where(task => string.Equals(task.WorkCenterId, center, StringComparison.Ordinal) &&
                    (normalizedState is null || string.Equals(task.State, normalizedState, StringComparison.Ordinal)))
                .Select(task => new WorkQueueItem(
                    plan.WorkPlanId,
                    plan.Version,
                    task.TaskId,
                    task.WorkCenterId,
                    task.State,
                    task.Priority,
                    task.Sequence,
                    task.PlannedStart,
                    task.PlannedEnd,
                    task.DependencyTaskIds,
                    plan.ObjectScope)))
            .OrderByDescending(item => item.Priority)
            .ThenBy(item => item.PlannedStart ?? DateTimeOffset.MaxValue)
            .ThenBy(item => item.Sequence)
            .ThenBy(item => item.TaskId, StringComparer.Ordinal)
            .ToArray();
        return new WorkQueueResult(center, normalizedState, OperationsContract.RuleSetVersion, items);
    }

    public static string HashTarget(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool HasPath(string start, string target, IReadOnlyList<LineageEdgeResult> edges)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(start);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current))
                continue;
            if (string.Equals(current, target, StringComparison.Ordinal))
                return true;
            foreach (var edge in edges.Where(edge => edge.SourceObjectId == current))
                pending.Push(edge.TargetObjectId);
        }
        return false;
    }

    private static void EnsureAcyclic(IReadOnlyDictionary<string, WorkTaskInput> tasks)
    {
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var taskId in tasks.Keys)
            Visit(taskId);

        void Visit(string taskId)
        {
            if (visited.Contains(taskId))
                return;
            if (!visiting.Add(taskId))
                throw new OperationsDomainException(OperationsErrorCodes.ValidationFailed);
            foreach (var dependency in tasks[taskId].DependencyTaskIds)
                Visit(dependency);
            visiting.Remove(taskId);
            visited.Add(taskId);
        }
    }

    private static WorkTaskResult[] PromoteReadyTasks(
        IReadOnlyList<WorkTaskResult> tasks,
        string actorId,
        DateTimeOffset now)
    {
        return tasks.Select(task =>
        {
            if (!string.Equals(task.State, WorkTaskStates.Planned, StringComparison.Ordinal) ||
                task.DependencyTaskIds.Any(dependency =>
                    tasks.Single(candidate => candidate.TaskId == dependency).State != WorkTaskStates.Completed))
            {
                return task;
            }
            return task with
            {
                State = WorkTaskStates.Ready,
                StateReason = "DEPENDENCIES_COMPLETED",
                StateChangedBy = actorId,
                StateChangedAt = now
            };
        }).ToArray();
    }

    private static bool AllowedTransition(string current, string next) => current switch
    {
        WorkTaskStates.Planned => next is WorkTaskStates.Blocked or WorkTaskStates.Cancelled,
        WorkTaskStates.Ready => next is WorkTaskStates.InProgress or WorkTaskStates.Blocked or WorkTaskStates.Cancelled,
        WorkTaskStates.InProgress => next is WorkTaskStates.Completed or WorkTaskStates.Blocked,
        WorkTaskStates.Blocked => next is WorkTaskStates.Ready or WorkTaskStates.Cancelled,
        _ => false
    };

    private static OperationsVersionedReference Reference(OperationsVersionedReference? value) =>
        value is not null && value.Version > 0
            ? new OperationsVersionedReference(Identifier(value.Id), value.Version)
            : throw new OperationsDomainException(OperationsErrorCodes.ValidationFailed);

    private static string Identifier(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return StableIdentifier.IsMatch(normalized)
            ? normalized
            : throw new OperationsDomainException(OperationsErrorCodes.ValidationFailed);
    }

    private static string? OptionalIdentifier(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Identifier(value);

    private static string Text(string? value, int maximumLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized.Length is > 0 && normalized.Length <= maximumLength
            ? normalized
            : throw new OperationsDomainException(OperationsErrorCodes.ValidationFailed);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex StableIdentifierPattern();
}
