# Task Plan: DEV-026 Toy LabelReview invalidation and re-review

## Goal

Implement `ATC-TOY-003@1.0.0`: immutable four-type label artifacts by language/market/evidence version, version-pinned LabelReview decisions, trusted change-impact evaluation with local invalidation and UNKNOWN fail-closed behavior, re-review chains, and `IToyLabelReviewStatusPort@v1`.

## Current Phase

Phase 5: delivery

## Phases

### Phase 1: gates and implementation context

- [x] Run validate, source-status, impact, and ready gates
- [x] Read the generated task card and approved BUS/AC successors
- [x] Read the delivered DEV-024/025 Toy contract, service, persistence, migration, and tests as the implementation baseline
- [x] Confirm branch and every planned path against `allowed_paths`
- **Status:** complete

### Phase 2: failing tests and detailed design

- [x] Add domain tests for artifact validation/versioning, review pinning/decision, impact match/non-match/UNKNOWN, re-review linkage, and status derivation
- [x] Add HTTP contract tests for five API operations and stable problem mappings
- [x] Add PostgreSQL tests for four artifact types, local invalidation, history/re-review, permissions, concurrency, rollback, and append-only enforcement
- **Status:** complete

### Phase 3: implementation

- [x] Extend Toy public contracts, capabilities, status/impact ports, domain rules, services, endpoints, telemetry, and static OpenAPI document
- [x] Add a monotonic LabelReview migration and append-only store without modifying published migrations
- [x] Store only immutable image object references/hashes and never access Labeling private tables
- [x] Document the trusted impact-event and status boundaries
- **Status:** complete

### Phase 4: verification

- [x] Run task, architecture, contract, full .NET, strict spec/source/history, double-generate, check, and Python gates
- [x] Verify formatting, private-table scan, diff check, and exact allowed paths
- **Status:** complete

### Phase 5: delivery

- [ ] Commit, push, create PR, wait for all CI, and Squash merge
- [ ] Sync main and append delivery evidence
- **Status:** in_progress

## Constraints

- Only modify `ATC-TOY-003@1.0.0` `allowed_paths`.
- Do not edit generated specs, PRD, published migrations, Seals, or immutable evidence in place.
- Do not invent impact scope, permission, or self-approval defaults.
- Do not read or write Labeling print/scan private tables.
- Do not implement DEV-027 conclusions, report issuance, OCR/AI interpretation, or frontend pages.

## Errors Encountered

| Error | Attempt | Resolution |
|---|---:|---|
| Tried to read nonexistent `contracts/platform/.../Models.cs` while locating `ObjectReference` | 1 | `rg` located the definition in `Primitives.cs`; subsequent reads use that exact file. |
| `dotnet test` resolved system SDK 9.0.305 while `global.json` requires 10.0.302 | 1 | Located `C:\Users\Administrator\.dotnet\dotnet.exe` with SDK 10.0.302 and will invoke it explicitly; `global.json` remains unchanged. |
| Planning update patch mixed task-plan and progress contexts | 1 | Re-read both tails and applied file-specific exact hunks. |
| UNKNOWN integration test called `.AsTask()` on the request instead of the impact port `ValueTask` | 1 | Moved `.AsTask()` after `EvaluateAsync(...)`; test project then compiled. |
| Focused PostgreSQL test lacked `OPENLIMS_TEST_POSTGRES_CONNECTION` in this shell | 1 | Located the local isolated PostgreSQL 16 instance at `127.0.0.1:55442` and used the repository's admin-connection format; no integration test was skipped. |
| Stale PostgreSQL 18 process at port 55432 closed every connection during SSL setup | 2 | Verified with both Npgsql and `psql`, then selected the healthy existing isolated PostgreSQL 16 instance at port 55442 rather than restarting or mutating unrelated local services. |
| Planning update again mixed task-plan and progress contexts | 2 | Split the update into separate patches per file and stopped using multi-file context for ledger-only changes. |
| Refined label audit-failure test did not fire the existing `%TOY%`-only test trigger, so the assertion dereferenced a null error | 1 | Extended only the test failure trigger to cover `%LABEL%` audit actions, preserving production action names required by the task card. |
| Nullable missing-scope support triggered CS8604 warnings-as-errors in the domain matcher | 1 | Normalized the nullable input to an empty local collection before validation/matching; empty remains UNKNOWN rather than becoming a default scope. |
| Required task verification command could not find `pwsh` on this Windows host | 1 | Invoke the same checked-in `scripts/verify.ps1` with the active Windows PowerShell host and explicit .NET 10 PATH; do not skip or alter the script. |
| Invoked verification profile as singular `contract`; script ValidateSet requires `contracts` | 1 | Use the declared `contracts` profile shown by the parameter error. |
| `verify.ps1 -Profile all` reached the locked Docker smoke stage but this host has no `docker` command | 1 | Inspect the script, preserve the Docker gate as an external CI requirement, and run every available non-Docker full .NET/frontend/spec gate locally; CI will run the locked container stage. |
| Full-solution format verification reported import ordering in API `Program.cs` plus two untouched pre-existing files | 1 | Format and re-verify only DEV-026 changed C# files; do not mutate out-of-scope Worker or platform contract-test files. |
| Combined verification search treated an expected no-match `rg` exit code as tool failure | 1 | Run path and source scans independently and normalize no-match to a successful empty result. |
| Static quality scan passed a quoted wildcard as a literal path to `rg` | 1 | Use an `rg -g` file glob rooted at the approved directory. |
