# Development environment

The development dependency stack is intentionally disposable and uses only
synthetic values. One Compose project represents exactly one Organization
Group; do not use it as a shared multi-tenant environment.

The repository requires the exact .NET SDK from `global.json`, Node.js from
the root `package.json`, and pnpm through Corepack. A different SDK or package
manager version is an error, not a fallback.

## Start dependencies

1. Copy `deploy/compose/.env.example` to `deploy/compose/.env` and replace the
   placeholder values with locally generated, non-production credentials. Keep
   `OPENLIMS_MINIO_BUCKET` unique to this one disposable Organization Group.
2. Review `deploy/config/development.env.example`. It is a configuration
   reference, not a secret store. Set the application configuration through
   your local secret mechanism or environment variables.
3. Run `docker compose --env-file deploy/compose/.env -f deploy/compose/compose.yaml up -d`.
4. Check `docker compose --env-file deploy/compose/.env -f deploy/compose/compose.yaml ps`.

The imported Keycloak realm contains one synthetic-only user, `dev.operator`,
with the temporary password `synthetic-development-only-password`. On first
login, Keycloak requires a local password change. Its token carries the
`organization_group=development-group` claim and the `openlims-api` audience;
the `openlims-web` client is public and requires PKCE S256. These are only
disposable development fixtures, not credentials or identity design for any
other environment. MinIO bootstrap creates exactly the configured development
bucket and marks it non-public. Stop the stack with
`docker compose --env-file deploy/compose/.env -f deploy/compose/compose.yaml down`.
Appending `-v` deletes local development data and must never be used as an
evidence-retention procedure.

## Run the engineering shell

Start the API in one terminal with a synthetic deployment group. The group is
server-only configuration and is never selected by the browser.

```powershell
$env:Platform__OrganizationGroupId = "development-group"
$env:Platform__PostgresConnectionString = "Host=localhost;Port=5432;Database=openlims_development;Username=openlims_dev;Password=<local compose password>"
$env:Platform__PostgresCommandTimeoutSeconds = "10"
$env:Platform__OidcAuthority = "http://localhost:8080/realms/openlims-development"
$env:Platform__OidcAudience = "openlims-api"
$env:Platform__OidcMetadataTimeoutSeconds = "5"
$env:Platform__AllowInsecureDevelopmentOidc = "true"
$env:Platform__ObjectStorageEndpoint = "http://localhost:9000"
$env:Platform__ObjectStorageBucket = "openlims-development-development-group"
$env:Platform__ObjectStorageAccessKey = "local-minio-admin"
$env:Platform__ObjectStorageSecretKey = "<local compose password>"
$env:Platform__ObjectStorageProbeTimeoutSeconds = "5"
$env:Platform__AllowInsecureDevelopmentObjectStorage = "true"
$env:Platform__DependencyProbeTimeoutSeconds = "15"
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = "http://127.0.0.1:5080"
dotnet run --project src/host/api/OpenLIMS.Api/OpenLIMS.Api.csproj -c Release
```

Start the worker in a second terminal:

```powershell
$env:Platform__OrganizationGroupId = "development-group"
$env:Platform__PostgresConnectionString = "Host=localhost;Port=5432;Database=openlims_development;Username=openlims_dev;Password=<local compose password>"
dotnet run --project src/host/worker/OpenLIMS.Worker/OpenLIMS.Worker.csproj -c Release
```

Start the Web shell in a third terminal:

```powershell
corepack pnpm@10.34.5 --dir apps/web dev --host 127.0.0.1 --port 5173
```

Open `http://localhost:5173/` and use the **System status** route. Vite proxies
the `/health` and `/system` technical paths to the API. The current Spike does not expose a
business endpoint or business navigation.

## Login fixture

The imported realm is ready for a PKCE login after the Web runtime configuration
is supplied by the disposable local runtime configuration: client
`openlims-web`, loopback authority
`http://localhost:8080/realms/openlims-development`, scope `openid profile
email`, and audience `openlims-api`. Authenticate as `dev.operator`
with the temporary synthetic password stated above and complete the prompted
password change. Its token should contain the `openlims-api` audience and the
`organization_group` claim.

The Web shell accepts HTTP OIDC only when both the Web page and authority are
loopback addresses. Every non-loopback authority must use HTTPS. The API applies
the same rule and additionally requires `ASPNETCORE_ENVIRONMENT=Development`
plus the explicit `AllowInsecureDevelopmentOidc` flag. Do not enable either
development exception in a shared, verification, or production environment.

Apply the append-only platform infrastructure migration as a separate controlled
step before starting the API or Worker:

```powershell
dotnet run --project src/host/worker/OpenLIMS.Worker/OpenLIMS.Worker.csproj -c Release -- --apply-platform-migration
```

Normal API and Worker startup never changes the schema. Readiness performs fresh,
bounded checks against PostgreSQL including the current migration ID, Keycloak
discovery, and the configured private MinIO bucket. Any failure returns 503 with
a stable summary and does not expose addresses or credentials. The authenticated
`/system/status` endpoint performs the same checks after validating issuer,
audience, and the trusted `organization_group` claim.

## Failure recovery

If a dependency reports unhealthy, inspect its container logs with
`docker compose --env-file deploy/compose/.env -f deploy/compose/compose.yaml logs <service>`.
Correct only local synthetic configuration, restart that dependency, then wait
for `docker compose ... ps` to report health before restarting API or Worker.
Do not use a prior successful readiness response as evidence of recovery. For a
discarded local stack, `down -v` is acceptable only when no test or audit
evidence needs retention; recreate dependencies, rerun the controlled platform
migration step, and execute the verification gates.

## Verification gates

The verification scripts never skip a missing prerequisite. They fail with a
diagnostic if .NET, pnpm, Python, or a required project is unavailable.

```powershell
pwsh -NoProfile -File scripts/verify.ps1 -Profile task -Module platform
pwsh -NoProfile -File scripts/verify.ps1 -Profile architecture
pwsh -NoProfile -File scripts/verify.ps1 -Profile contracts
pwsh -NoProfile -File scripts/verify.ps1 -Profile all
```

```bash
bash scripts/verify.sh --profile task --module platform
bash scripts/verify.sh --profile architecture
bash scripts/verify.sh --profile contracts
bash scripts/verify.sh --profile all
```

`all` runs the same backend gates, then frozen frontend restore, frontend
checks, pinned Compose configuration and image-digest checks, and the focused
repository engineering contract tests. Use the individual profiles while
developing a specific concern; do not reinterpret a failed or unavailable
prerequisite as a passing gate.

This remains an engineering Spike, not a production deployment guide. It does
not authorize production secrets, production data, shared multi-group
infrastructure, automatic migrations, backup/recovery acceptance, or a
production identity provider.
