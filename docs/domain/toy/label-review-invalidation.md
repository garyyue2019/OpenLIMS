# Toy label artifact review, invalidation, and re-review

DEV-026 implements `ATC-TOY-003@1.0.0`. The Toy module owns immutable compliance artifacts for packaging, labels, instructions, and marketing age claims. It never reads or writes Labeling's print-job or scan tables.

## Artifact and review versions

- One artifact identity is one product/type/language/market variant. Versions start at 1 and append monotonically.
- Each version freezes a SHA-256 content hash and at least one immutable image `{bucket, objectKey, hash}` reference. The service reads the object through `IObjectStoragePort@v1`, verifies SHA-256, and stores no binary content.
- One review chain belongs to one artifact identity. Every review version pins the artifact version, current product version, effective AgeGradeDecision version, market, language, exact `ID@version` scopes, impact rule, and rule-set version.
- Review decisions are separate append-only facts. `DRAFT` accepts exactly one `APPROVED` or `REJECTED` decision under `toy.label.review`; the module does not invent a self-approval rule.
- A re-review must cite the prior review version and the exact product/age change already recorded as that version's invalidation or UNKNOWN evaluation.

## Trusted impact boundary

`IToyLabelReviewImpactPort@v1` is the trusted internal boundary for product or AgeGradeDecision version changes. A caller pins the organization, product, resulting product/age versions, change reference, exact change scopes, impact rule, and rule set. It is not exposed as a client HTTP invalidation command.

The approved `TOY-LABEL-SCOPE-OVERLAP@1` algorithm compares exact versioned scope references:

- intersection: append `IMPACTED` plus an immutable invalidation;
- no intersection: append `NOT_IMPACTED`, retain the approval, and preserve reconstructable evidence;
- unsupported/mismatched rule, missing context, or indeterminate versions: append `UNKNOWN`, return `TOY.LABEL_IMPACT_UNKNOWN`, and block reuse.

An impact event locks the product and evaluates each current approved review independently, so a Chinese scope change does not globally invalidate an unrelated English/market review.

## Status and evidence

`IToyLabelReviewStatusPort@v1` and the status API return `VALID`, `RE_REVIEW_REQUIRED`, `REJECTED`, or `UNKNOWN`. `VALID` requires the newest artifact and review plus either matching pinned product/age versions or a `NOT_IMPACTED` evaluation proving the requested resulting versions. Every other answer is a downstream denial; `UNKNOWN` is never permissive.

Artifact, review, decision, impact evaluation/invalidation, platform audit intent, and outbox envelope commit in one PostgreSQL transaction. Authorization failures, UNKNOWN, conflicts, inaccessible objects, and persistence failures append a separate `toy.audit_attempt`. Migration `20260728_003_toy_label_review` adds only new tables and rejects UPDATE/DELETE with SQLSTATE `55000`; migrations `001` and `002` remain unchanged.
