# Findings: DEV-026

## Gate and scope evidence

- Start gates pass: 185 spec versions / 389 source entries, SOURCE CURRENT, empty impact, and `ATC-TOY-003@1.0.0` READY.
- Approved sources are `BUS-TOY-005@1.0.0` and `AC-TOY-004@1.0.0`; DEV-027 and `ATC-TOY-004@0.1.0` remain out of scope and blocked.
- Allowed implementation paths cover Toy contracts/module/API host, Toy tests, architecture, domain docs, planning, verification scripts, and required lock files.
- Handoff audit confirmed the branch is still exactly at main delivery baseline `fa2bc9d`; only this planning directory and the active-plan pointer are uncommitted.

## Core semantics

- Artifact types are PACKAGING, LABEL, INSTRUCTION, and MARKETING_AGE_CLAIM.
- Every artifact version pins language, market, content hash, and immutable `{objectRef, hash}` image evidence; binary content never enters Toy storage.
- Reviews pin artifact/product/age-decision versions, market/language/scope, exact impact rule and rule set; DRAFT can append exactly one APPROVED or REJECTED decision.
- A trusted product/age change compares exact change scopes to review scopes through the pinned impact rule. Matching approved reviews append INVALIDATED; non-matching reviews retain validity with reconstructable evaluation evidence; UNKNOWN blocks reuse.
- New review versions reference the triggering change and prior review version without rewriting old artifacts, reviews, decisions, evaluations, or invalidations.
- Management requires `toy.label.manage`; decision requires `toy.label.review`. Actor separation remains an authorization-policy decision, not a Toy-module default.

## Detailed implementation decisions

- One artifact identity represents one product/type/language/market variant. It owns monotonic versions; every version freezes a SHA-256 content hash and one-or-more Toy-owned `{bucket, objectKey, hash}` evidence records.
- One review identity represents the review chain for one artifact identity. Each immutable review version pins the current artifact, product, effective AgeGradeDecision, exact scopes, rule, and rule set. Its decision is a separate append-only fact.
- Re-review version `n+1` must cite version `n` plus the exact product/age change already recorded as the prior version's invalidation or UNKNOWN evaluation. This prevents a client from fabricating a re-review cause.
- The approved scope-overlap algorithm is identified as `TOY-LABEL-SCOPE-OVERLAP@1`. Reviews may preserve a different exact rule reference, but evaluation then appends UNKNOWN instead of silently substituting the known algorithm.
- Trusted impact input records the change type/ref/version, exact change scopes, resulting product/AgeGradeDecision versions, rule reference and rule set. Exact `ID@version` intersection yields IMPACTED; empty intersection yields NOT_IMPACTED; missing/mismatched/unsupported facts yield UNKNOWN.
- Status derives from the newest artifact and review version. APPROVED is VALID only when versions match directly or a NOT_IMPACTED evaluation proves the requested resulting versions. INVALIDATED means RE_REVIEW_REQUIRED, a decision means REJECTED, and unassessed/unsupported/UNKNOWN means UNKNOWN. Every non-VALID result blocks downstream use.
- The third migration will add normalized artifact/image/review/decision tables plus versioned impact evaluation/invalidation evidence and append-only triggers. No published migration is modified.

## Delivered Toy implementation baseline

- Toy contracts currently live in one public `ToyContracts.cs`; age-grade and TestUnitPlan expose independent contract constants, request/result records, service interfaces, and status ports.
- Toy implementation already separates the DEV-025 TestUnitPlan service/store/migration from the original age-grade files. DEV-026 can follow that additive file pattern instead of expanding the already-large legacy service and persistence files.
- Existing services centralize actor/scope authorization, transaction coordination, append-only event persistence, audit-attempt fallback, Npgsql fail-closed mapping, and telemetry. LabelReview must preserve those established boundaries while using its two distinct capabilities.
- `ToyModule` applies migrations monotonically (`001` age grade, `002` TestUnitPlan), so DEV-026 will add `003` and register new service/store/status/impact ports without altering either published migration.
- Endpoint mapping uses shared JSON/problem helpers; current stable mappings are 403 authorization, 404 inaccessible/not-found, 409 expected-version conflict, 422 invalid terminal/use states, 503 persistence, and 400 validation. The four LabelReview error codes must be added deliberately.
- The HTTP authorization port already accepts an arbitrary capability string and exact organization/legal-entity/laboratory scope. Adding `toy.label.manage` and `toy.label.review` constants is sufficient; no authorization implementation fork is needed.
- DEV-025 provides reusable transaction patterns: advisory aggregate locks, exact-current-version checks, separate write/read authorization, success audit intent + outbox in the business transaction, and failure `audit_attempt` outside the rolled-back transaction.
- `IObjectStoragePort` is a platform public port with `PutAsync`, `OpenReadAsync`, and `DeleteAsync`. Label artifact creation can verify each immutable `ObjectReference` through `OpenReadAsync` and SHA-256 without touching storage implementation details or storing bytes in Toy.
- Existing TestUnitPlan store writes audit/outbox through platform ports only after inserting the business fact. DEV-026 should use the same transaction-bound writers and keep object verification before any LabelReview insert so an unreadable or hash-mismatched image leaves no business event.
- Toy contracts intentionally have no dependency on platform contracts. To preserve that boundary while representing `{objectRef, hash}`, use Toy-owned `ToyImageObjectReference(Bucket, ObjectKey)` plus evidence input/result records; the service translates it to platform `ObjectReference` for verification.
- All Toy projects use SDK default compile inclusion, so additive `ToyLabelReview*.cs` source and test files need no solution/project item edits. Contract and integration tests already reference the required Toy/platform assemblies.
- Toy unit tests directly exercise internal domain helpers through `InternalsVisibleTo`; a separate `ToyLabelReviewDomainTests.cs` can establish RED tests without constructing infrastructure.
- Toy contract tests use a production endpoint map plus replaced service stubs. DEV-026 should extend the factory with an `IToyLabelReviewService` stub, add five endpoint tests, include the five operation IDs in OpenAPI assertions, and extend stable error mapping assertions.
- Toy integration tests run one dedicated PostgreSQL database serially and use real module migrations/services with fixed actor/authorization/downstream ports. LabelReview coverage can live in a second class in the same collection, sharing or recreating the same deterministic database setup pattern.
