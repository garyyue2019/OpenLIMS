# Findings: DEV-025

## Recovered gate and specification evidence (2026-07-28)

- Start gates passed: 185 spec versions and 389 source entries validated, source is current, impact is empty, and `ATC-TOY-002@1.0.0` is READY.
- `ATC-TOY-003@1.0.0` is also READY, so DEV-026 can follow once DEV-025 is merged. `ATC-TOY-004@0.1.0` remains BLOCKED and is out of scope.
- TestUnit identity pins product, physical item, scope, exact rule versions, hazard domain, positive parallel number, and contiguous sequence.
- Reuse within the same mutually exclusive destructive group is permanently forbidden even after a general Allocation is released.
- Sample demand must preserve separate contribution categories: BASE, PARALLEL, EXCLUSIVE_DESTRUCTIVE, CHEMICAL_MINIMUM, RETEST_RESERVE, and RETENTION.
- Missing/UNKNOWN hazard, chemical minimum, unit, or sharing semantics fail closed; implicit conversion and defaults are forbidden.
- Technical approval requires `toy.sample-demand.approve`; an approved plan/demand is append-only and cannot be edited in place.
- Cross-module collaboration is only through versioned Quantity and Allocation public ports/contracts; private tables are not accessible.
- DEV-026 LabelReview and DEV-027 ConformityDecision are explicitly excluded from DEV-025.

## Task-card implementation boundary

- Primary code paths are `contracts/toy/**`, `src/modules/toy/**`, API/worker host wiring, and toy unit/contract/integration tests; architecture tests and lock files are allowed where required.
- Required operations are create plan+demand, approve and freeze, allocate through public ports, and reconstruct by product/plan version.
- Required stable failures include invalid plan, unknown requirement, destructive conflict, unapproved demand, blocked downstream, expected-version conflict, unauthorized/object-inaccessible, and persistence unavailable.
- Audit/outbox and the module fact must commit atomically; rejected attempts are independently append-only.

## Existing Toy contract/domain baseline

- `ToyContracts.cs` currently exposes only DEV-024 age-grade/accessibility operations, `toy.manage`, and `IToyAgeGradeStatusPort`; DEV-025 contracts and `toy.sample-demand.approve` must be added without changing those semantics.
- Existing domain code centralizes validation and stable `ToyDomainException` errors, computes deterministic status results, and hashes stable targets with SHA-256; DEV-025 can extend this pattern.
- `ToyContract.RuleSetVersion` is age-grade-specific (`TOY-AGE-GRADE@1.0.0`), so TestUnit/sample-demand inputs must carry their own exact rule-set and per-rule references rather than reuse or reinterpret that constant.

## Existing Toy service/persistence baseline

- `ToyProductService` uses a transaction coordinator, PostgreSQL advisory/product locking, stored object-scope authorization, expected-current-version checks, and a separate append-only attempt-audit writer for failures.
- Successful facts write platform `AuditIntent` plus `OutboxEnvelope` in the same active transaction; the DEV-025 service/store should reuse this atomic evidence shape.
- Existing product `Version` is derived from DEV-024 appended facts. DEV-025 plan versioning should be isolated from that age-grade aggregate version while pinning the approved age/accessibility versions supplied by the caller.
- Current authorization helper always requests `toy.manage`; technical approval needs an explicit second capability without weakening existing command authorization.
- Persistence output was too large for one complete tool response; inspect `ToyPersistence.cs` in bounded chunks before editing and record any relevant schema/read patterns.

## Migration/API baseline

- `ToyMigration.cs` is the already-published `20260727_001_toy_age_grade` migration and must not be rewritten. DEV-025 needs a new later migration file/version and module wiring.
- All existing Toy tables, including `audit_attempt`, have PostgreSQL UPDATE/DELETE rejection triggers; DEV-025 tables need the same database-level append-only enforcement.
- Existing endpoint helpers only return `ToyProductOverview`; DEV-025 should add a generic or dedicated response path without destabilizing DEV-024 endpoints.
- Current HTTP mapping uses 409 for expected-version conflict, 403/404 for authorization/access, 422 for frozen-state errors, and 503 for persistence failures; new stable errors need explicit mapping consistent with task-card semantics.

## Module/authorization wiring baseline

- `ToyModule` registers services/ports through `TryAddScoped`, so tests/hosts can replace public ports; DEV-025 Quantity/Allocation adapters should be injectable and fail closed when not supplied.
- The migration entry point currently runs only `ToyMigrator`; a new migration must be invoked after it.
- `HttpClaimsToyAuthorizationPort` already accepts an arbitrary requested capability and exact object scope, so the same port can enforce `toy.sample-demand.approve` in addition to `toy.manage`.
- Toy module currently references only Toy/Platform contracts; adding public Quantity and Allocation ports requires project references and corresponding lock-file regeneration.

## Quantity/Allocation public-port constraints

- `IQuantityAvailabilityPort@v1` evaluates an account id, exact expected account version, exact quantity rule-set version, and requested amount; it returns ALLOWED/BLOCKED/UNKNOWN plus current account version and available amount.
- `IAllocationStatusPort@v1` evaluates an existing allocation id, exact expected subject-allocation version, and exact allocation rule-set version; it returns ALLOWED/BLOCKED/UNKNOWN plus current allocation state/version.
- Neither approved v1 port exposes a reserve/create command. DEV-025 must not invent a cross-module private-table shortcut or widen another module; reconcile the Toy `allocations` operation with the approved BUS/AC wording and the existing public-port surface before design.
- Both ports define UNKNOWN explicitly, making fail-closed mapping to `TOY.DOWNSTREAM_ELIGIBILITY_BLOCKED` straightforward once the request shape is settled.

## Approved downstream wording

- BUS-TOY-003 requires the Toy plan gate to pass before calling a versioned Allocation public port and forbids Allocation private-table access.
- BUS-TOY-004 names `QuantityAvailabilityPort@v1` for version-pinned quantity check/reservation and forbids Quantity private-table access.
- AC-TOY-003 requires only APPROVED versions to call Quantity/Allocation public ports and to persist their decisions/object versions verbatim.
- The source text expects reservation/allocation creation, while the delivered v1 interfaces only evaluate existing account/allocation status. The implementation must keep the contract gap explicit (through an injectable public orchestration abstraction or caller-supplied downstream references) and must not fabricate successful reservation/allocation state.

## Toy test patterns

- Existing unit tests call internal domain helpers directly and assert exact stable error codes, state derivation, fail-closed UNKNOWN, and version pinning.
- Existing HTTP contract tests replace `IToyProductService`, verify status/problem mappings and OpenAPI operation names, and intentionally avoid PostgreSQL. A separate `IToyTestUnitPlanService` avoids forcing DEV-025 methods into the DEV-024 contract stub.
- Contract coverage can add a second stubbed service plus the four task-card endpoints and exact response types while preserving all six existing operations.

## Toy PostgreSQL integration-test baseline

- Toy integration tests use a dedicated local PostgreSQL database, run `ToyModule.ApplyMigrationAsync`, build the real module through DI, and disable collection parallelization.
- Tests already assert atomic platform audit/outbox counts, independent `toy.audit_attempt` evidence, immutable UPDATE/DELETE SQLSTATE `55000`, authorization denial, expected-version conflicts, and direct database constraints.
- DEV-025 can add a separate integration test class/file sharing the existing collection and database helpers only if helpers are made reusable; otherwise duplicate a small fixture inside the allowed toy integration path.

## Integration fixture details

- The existing test class already contains reusable-in-class helpers for DI setup, migration, truncation, failure triggers, SQL execution, and fixed auth/actor/clock; adding DEV-025 tests to the same file minimizes fixture duplication.
- `BuildProvider` replaces only `IToyAuthorizationPort`; it must also install deterministic Quantity/Allocation test doubles once the module references those ports.
- `PrepareAsync` explicitly truncates each Toy table, so all new DEV-025 tables must be added in dependency order to avoid cross-test contamination.
- Failure triggers match audit actions containing `TOY` and outbox message types beginning `Toy`, so DEV-025 evidence naming should follow those conventions and automatically exercise rollback tests.

## Cross-module gate pattern

- Allocation and Batch call public status/availability ports outside their own write transaction, pin exact versions/rule sets/correlation id, convert any non-cancellation exception to UNKNOWN, and persist the returned decision only after it is ALLOWED.
- DEV-025 should follow the same exception boundary and never translate port failure, BLOCKED, UNKNOWN, null version, or mismatched returned identity/rule set into success.
- A practical v1 Toy allocation request can carry existing `quantityAccountId/accountVersion` and `allocationId/subjectAllocationVersion`; Toy verifies both public decisions and appends a downstream binding. It cannot honestly create the external reservation/allocation because the approved v1 public contracts expose no command methods.

## Repository verification constraints

- Task verification performs locked restore, Release build with warnings as errors, then all tests tagged `Profile=toy`; lock files must therefore be regenerated before task verification.
- Architecture and contract profiles run separately, and the final repository/spec/Python gates remain required.
- The current architecture contract-root enumeration appears not to include `contracts/toy`; inspect and extend it within the allowed architecture-test path so Toy public contracts receive the same private-persistence boundary check.

## DEV-025 implementation structure

- Reuse the existing scoped PostgreSQL transaction accessor and platform audit/outbox writers in a new `ToyTestUnitPlanStore`; keep the published DEV-024 store/migration behavior untouched.
- Add a second monotonic migration (`20260728_002_toy_test_unit_sample_demand`) and invoke it after `ToyMigrator` from the module entry point.
- New facts and outbox message types should keep `TOY`/`Toy.*` naming so the existing failure-injection tests cover atomic rollback.
- Integrate status/approval/plan/rejection metrics into the existing `OpenLIMS.Toy` meter instead of creating a separate telemetry surface.
- The host's `/openapi/v1.json` is a manually pinned deterministic document in `Program.cs`, not generated from endpoint metadata; every new route therefore requires a matching explicit path/operation entry.
- Design review corrected destructive history timing: a draft plan may mention a TestUnit without consuming it; permanent history is appended atomically only when an approved downstream allocation is bound, and survives every later general Allocation release.
- Adding Quantity/Allocation project references to the Toy module legitimately propagates those project dependencies into host/API contract-test lock graphs; standalone lock files showing no semantic diff are only line-ending refresh noise.

## Approved Scope

- Story：`ATC-TOY-002@1.0.0`，implementation task `DEV-025`，状态 approved/ready。
- 主要拥有者：toy 模块；依赖已交付的 `ATC-TOY-001@1.0.0`、`ATC-QTY-001@1.0.0`、`ATC-ALLOC-001@1.0.0`。
- 核心结果：TestUnit 计划、可解释样品需求、技术批准、版本固定下游决定与 `IToyTestUnitPlanStatusPort@v1`。
- 明确非目标：DEV-027 多 TestUnit 结论、DEV-026 LabelReview、危险域/化学最低量默认值、Quantity/Allocation 私表访问。

## Design Notes

- 互斥破坏永久复用门禁强于通用 Allocation 的“活跃分配”互斥，必须由 toy 的历史计划事实独立强制。
- 样品需求按 dimension/unit 分组，不允许跨维度或隐含单位换算；规则未知返回 UNKNOWN。
- 批准后计划和需求不可原地修改；后继批准版本通过派生 SUPERSEDED 表达。
