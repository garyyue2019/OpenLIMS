# Progress Log: Lab Workbench Core Flow

## 2026-07-29

- User explicitly requested immediate functional development and rejected further optional governance work.
- Selected the first production UI slice: Scope + Quantity + Allocation + Batch.
- Updated local `main` to merged commit `6fa4b91` and created branch `codex/lab-workbench-core-flow`.
- No product code has been modified yet.
- Mandatory preflight passed: 196 specifications / 389 PRD sources, SOURCE CURRENT, empty impact, and all four existing business stories READY.
- Chosen minimal boundary: one Web-only task card reusing approved business contracts; no OD, ADR, Seal, release, backend, or migration work.
- Inspected the validator and READY implementation: a single Story with exact approved dependencies is sufficient for a Web-only delivery boundary.
- Added the minimal Web-only `ATC-WEB-001@1.0.0` / `DEV-032` task boundary; strict validation passed at 197 specs / 389 sources and READY returned READY.
- Generated the machine-owned outputs through specgen (`written=10`) and `specgen check` passed.
- Began implementation discovery: captured Web feature/auth/client/test patterns and the exact Scope contract.
- Found two Story API/context wording mismatches before product code; they will be corrected in the source Story and regenerated rather than papered over in the UI.
- Corrected the Web Story to the exact four module paths and clarified target object context versus trusted server identity context.
- Attempted the first frontend RED run; execution stopped before Vitest because the injected runtime is Node.js 24.14.0 / pnpm 11.9.0 while the repository pins Node.js 24.14.1 / pnpm 10.34.5. The engine gate will not be bypassed.
- Located the exact pinned runtime through system Node.js plus Corepack; frontend commands can run as `corepack pnpm ...`. Corrected the Web Story verification command to `pnpm -C apps/web test:unit`.
- Revalidated/regenerated after the Story command correction: strict validation and READY passed, generator wrote 6 derived files, and `specgen check` passed.
- Confirmed the intended RED state using the pinned runtime: 19 existing suites / 47 tests pass; three new suites fail only because their implementation modules do not yet exist.
- Completed the shared workbench foundation: authenticated JSON requests, normalized Problem Details/network/auth failures, exact capability checks, four stable routes/navigation entries, and explicit production registry composition.
- Focused shared-layer tests are GREEN (4 suites / 19 tests), and frontend typecheck passes.
- Added four typed clients covering every approved Scope, Quantity, Allocation, and Batch endpoint with exact rule-set/version queries and encoded stable IDs.
- Client coverage is GREEN (2 suites / 8 tests including shared error handling), and typecheck remains clean.
- Implemented all four operator views with structured inputs and structured results for Scope create/revise/detail/eligibility, Quantity account/entry/detail/availability, Allocation create/release/detail/status, and Batch create/member/evidence/freeze/detail/status.
- Added authenticated/anonymous/loading/permission/error/retry behavior and client-side boundary checks without submitting trusted organization/actor context.
- View behavior tests are GREEN (5 tests covering positive, boundary, permission, recovery, and anonymous routing), and typecheck passes.
- Added an authenticated operator launchpad linking the existing Receiving entry and all four new workbench modules.
- Full frontend verification is GREEN: 25 suites / 64 tests, typecheck, zero-warning lint, and production build.
- Repository completion gate is GREEN after adding the required direct OD-002 dependency and expected inventory assertions: strict validate, READY, source status, history, check, 42 Python tests, and idempotent second generation (`written=0`).
- Exact .NET SDK 10.0.302 restore and solution build with warnings-as-errors passed; architecture (19) plus Scope (10), Quantity (11), Allocation (11), and Batch (10) contract tests all passed.
- Audited the final worktree: every changed or new file is within ATC-WEB-001 allowed paths, generator-owned files were produced only by specgen, and `git diff --check` is clean.
- Tightened response interfaces to the exact backend DTO shapes, made the header/launchpad responsive and keyboard-visible, then reran the full frontend gate successfully.

## Test Results

| Command | Result |
|---|---|
| `python -m tools.specgen validate` | PASS: 196 specs / 389 sources |
| `python -m tools.specgen source-status` | PASS: SOURCE CURRENT |
| `python -m tools.specgen impact` | PASS: no drift or impact |
| READY for Scope/Quantity/Allocation/Batch stories | PASS: all four READY |
| `python -m tools.specgen validate --strict-warnings` after Web Story | PASS: 197 specs / 389 sources |
| `python -m tools.specgen ready --story ATC-WEB-001@1.0.0` | PASS: READY |
| `python -m tools.specgen generate && check` | PASS: written=10; check passed |
| `pnpm -C apps/web test:unit` | BLOCKED BEFORE TESTS: runtime engine mismatch (Node.js 24.14.0 / pnpm 11.9.0 vs pinned 24.14.1 / 10.34.5) |
| `corepack pnpm -C apps/web test:unit` (RED baseline) | EXPECTED FAIL: 19 existing suites / 47 tests pass; 3 new suites cannot resolve missing implementation modules |
| Focused lab API/access/descriptor/registry tests | PASS: 4 suites / 19 tests |
| `corepack pnpm -C apps/web typecheck` after shared layer | PASS |
| Focused shared API/client tests | PASS: 2 suites / 8 tests |
| `corepack pnpm -C apps/web typecheck` after typed clients | PASS |
| Focused workbench view tests | PASS: 1 suite / 5 tests |
| `corepack pnpm -C apps/web typecheck` after four views | PASS |
| `corepack pnpm -C apps/web test:unit` | PASS: 25 suites / 64 tests |
| `corepack pnpm -C apps/web typecheck` | PASS |
| `corepack pnpm -C apps/web lint` | PASS: zero warnings |
| `corepack pnpm -C apps/web build` | PASS: production bundle built |
| Spec completion gate | PASS: 197 specs / 389 sources, READY, SOURCE CURRENT, history/check clean, second generate `written=0` |
| `python -m unittest discover -s tests -p "test_*.py"` | PASS: 42 tests |
| .NET locked restore + Release build `-warnaserror` | PASS: 0 warnings / 0 errors |
| .NET architecture + 4 affected contract projects | PASS: 61 tests total |

## Errors

| Error | Resolution |
|---|---|
| `bash` was unavailable for the isolated planning initializer | Used `apply_patch` to create the same isolated planning artifacts; no retry |
| Compound contract `rg` pattern was misparsed by PowerShell | Switched to explicit `Select-String` patterns; no repository change |
| Frontend test command hit the exact Node.js/pnpm engine gate before Vitest | Locate or provision the pinned runtime; do not use `--ignore-engines` |
| Looked for `apps/web/eslint.config.js`, which does not exist | Located the actual `apps/web/eslint.config.mjs`; no retry of the wrong path |
| Delivery audit confirmed a clean diff, but GitHub CLI is unavailable | Use Git for commit/push and the signed-in browser for PR/merge operations |
| Backend restore/build could not start because the active system host has .NET SDK 9.0.305 but `global.json` requires exactly 10.0.302 | Locate or provision the pinned SDK; do not weaken roll-forward or edit repository runtime pins |
| Completion spec gates and idempotent generation passed, but Python repository tests failed 4/42 because the new approved Story was absent from expected inventories and lacked the required existing `OD-002@1.0.0` tenant-context dependency | Apply only the allowed Story/repository-contract updates, regenerate, and rerun all completion gates |
| Accidentally passed an OD identifier to the Story-only READY command | Inspect OD-002 directly and reserve READY for ATC-WEB-001 after its dependency update |
| Frontend typecheck passed, then lint stopped on one self-closing-style warning before build | Applied the exact Vue style rule; rerun lint/build without weakening the gate |
| Full frontend test run passed 63/64; the new Home test used a non-reactive ref-shaped mock that the template could not auto-unwrap | Mock template-facing auth/runtime values directly and rerun the full suite |
| Initial view test run passed 4/5; the recovery assertion used a selector that clicked the wrong button | Replaced it with an explicit accessible-text selector for `显式重试` |
| First combined foundation patch could not match one Unicode registry-test context line | No product changes were applied; split into smaller patches with stable ASCII anchors |
| Nested quoting broke a Node one-liner intended to inspect UTF-8 test text | Switch to PowerShell's explicit UTF-8 decoding/output encoding |
| First shared-layer GREEN run had one navigation-label mismatch (`数量账本` vs `数量账`) | Preserved the RED test's stable navigation contract and aligned the descriptor/registry test |
