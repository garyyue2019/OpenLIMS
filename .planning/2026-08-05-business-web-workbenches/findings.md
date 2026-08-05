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
