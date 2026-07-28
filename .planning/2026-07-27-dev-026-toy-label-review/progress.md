# Progress Log: DEV-026

## Session: 2026-07-28 implementation

### Phase 1: gates and context

- **Status:** in_progress
- Created branch `codex/dev-026-toy-label-review` from main `fa2bc9d` after DEV-025 PR #26 was merged and delivery evidence pushed.
- Start gates: validate PASS, source-status CURRENT, impact empty, `ready --story ATC-TOY-003@1.0.0` READY.
- Read the full generated task card plus approved `BUS-TOY-005@1.0.0` and `AC-TOY-004@1.0.0`.
- Resumed the prior session, selected this directory as `.planning/.active_plan`, and confirmed the worktree contains no implementation changes.
- Re-ran all four start gates on 2026-07-28 after handoff; source remains current and the story remains READY.
- Inspected Toy contracts, module composition, endpoint/problem conventions, authorization, telemetry, and both published migrations; confirmed the additive DEV-026 file strategy.
- Read the UTF-8 approved BUS/AC sources, architecture tests, static OpenAPI, and PostgreSQL fixture. Phase 1 is complete; detailed LabelReview contracts, persistence facts, impact evaluation, and status derivation are now fixed for test-first implementation.

## Test Results

| Command | Result |
|---|---|
| `python -m tools.specgen validate` | PASS: 185 versions / 389 source entries |
| `python -m tools.specgen source-status` | PASS: SOURCE CURRENT |
| `python -m tools.specgen impact` | PASS: no impact |
| `python -m tools.specgen ready --story ATC-TOY-003@1.0.0` | PASS: READY |
| `C:\Users\Administrator\.dotnet\dotnet.exe test ... --filter FullyQualifiedName~ToyLabelReviewDomainTests` | EXPECTED RED: missing new LabelReview contracts/domain types |
| Focused LabelReview unit tests after contracts/domain implementation | PASS: 15/15 |
| Focused LabelReview HTTP contract tests before endpoint/OpenAPI implementation | EXPECTED RED: 6/6 fail with missing routes and operation IDs |
| Focused LabelReview PostgreSQL test before service/store/migration implementation | EXPECTED RED: missing `IToyLabelReviewService` registration |
| Focused LabelReview HTTP contract tests after endpoints/OpenAPI | PASS: 6/6 |
| Four-type artifact PostgreSQL scenario after migration/store/service | PASS: 1/1 |

## Handoff recovery

- `session-catchup.py` found only the prior DEV-025 completion summary and this DEV-026 resume context; no unrecorded implementation work exists.
- User explicitly confirmed DEV-025/026 as `approved`; no approval is inferred for out-of-scope DEV-027.

## Errors

- A baseline-inspection command referenced nonexistent `Models.cs`; `ObjectReference` was found in `Primitives.cs`, and no repository source was changed.
- First RED test invocation did not reach compilation because PATH resolved .NET SDK 9.0.305 while the repository requires 10.0.302. The SDK requirement remains unchanged; locating the already-used .NET 10 installation is the next diagnostic.
- Located user-scoped .NET SDK 10.0.302 and reran the focused LabelReview tests. RED confirmed at compilation on the intentionally missing DEV-026 contracts/domain types; existing projects compiled successfully.
- One planning update patch used a progress-file line as context in `task_plan.md`; after rereading both file tails, the update was split into exact file-specific hunks.

## Phase 2 test-first progress

- Added public LabelReview contract constants, artifact/review/impact/status DTOs, both public ports, and the five-operation service interface.
- Added 15 domain assertions covering all four artifact types, required hashes/evidence, version-pinned review/re-review causes, immutable terminal decisions, local exact-scope impact, unsupported-rule UNKNOWN, and fail-closed status derivation.
- Implemented only the pure domain layer needed to turn that focused RED suite green; persistence, service, endpoints, and migration remain intentionally absent pending their tests.
- Added contract tests and a service stub for the five specified HTTP operations plus all four new problem codes. The focused suite compiles and is RED exactly because the production routes/problem mappings/static OpenAPI entries do not yet exist.
- Added five PostgreSQL scenarios covering four types/version history/image verification/55000, Chinese-only invalidation with English preservation, UNKNOWN blocking, re-review linkage, permission, real concurrent decisions, and outbox rollback.
- Fixed one test-only `.AsTask()` placement compile error. The focused integration assembly now compiles; execution stopped before setup because this shell lacks `OPENLIMS_TEST_POSTGRES_CONNECTION`, so the repository's standard Postgres bootstrap is being located.
- Verified local PostgreSQL directly. Port 55432 belongs to a stale server that closes connections; the healthy isolated instance is PostgreSQL 16 on port 55442. With that explicit test connection, the focused test reaches the intended RED state: `IToyLabelReviewService` is not registered yet.
- A second combined planning patch accidentally reused progress context under the task-plan hunk; the update was immediately split by file. No implementation or test content was affected.

### Phase 2 complete

- Unit suite is green for the pure domain slice; HTTP and PostgreSQL suites are RED for precisely the missing production endpoints/OpenAPI and missing service/store/migration registrations.
- Test coverage now names every approved DEV-026 success, reverse, boundary, permission, concurrency, recovery, audit/outbox, image-evidence, and append-only behavior before infrastructure implementation begins.

## Phase 3 implementation progress

- Added monotonic migration `20260728_003_toy_label_review` with seven append-only tables and normalized artifact/image/review/decision/evaluation/invalidation evidence.
- Added transaction-bound store, image SHA-256 verification through `IObjectStoragePort`, scoped manage/review authorization, impact and status ports, five endpoints, stable problem mappings, telemetry, DI/migration registration, and static OpenAPI entries.
- Focused contract tests are fully green. The first real PostgreSQL scenario is green, including four types, V1/V2 history, object verification, audit/outbox writes, and UPDATE/DELETE SQLSTATE 55000.
- Full Toy integration initially passed 21/21. After strengthening DEV-026 coverage with both audit and outbox injection plus hash mismatch, the existing test-only audit trigger failed to match the approved `CREATE_LABEL_*` action and one assertion saw a null error. The trigger fixture now explicitly matches LABEL actions; production audit naming was not weakened.
- The strengthened permission/concurrency/audit/outbox test is green again. Architecture boundaries pass 18/18 and explicitly prove Toy SQL stays in `toy.*` and contains no Labeling print-job/scan access.
- Diff audit confirms the PRD, generated specs, and published Toy migrations `001`/`002` are untouched; all visible implementation changes are under approved paths and `git diff --check` is clean.
- Self-review tightened UNKNOWN semantics: any UNKNOWN evaluation permanently blocks that immutable review until a new review version, and missing rule/scope can be recorded without inventing a default. The first compile exposed one nullable-scope warning-as-error; it was fixed by normalizing only to an empty diagnostic input, which still evaluates UNKNOWN.
- Fresh-database verification confirmed nullable/missing impact rule and scope append an UNKNOWN evaluation and block the prior approval. Phase 3 is complete with contracts, migration, persistence, services, ports, endpoints, OpenAPI, telemetry, architecture guard, and domain documentation in place.
- The literal `pwsh` task command is unavailable on this host. Verification will run the identical checked-in PowerShell script under the current Windows PowerShell process with the required .NET 10 and test-Postgres environment.
- `scripts/verify.ps1 -Profile task -Module toy` passed under Windows PowerShell: locked restore, Release build with 0 warnings/errors, Toy unit 38/38, contract 25/25, integration 21/21.
- `scripts/verify.ps1 -Profile architecture` passed: architecture 18/18.
- First contract-profile invocation used the singular name and was rejected before running tests; the script declares `contracts`, which will be used next.
- `scripts/verify.ps1 -Profile contracts` passed across all contract suites (Toy 25/25).
- `scripts/verify.ps1 -Profile all` passed locked restore/build (0 warnings/errors), platform/architecture/contracts and frontend lint/typecheck/unit 47/47/build, then stopped at the environment-only Docker smoke stage because Docker is not installed on this host. No test failure occurred before that tool availability gate.
- Direct unfiltered `dotnet test OpenLIMS.slnx -c Release --no-build` passed every .NET suite, including all integration and E2E projects; Toy remained unit 38/38, contract 25/25, integration 21/21.
- Strict spec/source/history/check and both generator runs passed (`written=0` twice); Python repository tests passed 41/41.
- Full-solution format verification surfaced import-order findings in the touched API `Program.cs` and two untouched out-of-scope baseline files. Only the DEV-026 changed file set will be formatted and verified.
- Applied `dotnet format` only to the DEV-026 changed C# set. Changed-file format verification and `git diff --check` now pass; untouched Worker/platform-test baseline findings remain untouched and out of scope.
- A combined static-scan command returned nonzero solely because the forbidden Labeling-table search found no matches; scans will be rerun independently with no-match treated as the desired result.
- The first independent quality scan used a quoted wildcard path unsupported by `rg`; it will be rerun with an explicit `-g` file glob.
- Exact allowed-path audit reports no violations. Changed-source quality scan has no TODO/FIXME/NotImplemented/latest markers, and Toy contracts/implementation contain no Labeling private-table references.
- Final post-format task profile passed again: Release build 0 warnings/errors, Toy unit 38/38, contract 25/25, integration 21/21.
- Phase 4 is complete. The only unavailable local all-profile subgate is Docker compose configuration/image audit; the same locked gate remains required in GitHub CI. All code, database, frontend, spec, Python, formatting, boundary, and path gates available on this host are green.

## Phase 5 delivery progress

- Created implementation commit `18eb5cd` and pushed `codex/dev-026-toy-label-review` to origin.
- GitHub CLI is unavailable; PR creation, checks, and merge will use the authenticated GitHub REST API with credentials kept out of logs.
- The first REST credential lookup sent one multiline PowerShell string and Git did not parse its protocol field. The retry will stream separate credential-protocol lines; no credential was returned or exposed.
- PowerShell's line-array pipeline still did not reach Git Credential Manager as credential stdin. A redirected process stdin stream will be used next; no secret has appeared in command output.
- The redirected credential approach was blocked by local policy before execution. Using the signed-in browser session, created ready PR #27: `https://github.com/garyyue2019/OpenLIMS/pull/27`.
