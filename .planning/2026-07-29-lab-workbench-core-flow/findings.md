# Findings: Lab Workbench Core Flow

## Product gap

- The backend exposes 13 business modules and 89 HTTP endpoints.
- The Web registry currently contains only the platform shell and Receiving feature.
- Scope, Quantity, Allocation, and Batch already expose complete versioned HTTP slices but have no operator UI.

## Repository boundary

- Existing delivered task cards for Scope, Quantity, Allocation, and Batch do not permit `apps/web/src/**`.
- Repository instructions require a reviewed task card before expanding allowed paths.
- The new boundary must stay minimal: Web feature code/tests, task planning, generated outputs produced by the generator, and repository assertions only.
- The four existing business stories are READY and source status/impact are clean, but none permits `apps/web/src/**`.
- A single Web-only story can depend on the already approved Scope, Quantity, Allocation, and Batch stories/acceptance criteria; it does not need a new business decision or backend semantic change.

## Implementation direction

- Follow the Receiving feature descriptor/client/access/component test pattern.
- Compose features explicitly at build time; do not add runtime feature discovery.
- Keep trusted organization/actor context server-owned and never accept it from UI forms.
- Surface RFC 9457 problem details with stable error code, correlation ID, and safe retry/next action.
- Use the next unused stable IDs `ATC-WEB-001` and `DEV-032` for this implementation boundary only.
- The READY gate only requires the Story itself to be approved/ready and every exact dependency to resolve to an approved object (with decided state for decisions).
- Therefore the minimal legal boundary is one approved Story depending on the four existing approved business Stories and their acceptance criteria; no new requirement, acceptance, or decision semantics are needed.

## Existing Web pattern

- Features are build-time descriptors with exact contract versions, owned routes, and owned navigation entries; composition rejects duplicate IDs, names, and paths.
- The auth store exposes a readonly snapshot; authenticated snapshots carry the OIDC user/access token and claims used only for UX hints.
- Receiving uses typed clients, an explicit capability helper, Vue Test Utils, and Vitest with Ant Design component stubs.
- No shared Problem Details client exists yet, so this task should introduce one for all four new features.

## Contract correction discovered during inspection

- Scope's actual base path is `/api/v1/scope-matrices`, not `/api/v1/scope/matrices`.
- Scope request object context legitimately includes legal entity/laboratory/customer/order/category target identifiers; the server separately owns organization, actor, and authorization decisions. The Web Story must distinguish submitted target context from trusted identity context.
- Exact API roots are `/api/v1/scope-matrices`, `/api/v1/quantity-accounts`, `/api/v1/test-object-allocations`, and `/api/v1/batches`.
- Write capabilities are `scope.approve`, `quantity.post`, `allocation.assign`, and `batch.manage`; they are UX hints only because the server remains authoritative.
- Allocation exposes create/release/get/status, not an append-version endpoint. Batch exposes create/member/evidence/freeze/get/status.
- The system Node.js and Corepack match the repository pins exactly (Node.js 24.14.1 and pnpm 10.34.5). The desktop-injected `pnpm.cmd` is the mismatched path, so local frontend gates must be invoked as `corepack pnpm ...` without weakening engine checks.
- The RED baseline is clean: all 19 pre-existing suites / 47 tests pass, while the three new suites fail only on missing `lab-api`, access, and feature descriptor modules.
- The registry is a static descriptor array consumed directly by the router and application navigation. Registration therefore needs one descriptor import plus one array entry; the four routes can remain owned by a single workbench feature.
- Authenticated requests can obtain the bearer token from `authSnapshot.value.user.access_token`; capability claims live in the OIDC user profile and must remain UX hints only.
- The exact public request/response records are centralized in the four `contracts/*/OpenLIMS.Contracts.*/*Contracts.cs` files, with API contract tests providing representative serialized payloads and query construction.
- Scope create/revise share `SubmitScopeMatrixVersionRequest`; Quantity exposes separate account and entry records; Allocation creation pins receiving/scope/quantity versions and has a reason-only release record; Batch has distinct create/member/evidence/freeze records.
- All four status/eligibility reads require positive `expectedVersion` plus exact `ruleSetVersion`; Quantity availability additionally requires a positive `requestedAmount`. The server injects organization context and accepts the correlation header through middleware.
- Backend Problem Details currently guarantees `errorCode` and `correlationId` (plus optional `gateSource`); the shared client must safely accept optional `detail`/`nextAction` without assuming every backend error provides them.
- The existing production registry test intentionally locks the previous two-feature registry and must be updated in the same change to expect the new workbench descriptor and its four explicit routes/navigation entries.
- Create semantics are contract-specific: Scope creation requires `expectedCurrentVersion=0`; Scope revision and Quantity/Batch append operations require positive current versions; Allocation creation uses the subject's current allocation version and valid examples start at 0.
- Contract-test examples confirm the UI defaults and domain vocabularies: Scope FEATURE_NODE/EVALUATED references, Quantity RECEIVED_ITEM/MASS/GRAM, Allocation version-pinned gates, and Batch ANALYTICAL plus SPECIMEN/QC_SAMPLE/CDS freeze causes.
- A single reusable access notice plus shared error panel covers authenticated read-only operation, exact-capability write hints, safe sign-in return paths, stable error/correlation display, and explicit retry for network/5xx failures across all four views.
- Repository CI uses the exact .NET SDK 10.0.302 and ultimately runs `dotnet test OpenLIMS.slnx -c Release --no-build`; focused verification can run architecture plus the four affected contract projects before the solution-wide gate.
- The repository contract intentionally requires every Story mentioning client/server tenant context to depend directly on existing `OD-002@1.0.0`; indirect dependency through the platform Story is not accepted.
