# Task Plan: Lab Workbench Core Flow

## Goal

Deliver an authenticated, tested Web workbench for Scope, Quantity, Allocation, and Batch so an operator can execute the first half of the laboratory flow through existing versioned APIs.

## Current Phase

Phase 5: delivery

## Phases

### Phase 1: hard preflight and implementation boundary

- [x] Start from merged `main` on `codex/lab-workbench-core-flow`
- [x] Run repository-mandated validate, source-status, impact, and READY checks
- [x] Establish one minimal implementation task boundary permitting the Web paths; no OD/ADR/Seal work
- [x] Inspect exact API contracts and the existing receiving Web pattern
- **Status:** complete

### Phase 2: shared workbench foundation

- [x] Add common authenticated API/problem handling for laboratory features
- [x] Add explicit feature registration, navigation, and an operator landing page
- [x] Cover route, access, loading, empty, error, and retry states
- **Status:** complete

### Phase 3: core flow features

- [x] Implement Scope matrix create/revise/detail/eligibility UI
- [x] Implement Quantity account, ledger entry, detail, and availability UI
- [x] Implement Allocation request/detail/status UI
- [x] Implement Batch create/member/evidence/freeze/detail UI
- **Status:** complete

### Phase 4: verification

- [x] Run focused frontend unit/component tests and type/lint/build checks
- [x] Run repository Python and relevant .NET architecture/contract tests
- [x] Run the repository completion gate, including idempotent generation
- **Status:** complete

### Phase 5: delivery

- [x] Audit allowed paths and worktree
- [ ] Commit, publish, open PR, wait for CI, and merge when authorized
- **Status:** in_progress

## Constraints

- Do not edit the PRD source or generator-owned `generated/spec/**` directly.
- Do not create OD, ADR, Seal, release, deployment, or production migration work.
- Reuse existing versioned Scope, Quantity, Allocation, and Batch APIs; do not change backend business semantics.
- Modify only paths explicitly permitted by the approved implementation task boundary.
- Submit UI tests with each feature, including positive, negative, boundary, permission, recovery, and error states.

## Errors Encountered

| Error | Attempt | Resolution |
|---|---:|---|
| The planning skill's isolated shell initializer could not run because `bash` is unavailable | 1 | Do not retry; create the isolated planning files with `apply_patch` and keep the active-plan pointer explicit |
| A combined `rg` contract search was split into invalid path arguments by PowerShell quoting | 1 | Do not retry the compound regex; use `Select-String` with explicit patterns and simple `rg -e` searches |
| Workspace-injected Node.js 24.14.0 / pnpm 11.9.0 fail the repository's exact engine gate (24.14.1 / 10.34.5) | 1 | Do not bypass engine checks; locate or provision the repository-pinned runtime before rerunning frontend tests |
| Tried to read `apps/web/eslint.config.js`, but the repository uses `.mjs` | 1 | Use `apps/web/eslint.config.mjs`; do not retry the nonexistent path |
| GitHub CLI is not installed, so `gh auth status` cannot run | 1 | Commit and push with Git, then use the signed-in in-app browser for PR creation/merge instead of retrying `gh` |
| System `dotnet` exposes only SDK 9.0.305 while the repository pins 10.0.302 with roll-forward disabled, so restore/build cannot start | 1 | Do not alter `global.json`; locate an existing 10.0.302 installation or install the pinned SDK locally before rerunning .NET gates |
| Invoked the Story-only READY command with `OD-002@1.0.0`, so it correctly reported that no such Story exists | 1 | Inspect the existing decision object directly; rerun READY only for `ATC-WEB-001@1.0.0` after adding the dependency |
| Repository Python gate found four expected boundary assertions not updated for `ATC-WEB-001` (spec count, generated task inventory, approved-story inventory, and OD-002 tenant-context dependency) | 1 | Update the permitted repository-contract assertions and add the exact existing OD dependency to the Story, then regenerate and rerun the entire gate |
| Frontend lint found one `vue/html-self-closing` warning on the Quantity reason textarea, and warnings are fatal | 1 | Use the repository's required self-closing Vue textarea syntax |
| The Home view test mocked Vue refs as plain `{ value }` objects, so template auto-unwrapping never saw the authenticated status | 1 | Mock the already-unwrapped template-facing values directly and stub inactive Ant components |
| The recovery test's structural `:last-of-type` selector clicked a different secondary action, so the retry spy remained at one call | 1 | Select the explicit retry control by accessible text and assert it exists before clicking |
| A combined shared-foundation patch failed because a Unicode navigation-label context line did not match | 1 | Split the change into smaller patches and anchor registry-test edits on ASCII-only lines |
| A Node one-liner for reading the Unicode registry-test tail was broken by nested PowerShell quotes | 1 | Do not retry nested quoting; set PowerShell output encoding and use `Get-Content -Encoding UTF8` |
| The feature descriptor used `数量账本`, while the approved RED test fixes the navigation label as `数量账` | 1 | Align the descriptor and production registry expectation with the pre-existing RED test contract |
