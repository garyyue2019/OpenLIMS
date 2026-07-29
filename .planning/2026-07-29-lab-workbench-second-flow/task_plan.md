# Task Plan: Lab Workbench Second Flow

## Goal

Deliver an authenticated, tested Web workbench for Instrument, Result, QC, and Report so an operator can continue from an approved Batch through instrument import, result adoption, QC release, and controlled report issuance using the existing versioned APIs.

## Current Phase

Phase 5: delivery

## Phases

### Phase 1: hard preflight and implementation boundary

- [x] Start from merged `main` on `codex/lab-workbench-second-flow`
- [x] Run repository-mandated validate, source-status, and impact checks
- [x] Add one minimal approved Web-only task card with exact dependencies and allowed paths
- [x] Run strict validation and READY for the new Story
- [x] Inspect exact Instrument, Result, QC, Report contracts and existing Web patterns
- **Status:** complete

### Phase 2: shared flow foundation

- [x] Reuse the authenticated API/problem/access foundation from the first workbench batch
- [x] Add build-time feature registration, routes, navigation, and operator launchpad entries
- [x] Add shared structured-input helpers only where they materially reduce duplication
- **Status:** complete

### Phase 3: second-flow features

- [x] Implement Instrument register/rows/exception-resolution/detail/status UI
- [x] Implement Result group/observation/derivation/adoption-rule/adoption/detail/status UI
- [x] Implement QC run/result/verdict/impact/deviation/gates/release/detail/reportability UI
- [x] Implement Report draft/lines/gate/approval/hash/issuance/actions/detail/verification/version UI
- **Status:** complete

### Phase 4: verification

- [x] Run focused and full frontend tests, typecheck, lint, and production build
- [x] Run repository Python and relevant .NET architecture/contract tests
- [x] Run the full repository completion gate and confirm idempotent generation
- **Status:** complete

### Phase 5: delivery

- [ ] Audit allowed paths, generated ownership, diff quality, and worktree state
- [ ] Commit, push, open PR, wait for CI, and merge when all checks pass
- [ ] Sync local `main` and confirm the merge commit
- **Status:** in_progress

## Constraints

- Do not edit the PRD or generator-owned `generated/spec/**` directly.
- Do not add OD, ADR, Seal, deployment, or new backend business semantics.
- Reuse existing versioned Instrument, Result, QC, and Report APIs.
- Never submit trusted organization, actor, or authorization identity from the client.
- Capabilities are UX hints only; server authorization remains final.
- Modify only paths explicitly permitted by the approved Web task card.
- Submit positive, negative, boundary, permission, recovery, audit-facing, and regression tests with the implementation.

## Decisions Made

| Decision | Rationale |
|---|---|
| Continue the laboratory flow with Instrument, Result, QC, and Report | These existing backend modules form the direct path from Batch to controlled report issuance and currently have no Web feature registration. |
| Use one Web-only Story | Existing approved domain semantics are sufficient; the task only adds operator interaction and tests. |
| Reuse the first-batch shared API and access layer | It already normalizes auth, Problem Details, safe retries, and capability hints without duplicating trusted context. |
| Add a separate `LAB-WORKBENCH-SECOND-FLOW` descriptor in flow order | Keeps ownership explicit and prevents one feature descriptor from silently changing its established four-route contract. |
| Use typed clients plus operation-specific JSON samples | Covers all nested approved DTO fields while retaining exact endpoint typing and client-side validation. |

## Errors Encountered

| Error | Attempt | Resolution |
|---|---:|---|
| PowerShell default decoding corrupted UTF-8 Story JSON and made `ConvertFrom-Json` fail | 1 | Read repository JSON with explicit `-Encoding UTF8`; do not retry default decoding. |
| A combined planning update used a stale context anchor and `apply_patch` rejected the whole patch | 1 | Re-read the plan and apply exact, smaller contextual changes; no partial edit was retained. |
| `progress.md` became a 3439-byte all-NUL file across the session boundary | 1 | Verified repository changes, reconstructed the uncommitted log from recorded facts, and will verify the restored bytes before continuing. |
| New client path tests passed at runtime but typecheck widened shared rule-set literals to `string` | 1 | Preserve exact literals with `as const` and an explicit `AddReportLineRequest` return type; production clients were unaffected. |
| Python repository gate found three expected inventory assertions still fixed at `ATC-WEB-001` | 1 | Extend only the permitted spec count, generated artifact, and approved topology expectations for `ATC-WEB-002`, then rerun the full Python gate. |
| System `dotnet` exposes only SDK 9.0.305 while `global.json` requires 10.0.302 | 1 | Do not change the pin; locate and invoke the already-provisioned exact SDK used by the previous delivery. |
| Combined audit shell returned exit 1 because `rg` correctly found zero forbidden client fields | 1 | Treat zero matches as the desired result and run the path audit with explicit exit handling; no validation failed. |
