# Development workflow

OpenLIMS uses direct engineering delivery. Product work does not wait for a
specification approval state, generated task card, source-drift review, impact
review, Seal, or path allowlist.

## Sources of context

- The product requirements document describes the intended product.
- Existing public contracts, migrations, tests, runtime behavior, and user
  feedback describe the delivered system.
- `spec/`, `generated/spec/`, and `docs/ai-development/` are retained as
  historical reference material. They are not executable gates.

When sources disagree, prefer behavior that is explicit, testable, secure, and
compatible with already published runtime contracts. Record consequential
engineering choices in code, tests, or a short ADR; no approval status is
required before implementation.

## Delivery loop

1. Inspect the affected modules, public contracts, migrations, API routes, Web
   views, and tests.
2. Define the smallest coherent behavior slice and its acceptance tests.
3. Implement through public module boundaries with explicit version binding.
4. Preserve runtime authorization, audit, append-only facts, concurrency, and
   failure-closed behavior.
5. Run focused tests, then the full engineering checks in `AGENTS.md`.
6. Commit, push, and merge without a separate specification approval step.

## Quality boundary

Removing development governance does not remove product controls. Database
migrations remain append-only, trusted actor context remains server-owned,
business evidence remains auditable, and unknown decisions remain blocked when
the runtime contract requires it.

