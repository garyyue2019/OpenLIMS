# Findings: Business Web Workbenches

## Starting state

- `main` is clean and synchronized with `origin/main` at `febc91c`.
- Existing Web features: platform shell, Receiving registration, laboratory core flow, laboratory second flow.
- Billing exposes 4 authenticated endpoints: create evidence, add adjustment, get evidence, get status.
- Labeling exposes 4 authenticated endpoints: create jobs, get job, controlled reprint, resolve scan.
- Receiving registration already embeds Identity, Exception, Release, and all Labeling actions, but Labeling has no independent navigation entry.
- Textile exposes 4 authenticated endpoints.
- Toy exposes 19 authenticated endpoints.

## Security and behavior constraints

- The browser must send the authenticated access token but must not invent trusted actor headers or identities.
- Exact object/rule versions come from user input or server responses; no “latest” inference.
- Writes update visible state only after a successful response; network and problem responses preserve safe inputs.
- Stable `errorCode`, `correlationId`, and `nextAction` are presented when supplied, without exposing tokens or raw sensitive content.

## Billing + Labeling boundary

- The correct runtime dependencies are `ATC-BILL-001@1.0.0` and `ATC-REC-002@2.0.0`; both are approved and expose the exact 8 requested operations.
- Billing uses only `billing.record`. Labeling capabilities are `receiving.label.print`, `receiving.label.scan`, `receiving.label.reprint`, and the server-enforced override capability beyond the threshold.
- Billing create input includes exact result group, contract baseline, currency, and ruleset versions. No version may be inferred by the UI.
- Labeling only supports object types `CT` and `RI`; reprint is one copy and requires printer plus non-empty reason.
- `DISPATCHED` is not physical verification, and `UNKNOWN` must not offer ordinary retry.

## Web implementation pattern

- Existing laboratory workbenches already provide reusable authenticated access, operation-state, JSON input/result, and RFC 9457 problem components under `features/lab-workbench`.
- Billing status is `GET /api/v1/billing-evidence/{id}/status?ruleSetVersion=BILLING-EVIDENCE%401.0.0`; the query is mandatory.
- Billing creation and adjustment can use the shared `labRequest` transport directly.
- The existing Receiving Labeling client remains the protocol source. Its error type will be enriched with correlation/detail/nextAction and normalized into the shared problem view, preserving current Receiving call signatures and tests.
- The production registry test asserts exact feature, route, and navigation order, so the new descriptor must be appended explicitly.
- Existing view tests mock the auth snapshot and typed clients, then verify successful response-driven state, local boundary rejection, capability disabling, safe anonymous login, and explicit network retry. DEV-034 tests will follow the same pattern.
- Existing responsive `.lab-*` styles already satisfy the new workbenches; no new global visual system is needed.

## Textile runtime boundary

- `ATC-TEX-004@1.0.0` is the approved runtime story for all 4 requested operations; it depends on the frozen DEV-011 contracts and runtime requirements BUS-TEX-006/007/008.
- Capabilities are `textile.sample-requirement.manage` for calculation/create/query and `textile.cutting-plan.approve` for approval.
- The fixed calculation ruleset is `TEXTILE-SAMPLE-REQUIREMENT@1.0.0`.
- Runtime inputs require stable requirement/plan IDs, exact positive versions, a requirement input hash, and fully versioned style/colorway/component/material/test item references.
- Decisions `INSUFFICIENT` and `UNKNOWN` must remain visibly blocked; the UI must not approve them or invent sample sufficiency.

## Toy runtime boundary

- All 19 requested operations are already approved across `ATC-TOY-001@1.0.0`, `ATC-TOY-002@1.0.0`, `ATC-TOY-003@1.0.0`, and the remediated conclusion runtime `ATC-TOY-005@1.0.0`.
- Operation groups: product age/accessibility (6), TestUnit/sample demand/allocation (4), label artifact/review/status (5), and conclusions (4).
- Fixed rulesets are `TOY-AGE-GRADE@1.0.0`, `TOY-TEST-UNIT-SAMPLE-DEMAND@1.0.0`, `TOY-LABEL-REVIEW@1.0.0`, and `TOY-CONCLUSION-COVERAGE@1.0.0`.
- Capabilities are explicitly separated: `toy.manage`, `toy.sample-demand.approve`, `toy.label.manage`, `toy.label.review`, `toy.conclusion.approve-item`, and `toy.conclusion.approve-scope`.
- Label-review status query pins product and age-grade versions plus market/language/artifact type/ruleset. Conclusion list query pins product ref and product version.
- Item and tested-scope conclusion inputs have materially different evidence/signature requirements. Tested-scope conclusion requires reauthentication reference, signing intent, and signed content hash; whole-product compliance and custom statements remain prohibited.
- `RecordAgeGradeDecisionRequest.approvedBy` is an approved business field in the public contract; it is not used as a browser-selected session actor or authorization claim.
- The four Toy routes are registered as one feature but intentionally use separate views so each operation group can enforce its own capability and version boundary.
- The existing product and TestUnit views already use the shared authenticated request/state/problem components and preserve explicit retry; the remaining label-review and conclusion views can follow the same response-driven pattern.
- The label-review client maps 5 endpoints and always injects the fixed status-query ruleset; the conclusion client maps 4 endpoints and types `customStatement` as forbidden plus whole-item conclusion as false-only.
- Runtime authorization uses `toy.label.manage` for artifact creation/versioning, review draft creation, and status lookup; `toy.label.review` is the distinct decision capability. The Web controls mirror this delivered service boundary while leaving the server authoritative.
- Toy adds one approved story/task and one generated feature, moving repository expectations from 200 to 201 specs and from 79 to 80 generated feature files.

## Receiving continuation boundary

- There is no public general-purpose received-item GET endpoint. The existing Identity GET can refresh item version for identities the user may evaluate, while Release only accepts an explicitly pinned item version and state.
- A safe continuation page therefore needs stable `receivedItemId`, explicit current item version/state inputs, and an optional stable `exceptionId`; it must never infer the latest object version.
- `IdentityAssessmentPanel` already reloads by received-item ID. `ReceivingExceptionPanel` needs an optional existing exception ID plus `exception.read` loading. `ReceivingReleasePanel` can be reused unchanged once the parent supplies the pinned version/state.
- The navigation-safe design is an index route for reopening by IDs plus a stable item route. The same page can use route state while keeping a direct navigation entry that does not require an unresolved dynamic parameter.
- Receiving continuation adds one approved story/task and one generated feature, moving repository expectations from 201 to 202 specs and from 80 to 81 generated feature files.
