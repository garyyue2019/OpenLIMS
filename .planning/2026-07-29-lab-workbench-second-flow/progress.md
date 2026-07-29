# Progress Log: Lab Workbench Second Flow

## 2026-07-29

### Phase 1: hard preflight and implementation boundary

- **Status:** complete
- User approved the recommended Instrument + Result + QC + Report Web batch.
- Restored the previous planning context and left completed-task evidence unchanged.
- Confirmed local `main` was clean and synchronized at `2d8f8f9`.
- Created branch `codex/lab-workbench-second-flow`.
- Mandatory preflight passed: 197 specs / 389 sources, SOURCE CURRENT, and empty impact.
- Initialized an isolated persistent plan for this development slice.
- Confirmed all five domain Stories are approved and that Report needs both `ATC-RPT-001` and `ATC-RPT-002` dependencies.
- Located the repository assertions that must be mechanically extended for the new Story and generated artifacts.
- Added `ATC-WEB-002@1.0.0` / `DEV-033`; strict validation passed at 198 specs / 389 sources and READY returned READY.
- Generated the owned derived artifacts through specgen (`written=10`).
- Inspected the complete Instrument, Result, QC, and Report DTO and HTTP endpoint contracts, including exact query names, response families, and server-side trusted-context injection.
- Captured the exact QC five-gate invariant and both Report issuance-gate and signature/version-chain slices.

### Phase 2: shared flow foundation

- **Status:** in_progress
- Confirmed the first-batch bearer/correlation, Problem Details, capability, auth-state, retry, feature registry, view, and test patterns are reusable.
- Selected a separate build-time feature descriptor and operation-specific structured JSON samples for the deep nested request DTOs.
- Added exact typed clients for all approved Instrument, Result, QC, and Report HTTP operations, including stable-ID encoding and exact status/gate query names.
- Verified the reconstructed progress log is valid UTF-8 with no NUL bytes.
- Frontend typecheck passes with all four new typed clients.
- Added the shared structured JSON parser/validators, accessible JSON editor, server-response panel, focused helper tests, and responsive editor styling.
- Added shared operation state for validation, busy/error/response handling and user-triggered retry; enhanced result panels with visible ID/version/state/decision/rule summaries.
- Shared JSON helper tests (3) and frontend typecheck pass.
- Implemented Instrument and Result workbench views covering every approved write, detail, and status operation with complete DTO examples, validation, response summaries, auth hints, and explicit retry.
- Frontend typecheck remains green after the first two views.
- Implemented QC and Report workbench views covering every approved write/read operation, the exact five-gate invariant, pending-hash issuance flow, controlled actions, and immutable version reads.
- Frontend typecheck remains green after all four views.
- Registered the separate second-flow feature at build time with four stable routes/navigation entries and extended the authenticated Home launchpad through Report.
- Phase 2 is complete: focused descriptor/registry/Home tests pass (14 tests) and typecheck is green.
- Started Phase 3 verification of every client endpoint and representative page behavior.
- Added and passed exhaustive client path/query coverage for all Instrument, Result, QC, and Report operations; typecheck is green after preserving exact rule-set literals in fixtures.
- Added and passed representative component tests for positive flow, JSON/SHA boundaries, exact Result adoption, capability-only UX, explicit network retry, trusted-context absence, and anonymous sign-in.
- Phase 3 is complete; all four workbench feature slices and their focused tests are implemented.
- Full frontend verification is green: 29 test files / 77 tests, typecheck, zero-warning lint, and production build.
- Updated the permitted repository contract expectations for 198 specs, 77 generated feature files, the new task artifact, and the approved topology entry.
- Python repository contracts are green: 42/42 tests.
- Completion spec gates are green: strict validation, SOURCE CURRENT, history, idempotent generate (`written=0`), and check.
- Exact .NET SDK 10.0.302 locked restore and Release solution build passed with 0 warnings / 0 errors.
- Architecture plus Instrument, Result, QC, and Report contract tests passed: 69 tests total.
- Phase 4 is complete; entering final allowed-path/diff audit and delivery.
- Final path audit passes for all 37 changed/new files; new clients and views contain no trusted organization/actor/tenant fields and no `latest` version lookup.

## Test Results

| Command | Result |
|---|---|
| `python -m tools.specgen validate` | PASS: 197 specs / 389 sources before the new Story |
| `python -m tools.specgen source-status` | PASS: SOURCE CURRENT |
| `python -m tools.specgen impact` | PASS: no drift or impact |
| `python -m tools.specgen validate --strict-warnings` | PASS: 198 specs / 389 sources after `ATC-WEB-002` |
| `python -m tools.specgen ready --story ATC-WEB-002@1.0.0` | PASS: READY |
| `python -m tools.specgen generate` | PASS: written=10, removed=0 |
| `corepack pnpm -C apps/web typecheck` after typed clients | PASS |
| `corepack pnpm -C apps/web exec vitest run src/features/lab-workbench/lab-json.spec.ts` | PASS: 3 tests |
| `corepack pnpm -C apps/web typecheck` after shared JSON foundation | PASS |
| `corepack pnpm -C apps/web typecheck` after Instrument/Result views | PASS |
| `corepack pnpm -C apps/web typecheck` after QC/Report views | PASS |
| Focused second feature, registry, and Home tests | PASS: 3 files / 14 tests |
| `corepack pnpm -C apps/web typecheck` after registration | PASS |
| `corepack pnpm -C apps/web exec vitest run src/features/lab-workbench/lab-second-clients.spec.ts` | PASS: 4 tests covering all module operations |
| `corepack pnpm -C apps/web typecheck` after client path tests | PASS |
| `corepack pnpm -C apps/web exec vitest run src/features/lab-workbench/lab-second-workbench-views.spec.ts` | PASS: 5 tests |
| `corepack pnpm -C apps/web test:unit` | PASS: 29 files / 77 tests |
| `corepack pnpm -C apps/web typecheck` | PASS |
| `corepack pnpm -C apps/web lint` | PASS: zero warnings |
| `corepack pnpm -C apps/web build` | PASS |
| `python -m unittest discover -s tests -p "test_*.py"` | PASS: 42 tests |
| Spec completion gate | PASS: 198 specs / 389 sources, history/check clean, second generate `written=0` |
| .NET 10.0.302 locked restore + Release build `-warnaserror` | PASS: 0 warnings / 0 errors |
| Architecture + Instrument/Result/QC/Report contract tests | PASS: 69 tests |

## Errors

| Error | Resolution |
|---|---|
| Default PowerShell decoding broke UTF-8 Story JSON parsing | Switched to explicit UTF-8 decoding and recorded the exact approved dependencies. |
| A combined planning update used a stale task-plan context line | Re-read the plan and reapplied exact changes; the failed patch made no partial edits. |
| `progress.md` contained only NUL bytes after the session boundary | Verified code/spec changes were intact, deleted the corrupt uncommitted file, and reconstructed the log from the plan and recorded session facts. |
| Client path tests passed, then typecheck rejected widened test-fixture rule-set strings | Kept the exact literals with `as const` and typed the Report line fixture; no production code change was required. |
| Python repository tests failed 3/42 on the new Story's expected count/task/topology inventories | Update the explicit permitted expectations and rerun all 42 tests; no generator or product behavior is failing. |
| System `dotnet` could not resolve required SDK 10.0.302 because only 9.0.305 is globally installed | Locate the exact previously provisioned SDK and invoke it directly; do not edit `global.json` or relax roll-forward. |
| Combined audit command returned exit 1 when forbidden-field `rg` found no matches | Record zero matches as PASS and rerun a dedicated allowed-path audit with explicit exit handling. |

## 5-Question Reboot Check

| Question | Answer |
|---|---|
| Where am I? | Phase 2: shared flow foundation |
| Where am I going? | Four operator workbenches, full verification, and merged delivery |
| What's the goal? | Continue the laboratory Web flow from Batch through controlled report issuance |
| What have I learned? | See `findings.md` |
| What have I done? | READY boundary, exact contract discovery, generated artifacts, and four typed clients |
