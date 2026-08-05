using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OpenLIMS.Contracts.Operations;
using OpenLIMS.Modules.Operations;
using Xunit;

namespace OpenLIMS.Operations.UnitTests;

[Trait("Profile", "operations")]
public sealed class OperationsRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Lineage_rejects_self_reference_and_cycles()
    {
        var self = Assert.Throws<OperationsDomainException>(() => OperationsRules.CreateLineageEdge(
            Id(1), Edge("SAMPLE-A", "SAMPLE-A"), [], "actor", Now));
        var first = OperationsRules.CreateLineageEdge(
            Id(2), Edge("SAMPLE-A", "SAMPLE-B"), [], "actor", Now);
        var cycle = Assert.Throws<OperationsDomainException>(() => OperationsRules.CreateLineageEdge(
            Id(3), Edge("SAMPLE-B", "SAMPLE-A"), [first], "actor", Now));

        Assert.Equal(OperationsErrorCodes.LineageCycle, self.ErrorCode);
        Assert.Equal(OperationsErrorCodes.LineageCycle, cycle.ErrorCode);
    }

    [Fact]
    public void Direct_parent_cannot_be_replaced_but_composite_sources_can_accumulate()
    {
        var first = OperationsRules.CreateLineageEdge(
            Id(1), Edge("SOURCE-A", "DERIVED-A"), [], "actor", Now);
        var conflict = Assert.Throws<OperationsDomainException>(() => OperationsRules.CreateLineageEdge(
            Id(2), Edge("SOURCE-B", "DERIVED-A"), [first], "actor", Now));
        var composite = OperationsRules.CreateLineageEdge(
            Id(3), Edge("SOURCE-B", "DERIVED-A") with { RelationKind = LineageRelationKinds.CompositeFrom },
            [first], "actor", Now);

        Assert.Equal(OperationsErrorCodes.LineageParentConflict, conflict.ErrorCode);
        Assert.Equal(LineageRelationKinds.CompositeFrom, composite.RelationKind);
    }

    [Fact]
    public void Custody_requires_received_first_and_contiguous_locations()
    {
        var invalidFirst = Assert.Throws<OperationsDomainException>(() => OperationsRules.CreateCustodyEvent(
            Id(1), Custody(CustodyEventKinds.Transferred, "DOCK", "LAB"), null, "actor", Now));
        var received = OperationsRules.CreateCustodyEvent(
            Id(2), Custody(CustodyEventKinds.Received, null, "DOCK"), null, "actor", Now);
        var wrongFrom = Assert.Throws<OperationsDomainException>(() => OperationsRules.CreateCustodyEvent(
            Id(3), Custody(CustodyEventKinds.Transferred, "STORE", "LAB"), received, "actor", Now));
        var transferred = OperationsRules.CreateCustodyEvent(
            Id(4), Custody(CustodyEventKinds.Transferred, "DOCK", "LAB"), received, "actor", Now);

        Assert.Equal(OperationsErrorCodes.CustodySequenceConflict, invalidFirst.ErrorCode);
        Assert.Equal(OperationsErrorCodes.CustodySequenceConflict, wrongFrom.ErrorCode);
        Assert.Equal(2, transferred.Sequence);
    }

    [Fact]
    public void Disposed_sample_cannot_receive_more_custody_events()
    {
        var received = OperationsRules.CreateCustodyEvent(
            Id(1), Custody(CustodyEventKinds.Received, null, "DOCK"), null, "actor", Now);
        var disposed = OperationsRules.CreateCustodyEvent(
            Id(2), Custody(CustodyEventKinds.Disposed, "DOCK", "DISPOSAL"), received, "actor", Now);
        var after = Assert.Throws<OperationsDomainException>(() => OperationsRules.CreateCustodyEvent(
            Id(3), Custody(CustodyEventKinds.Returned, "DISPOSAL", "DOCK"), disposed, "actor", Now));

        Assert.Equal(OperationsErrorCodes.CustodySequenceConflict, after.ErrorCode);
    }

    [Fact]
    public void Work_plan_rejects_dependency_cycles()
    {
        var request = PlanRequest() with
        {
            Tasks =
            [
                Task("TASK-A", 1, ["TASK-B"]),
                Task("TASK-B", 2, ["TASK-A"])
            ]
        };

        var exception = Assert.Throws<OperationsDomainException>(() =>
            OperationsRules.CreateWorkPlan(Id(1), request, "actor", Now));

        Assert.Equal(OperationsErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    [Fact]
    public void Completed_dependency_promotes_downstream_task_to_ready()
    {
        var plan = OperationsRules.CreateWorkPlan(Id(1), PlanRequest(), "actor", Now);
        var started = OperationsRules.ChangeTaskState(
            plan, "TASK-A", new ChangeWorkTaskStateRequest(1, WorkTaskStates.InProgress, "started"),
            "actor", Now.AddMinutes(1));
        var completed = OperationsRules.ChangeTaskState(
            started, "TASK-A", new ChangeWorkTaskStateRequest(2, WorkTaskStates.Completed, "done"),
            "actor", Now.AddMinutes(2));

        Assert.Equal(WorkTaskStates.Ready, completed.Tasks.Single(task => task.TaskId == "TASK-B").State);
        Assert.Equal(3, completed.Version);
    }

    [Fact]
    public void Blocked_task_cannot_resume_until_dependencies_complete()
    {
        var plan = OperationsRules.CreateWorkPlan(Id(1), PlanRequest(), "actor", Now);
        var blocked = OperationsRules.ChangeTaskState(
            plan, "TASK-B", new ChangeWorkTaskStateRequest(1, WorkTaskStates.Blocked, "waiting"),
            "actor", Now.AddMinutes(1));
        var exception = Assert.Throws<OperationsDomainException>(() => OperationsRules.ChangeTaskState(
            blocked, "TASK-B", new ChangeWorkTaskStateRequest(2, WorkTaskStates.Ready, "resume"),
            "actor", Now.AddMinutes(2)));

        Assert.Equal(OperationsErrorCodes.DependencyBlocked, exception.ErrorCode);
    }

    [Fact]
    public void Resource_reservation_is_versioned_and_rejects_invalid_window()
    {
        var plan = OperationsRules.CreateWorkPlan(Id(1), PlanRequest(), "actor", Now);
        var reserved = OperationsRules.AddReservation(
            plan,
            new ReserveResourceRequest(1, "TASK-A", ResourceKinds.Equipment, "EQUIP-1", Now, Now.AddHours(1)),
            Id(2), "actor", Now);
        var invalid = Assert.Throws<OperationsDomainException>(() => OperationsRules.AddReservation(
            reserved,
            new ReserveResourceRequest(2, "TASK-A", ResourceKinds.Equipment, "EQUIP-1", Now, Now),
            Id(3), "actor", Now));

        Assert.Single(reserved.Reservations);
        Assert.Equal(2, reserved.Version);
        Assert.Equal(OperationsErrorCodes.ValidationFailed, invalid.ErrorCode);
    }

    [Fact]
    public void Work_queue_orders_priority_then_time_then_sequence()
    {
        var first = OperationsRules.CreateWorkPlan(Id(1), PlanRequest(), "actor", Now);
        var second = OperationsRules.CreateWorkPlan(
            Id(2),
            PlanRequest() with
            {
                Tasks =
                [
                    Task("TASK-C", 1, []) with
                    {
                        Priority = 90,
                        PlannedStart = Now.AddHours(2),
                        PlannedEnd = Now.AddHours(3)
                    }
                ]
            },
            "actor",
            Now);
        var queue = OperationsRules.BuildQueue("WC-A", null, [first, second]);

        Assert.Equal("TASK-C", queue.Items[0].TaskId);
        Assert.Equal("TASK-A", queue.Items[1].TaskId);
        Assert.Equal("TASK-B", queue.Items[2].TaskId);
    }

    [Fact]
    public async Task Authorization_requires_every_exact_scope_claim()
    {
        var context = new DefaultHttpContext { User = Principal(includeProductCategory: true) };
        var port = new HttpClaimsOperationsAuthorizationPort(new HttpContextAccessor { HttpContext = context });

        var allowed = await port.AuthorizeAsync(AuthRequest(), TestContext.Current.CancellationToken);
        context.User = Principal(includeProductCategory: false);
        var denied = await port.AuthorizeAsync(AuthRequest(), TestContext.Current.CancellationToken);

        Assert.True(allowed.Allowed);
        Assert.False(denied.Allowed);
    }

    private static CreateLineageEdgeRequest Edge(string source, string target) => new(
        source, target, LineageRelationKinds.DerivedFrom, Ref("BASIS", 1), Scope());

    private static RecordCustodyEventRequest Custody(string kind, string? from, string to) => new(
        "SAMPLE-A", kind, from, to, "PERSON-A", "EVIDENCE-A", Scope());

    private static CreateWorkPlanRequest PlanRequest() => new(
        Ref("SCOPE", 1),
        Ref("IDENTITY", 1),
        [Task("TASK-A", 1, []), Task("TASK-B", 2, ["TASK-A"])],
        Scope());

    private static WorkTaskInput Task(string id, int sequence, IReadOnlyList<string> dependencies) => new(
        id, $"SCOPE-LINE-{sequence}", Ref("METHOD", 1), "WC-A",
        sequence == 1 ? 50 : 10, sequence, sequence == 2,
        Now.AddHours(sequence), Now.AddHours(sequence + 1), dependencies);

    private static OperationsVersionedReference Ref(string id, long version) => new(id, version);

    private static OperationsObjectContext Scope() => new(
        "LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TEXTILE");

    private static string Id(int value) => value.ToString("x32");

    private static OperationsAuthorizationRequest AuthRequest() => new(
        "group-a", "actor-a", Scope(), OperationsCapabilities.Write);

    private static ClaimsPrincipal Principal(bool includeProductCategory)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "actor-a"),
            new("organization_group", "group-a"),
            new(OperationsClaimTypes.Capability, OperationsCapabilities.Write),
            new(OperationsClaimTypes.LegalEntity, "LEGAL-A"),
            new(OperationsClaimTypes.Laboratory, "LAB-A"),
            new(OperationsClaimTypes.Customer, "CUSTOMER-A"),
            new(OperationsClaimTypes.ServiceOrder, "ORDER-A")
        };
        if (includeProductCategory)
            claims.Add(new Claim(OperationsClaimTypes.ProductCategory, "TEXTILE"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }
}
