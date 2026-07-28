# Progress Log: DEV-025

## Session: 2026-07-28 实施

### Phase 1: 开始门禁与上下文

- **Status:** in_progress
- 用户已批准 DEV-025；`ATC-TOY-002@1.0.0` 在规格 PR #25 中发布并合并为主干提交 `26bf6f3`。
- 主干 planning 记录 `df68714` 已推送；从该提交创建 `codex/dev-025-toy-test-unit-sample-demand`。
- 下一步运行四项开始门禁并审查任务卡/既有边界。

## Test Results

| Command | Result |
|---|---|
| `python -m tools.specgen validate` | PASS: 185 spec versions / 389 source entries |
| `python -m tools.specgen source-status` | PASS: SOURCE CURRENT |
| `python -m tools.specgen impact` | PASS: no pending direct or transitive impact |
| `python -m tools.specgen ready --story ATC-TOY-002@1.0.0` | PASS: READY |
| `python -m tools.specgen ready --story ATC-TOY-003@1.0.0` | PASS: READY (DEV-026 handoff prerequisite) |
| `python -m tools.specgen ready --story ATC-TOY-004@0.1.0` | Expected BLOCKED: OD-034 and unapproved dependencies remain unresolved |

### Recovered work from previous agent

- Read the generated `ATC-TOY-002@1.0.0` task card and approved `BUS-TOY-003@1.0.0`, `BUS-TOY-004@1.0.0`, and `AC-TOY-003@1.0.0`.
- Confirmed DEV-025 is READY and implementation work has not started; the only worktree changes are this untracked planning directory.
- Confirmed DEV-026 is approved and READY for sequential implementation after DEV-025.
- Re-read the complete generated task card with UTF-8 output and confirmed the exact `allowed_paths`, API operations, error codes, persistence/audit invariants, and nine required scenario families.

### Phase 2: failing tests

- Added `ToyTestUnitPlanDomainTests.cs` covering complete six-kind demand calculation, deterministic input hashing, positive/contiguous TestUnit sequencing, same-unit exclusive-destructive rejection, UNKNOWN/missing chemical minimum, rule unit conflicts, technical approval hash/state checks, and exact downstream totals.
- Confirmed the new unit suite fails at compile time because the DEV-025 contract/domain types and stable errors do not yet exist; this is the expected red baseline.
- Added the DEV-025 public contract, stable states/errors/capability, status port, service surface, and pure deterministic domain validation/calculation.
- Unit profile now passes 23/23 tests, including the new DEV-025 cases.
- Added HTTP contract coverage for the four DEV-025 operations, OpenAPI names, response shape, and new problem mappings. The red run fails exactly on missing routes/OpenAPI entries and three unmapped 422 domain errors (5 failures, 14 passes).
- Added PostgreSQL integration red tests for positive reconstruction/status, permanent destructive history, true concurrent version append, approval capability, blocked downstream rollback, append-only triggers, and audit/outbox rollback.
- The integration project currently fails at compile time because Quantity/Allocation contract references have not yet been wired; this is the expected integration red baseline.

### Phase 3: implementation

- Added a monotonic `20260728_002_toy_test_unit_sample_demand` migration without modifying the published DEV-024 migration.
- Added append-only plan/TestUnit/sequence/destructive-history/requirement/approval/downstream tables and database constraints/triggers.
- Added the real service, store, status port, capability enforcement, version locks, Quantity/Allocation public-port gates, atomic audit/outbox evidence, independent failure attempts, telemetry, four API routes, and deterministic OpenAPI entries.
- Targeted suites are green: unit 23/23, contract 19/19, PostgreSQL integration 16/16; Release warning-as-error build also passes for the integration dependency graph.

### Phase 4: verification

- `scripts/verify.ps1 -Profile task -Module toy` PASS: locked restore, whole-solution Release warning-as-error build, unit 23/23, contract 19/19, PostgreSQL integration 16/16.
- Architecture profile PASS: 17/17 architecture tests.
- Contracts profile PASS, including all module API contract suites and Toy 19/19.
- Strict spec validation PASS: 185 versions / 389 source entries; source current; sealed history/Seal chain intact.
- Generation is idempotent twice: both runs `written=0 unchanged=125 removed=0`; `specgen check` PASS.
- Python repository suite PASS: 41/41.
- Whole-solution format audit exposed unrelated pre-existing import-order findings outside the Story boundary; the edited Toy integration file was formatted mechanically and will be re-verified in isolation.
- All edited Toy contract/module/unit/integration files pass isolated `dotnet format --verify-no-changes`.
- Full unfiltered .NET suite PASS across every contract, architecture, unit, integration, and chain E2E project.
- Final readiness/impact audit PASS: `ATC-TOY-002@1.0.0` READY, no direct/transitive impact, `git diff --check` clean, and all 37 working-tree paths authorized by the Story.

### Phase 5: delivery

- Implementation commit: `f578e97fd0ace68697ce49ab995321ce2d62177c`.
- PR #26: `https://github.com/garyyue2019/OpenLIMS/pull/26`.
- CI: 3/3 successful (`deterministic-specification-gate`, `verify-module-onboarding-windows`, `verify`).
- Squash-merged to `main` as `0c1ae1b66d05a467d0376cff13f123afaa83c5de` at 2026-07-28T01:35:44Z.
