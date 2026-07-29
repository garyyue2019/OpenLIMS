# Findings: Lab Workbench Second Flow

## Requirements

- User approved the recommendation to implement the second laboratory Web batch: Instrument + Result + QC + Report.
- The delivery must be functional development, not optional governance work.
- The end state is an operator flow from Batch through instrument import, result adoption, QC release, and report issuance.

## Initial Findings

- Repository preflight is clean: 197 specs, 389 PRD sources, SOURCE CURRENT, and no direct or transitive impact.
- Current Web production registry contains platform shell, Receiving registration, and the first laboratory workbench batch only.
- Backend HTTP APIs already expose complete versioned slices for Instrument, Result, QC, and Report.
- Existing `lab-api.ts`, access notice, problem alert, feature descriptor, view-state helpers, and component test stubs are the implementation baseline.
- A new Web-only Story is required because existing domain task cards do not authorize the new Web feature paths.
- The exact approved dependencies are `ATC-INST-001@1.0.0`, `ATC-RESULT-001@1.0.0`, `ATC-QC-001@1.0.0`, `ATC-RPT-001@1.0.0`, and `ATC-RPT-002@1.0.0`, plus the shared platform/auth/architecture and direct `OD-002@1.0.0` context dependency.
- Report Web coverage must include both the issuance-gate slice and the signature/version-chain slice; using only `ATC-RPT-001` would omit five approved endpoints.
- The repository contract fixes the total spec count, generated task inventory, generated feature inventory, and approved topology inventory; `ATC-WEB-002` must update those explicit expectations through the permitted test path.
- Every Story must depend directly on `OD-002@1.0.0` and must not expose client-selected trusted tenant context.
- Instrument uses `INST-IMPORT@1.0.0` and capability `instrument.import`; its Web slice covers registration, parsed rows, exception resolution, detail, and status.
- Result uses `RESULT-ADOPTION@1.0.0` and capability `result.record`; its Web slice covers groups, observations, derivations, adoption rules, adoptions, detail, and status.
- Public-port request records contain trusted organization context for server-to-server evaluation, but HTTP clients must submit only endpoint path/query fields exposed by the endpoint implementation.
- QC uses `QC-IMPACT@1.0.0` and capability `qc.manage`; it has five exact release gates (`INVESTIGATION`, `IMPACT_SCOPE`, `VALIDITY_DECISION`, `ADOPTION_RULE`, `TECHNICAL_REVIEW`), and deviation approval never releases a block by itself.
- Report uses `RPT-ISSUANCE@1.0.0` and capability `report.manage`; report-line gate replay pins Result, QC, Instrument, Receiving, Scope, Allocation, Batch, accreditation, signatory, conformity, and traceability sources.
- Report scope partition and accreditation dimensions are fixed vocabularies; the UI must expose them without inventing combined accreditation defaults.
- A report line pins Result group/version, scope line/partition, trace references, accreditation reference plus six-dimensional claim, every QC run version, Instrument file/version, ReceivedItem, Scope, Allocation, and Batch versions.
- Issuance is a separate step after gate evaluation and approval: fetch the pending content hash, then submit exact reauthentication reference, signing intent, expected hash, and signatory business identifier; server identity remains authoritative.
- Controlled report actions are exactly `CORRECTION`, `SUPPLEMENT`, `WITHDRAWAL`, `VOID`, and `SUPERSESSION`; only correction and supplement produce a new version.
- Instrument status HTTP query is `expectedFileVersion` + `ruleSetVersion`; Result adoption status query is `expectedVersion` + `ruleSetVersion`. Trusted organization context is injected by the server in both cases.
- Instrument writes return 201 and its exception resolution path requires both file ID and exception ID. Result writes consistently return 201 and surface optional `gateSource` in Problem Details.
- Report version detail always returns the requested version's own snapshot, signature, and controlled actions; historical references never silently resolve to current content.
- QC reportability HTTP query is `expectedRunVersion`, `ruleSetVersion`, and `targetId`; every write returns the updated full `QcRunResult` with 201.
- Report issuance-gate HTTP query is `expectedReportVersion` + `ruleSetVersion`; pending-hash, verification, and version-detail reads require no client-supplied trusted context.
- Report draft/gate/submit writes return the updated `ReportResult`; issuance and controlled actions return their immutable records with 201. The UI can therefore replace its visible server snapshot only after successful responses.
- The first workbench batch already provides a reusable bearer/correlation client, Problem Details normalization, capability hints, auth-state notices, retry behavior, and positive/non-negative integer helpers.
- Existing module clients use exact TypeScript DTOs and URL-encode stable IDs; new clients should follow the same pattern and keep one file per module.
- Existing views are conventional Vue forms with server-response-driven result panels. For the much deeper second-batch payloads, a structured JSON operation editor with complete examples is the approved way to expose every field without hard-coding business defaults into dozens of conditional controls.
- The production registry composes feature descriptors explicitly; the second batch should be a separate descriptor with four routes, while reusing shared components from the same feature directory.
- Existing client tests assert every endpoint path and exact query string from one success fetcher; the second batch should add one path-coverage test per module and inspect representative request bodies where path alone is insufficient.
- Home currently links exactly Receiving through Batch and the registry test locks exact order. Both must be extended to Instrument → Result → QC → Report in business-flow order.
- Existing CSS already supports responsive three-column form grids, result/block panels, keyboard focus, and error notices. Only JSON editor monospace/min-height and operation-summary styling are needed.

## Technical Decisions

| Decision | Rationale |
|---|---|
| Stable task IDs will use the next unused Web and development IDs | `ATC-WEB-001` / `DEV-032` are delivered; the next boundary should be `ATC-WEB-002` / `DEV-033`. |
| Use explicit build-time feature composition | This matches the repository architecture and prevents runtime rule discovery. |
| Use structured JSON editors for deeply nested domain payloads, with client-side shape and required-field validation | The four APIs contain nested, versioned evidence structures; this exposes all approved fields without inventing business defaults. |
| Include both Report Stories as exact dependencies | The Web page must cover draft/gate approval and signature/version-chain operations without redefining either slice. |

## Resources

- `apps/web/src/features/lab-workbench/**`
- `apps/web/src/web-feature-registry.ts`
- `src/modules/instrument/**`
- `src/modules/result/**`
- `src/modules/qc/**`
- `src/modules/report/**`
- `contracts/instrument/**`
- `contracts/result/**`
- `contracts/qc/**`
- `contracts/report/**`
- `contracts/instrument/OpenLIMS.Contracts.Instrument/InstrumentContracts.cs`
- `contracts/result/OpenLIMS.Contracts.Result/ResultContracts.cs`
- `contracts/qc/OpenLIMS.Contracts.Qc/QcContracts.cs`
- `contracts/report/OpenLIMS.Contracts.Report/ReportContracts.cs`

## Issues Encountered

| Issue | Resolution |
|---|---|
| PowerShell default text decoding produced mojibake and invalid JSON for Story inspection | Use explicit `Get-Content -Encoding UTF8` for repository JSON and source files. |
| A combined planning-file patch used one stale context anchor and was rejected atomically | Re-read the current plan and split the update around exact current headings; no partial change occurred. |
| The uncommitted `progress.md` file contained only NUL bytes after a session boundary | Code and specs were unaffected; reconstruct the log from the persisted plan and session facts, then verify nonzero UTF-8 bytes. |

## Delivery Findings

- GitHub PR #30 targets `main` from `codex/lab-workbench-second-flow`.
- GitHub reports no conflicts and automatic mergeability; Application CI (Linux and Windows onboarding) plus the deterministic specification gate are running.
