# Toy TestUnit plan and sample-demand approval

DEV-025 implements `ATC-TOY-002@1.0.0`. It creates immutable, version-pinned TestUnit plans and explainable sample-demand drafts, requires a separate technical-approval capability before downstream use, and records every successful business fact with audit and outbox evidence in one PostgreSQL transaction.

## Version and state model

- Every plan pins the product version, effective age-grade decision version, accessibility-assessment version, scope matrix and lines, sample rules, TestUnits, hazards, physical-object versions, parallel numbers, and ordered task steps.
- Sequence order starts at 1 and is contiguous per TestUnit. Parallel numbers are positive and unique within the plan.
- A draft demand retains `BASE`, `PARALLEL`, `EXCLUSIVE_DESTRUCTIVE`, `CHEMICAL_MINIMUM`, `RETEST_RESERVE`, and `RETENTION` as separate source-rule-backed components. Totals are grouped only by exact dimension and unit; the module performs no implicit conversion.
- Missing rules, UNKNOWN applicability, incompatible use of one rule reference, or an unpinned input fails with `TOY.SAMPLE_REQUIREMENT_UNKNOWN` and creates no approvable fact.
- Approval requires both `toy.manage` and `toy.sample-demand.approve`. Whether an actor receives those capabilities remains an authorization-policy decision; the Toy module does not invent a self-approval default.
- A later approved plan derives the earlier approved plan and requirement as `SUPERSEDED`; published rows are never updated or deleted.

## API surface

- `POST /api/v1/toy/products/{id}/test-unit-plans` creates a plan and demand draft.
- `POST /api/v1/toy/products/{id}/test-unit-plans/{planVersion}/approval` appends technical approval and freezes the input hash.
- `POST /api/v1/toy/products/{id}/test-unit-plans/{planVersion}/allocations` evaluates and binds exact downstream public-port decisions.
- `GET /api/v1/toy/products/{id}/test-unit-plans/{planVersion}` reconstructs the complete plan, components, approval, and downstream evidence.
- `IToyTestUnitPlanStatusPort@v1` returns `ALLOWED`, `BLOCKED`, or `UNKNOWN` with approved requirement, reservation, and allocation references. Consumers must treat `UNKNOWN` as denial.

## Quantity and Allocation boundary

The delivered `IQuantityAvailabilityPort@v1` and `IAllocationStatusPort@v1` are read-only decision ports. They do not expose reservation or allocation creation commands. Therefore, the DEV-025 allocation operation accepts already-created, version-pinned account/reservation/allocation references, verifies them through those public ports, and appends the returned decisions verbatim. It never calls another module's internal service and never reads or writes Quantity or Allocation private tables.

A non-`ALLOWED` result, returned identity/version/rule-set mismatch, insufficient available amount, released allocation, UNKNOWN result, timeout, or exception maps to `TOY.DOWNSTREAM_ELIGIBILITY_BLOCKED`. The Toy transaction then writes no downstream success fact; an independent `audit_attempt` records the rejection.

## Destructive history and evidence

- Two steps in the same exclusive destructive group cannot share one TestUnit in a plan.
- Permanent history is appended when an approved downstream allocation is bound. Its database uniqueness key contains product, physical-object identity and version, and exclusive group, so changing a client TestUnit id cannot disguise reuse.
- A general Allocation release does not delete this Toy-owned history. Later reuse remains rejected with `TOY.DESTRUCTIVE_TEST_UNIT_CONFLICT`.
- Plan creation, technical approval, and downstream binding each write the Toy fact, platform audit intent, and outbox envelope atomically. Audit or outbox failure rolls the whole transaction back.
- All DEV-025 tables have PostgreSQL UPDATE/DELETE rejection triggers. Failed authorization, UNKNOWN, destructive conflict, downstream block, concurrency conflict, and persistence failure are recorded separately in `toy.audit_attempt`.
