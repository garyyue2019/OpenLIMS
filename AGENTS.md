# OpenLIMS Engineering Rules

These rules apply to the entire repository.

## Development Workflow

- Development does not require specification approval, READY status, source-drift review, impact review, task-card approval, Seal creation, or a path allowlist.
- The product requirements document, `spec/`, and `generated/spec/` are reference material. They do not authorize, block, or limit code changes.
- Read the existing implementation and tests before changing behavior. Prefer established module, contract, persistence, API, and frontend patterns.
- Keep changes scoped to the product capability being implemented, but expand to any repository path required to complete it correctly.

## Runtime Boundaries

- Do not access another module's private tables. Use versioned public ports, HTTP contracts, or events.
- Bind decisions to explicit object, rule, and evidence versions. Do not infer a latest business version from mutable runtime state.
- Preserve server-owned authorization, audit evidence, append-only business facts, concurrency protection, and failure-closed behavior.
- Published database migrations are immutable. Add a new migration for schema or data-semantic changes.
- Do not weaken tests, silently synthesize trusted data, delete failure evidence, or bypass runtime audit to make a build pass.

## Testing

- Add focused tests with the implementation. Coverage should include positive, negative, boundary, permission, concurrency, recovery, and audit behavior when applicable.
- Run the narrowest relevant tests while developing, then run the repository engineering checks before completion.

```powershell
dotnet restore OpenLIMS.slnx --locked-mode
dotnet build OpenLIMS.slnx -c Release --no-restore -warnaserror
dotnet test OpenLIMS.slnx -c Release --no-build
corepack pnpm@10.34.5 --dir apps/web lint
corepack pnpm@10.34.5 --dir apps/web typecheck
corepack pnpm@10.34.5 --dir apps/web test:unit
corepack pnpm@10.34.5 --dir apps/web build
python -m unittest tests.test_repository_contract -v
```

- Docker Compose configuration, pinned-image, dependency, migration, readiness, OIDC, and object-storage smoke checks remain required in CI.
