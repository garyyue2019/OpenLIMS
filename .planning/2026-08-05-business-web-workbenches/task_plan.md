# Task Plan: Business Web Workbenches

## Goal

Deliver the remaining approved runtime capabilities through authenticated, explicitly registered Web workbenches in this order: Billing + Labeling, Textile, Toy, and Receiving existing-object continuation. Preserve server-owned authorization, versions, evidence, and failure-closed semantics.

## Current Phase

Phase 6 - Receiving existing-object continuation

## Phases

### Phase 1: Billing + Labeling task boundary and readiness

- [x] Run repository pre-task gates and inspect exact public contracts.
- [x] Add a thin user-approved Web-only task card with precise dependencies and allowed paths.
- [x] Generate/check derived specifications and confirm the story is READY.
- **Status:** complete

### Phase 2: Billing + Labeling Web implementation

- [x] Implement typed API clients, access rules, recoverable operation state, and views for all 8 operations.
- [x] Register stable routes/navigation entries without disturbing existing features.
- [x] Add positive, negative, boundary, permission, recovery, audit-safe, and registry regression tests.
- **Status:** complete

### Phase 3: Billing + Labeling verification and handoff

- [x] Run focused Web tests, typecheck, lint, and build.
- [x] Run full repository completion gates, including deterministic generation.
- [x] Review diff and commit the completed batch on a `codex/` branch.
- **Status:** complete

### Phase 4: Textile Web workbench

- [x] Create/ready the Web-only task card.
- [x] Implement and test all 4 Textile operations and explicit feature registration.
- [x] Verify and commit the batch.
- **Status:** complete

### Phase 5: Toy full-flow Web workbench

- [x] Create/ready the Web-only task card.
- [x] Implement and test all 19 Toy operations and explicit feature registration.
- [x] Verify and commit the batch.
- **Status:** complete

### Phase 6: Receiving existing-object continuation

- [ ] Confirm the safe reopen contract boundary for existing received items and exceptions.
- [ ] Create/ready the task card and add usable continuation routes without duplicating registration panels.
- [ ] Verify and commit the batch.
- **Status:** in_progress

## Decisions

| Decision | Rationale |
|---|---|
| Deliver in four implementation batches | Keeps each READY boundary, test surface, and commit reviewable while preserving the requested order. |
| Reuse existing Receiving Labeling clients where compatible | Avoids duplicate protocol logic while adding the missing independent workflow entry. |
| Never synthesize actor identity, latest versions, or success states in the browser | These remain server-owned and are required to fail closed. |

## Errors Encountered

| Error | Attempt | Resolution |
|---|---:|---|
| Root planning files contain mojibake from an unrelated 2026-07-22 task | 1 | Created this isolated plan and left the user-owned root files unchanged. |
| Direct `pnpm` validation used Node 24.14.0 / pnpm 11.9.0, but the repository requires Node 24.14.1 / pnpm 10.34.5 | 1 | Resolve and invoke the bundled exact workspace runtime instead of repeating the direct command. |
| Full Python repository tests reported 4 WEB-003 integration assertions (spec count, task allowlist, approved delivery set, missing OD-002 dependency) | 1 | Update the explicit repository contract and story dependency, regenerate, then rerun the full gate. |
| Guessed obsolete Textile/Billing test directories while restoring test patterns | 1 | Use the actual `industry-workbench` and `business-workbench` file list for subsequent reads. |
| Phase-status patch initially omitted the plan's bulleted status marker and matched a mojibake rendering | 2 | Re-read exact UTF-8 lines with numbers and patched the stored format. |
