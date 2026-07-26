[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('task', 'architecture', 'contracts', 'all')]
    [string]$Profile,
    [ValidateSet('platform', 'module-onboarding', 'receiving', 'labeling', 'scope', 'quantity', 'allocation', 'textile', 'batch', 'result', 'billing')]
    [string]$Module
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

function Require-Command([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' is not available. Install the locked toolchain before running verification."
    }
}

function Invoke-Gate([string]$Name, [scriptblock]$Action) {
    Write-Host "==> $Name"
    & $Action
    if ($LASTEXITCODE -ne 0) { throw "Gate '$Name' failed with exit code $LASTEXITCODE." }
}

function Invoke-DotNetTest([string]$Filter) {
    Require-Command dotnet
    Invoke-Gate "dotnet test ($Filter)" { dotnet test OpenLIMS.slnx -c Release --no-build --filter $Filter }
}

switch ($Profile) {
    'task' {
        $moduleFilters = @{
            'platform' = 'FullyQualifiedName~Platform'
            'module-onboarding' = 'Profile=module-onboarding'
            'receiving' = 'Profile=receiving'
            'labeling' = 'Profile=labeling'
            'scope' = 'Profile=scope'
            'quantity' = 'Profile=quantity'
            'allocation' = 'Profile=allocation'
            'textile' = 'Profile=textile'
            'batch' = 'Profile=batch'
            'result' = 'Profile=result'
            'billing' = 'Profile=billing'
        }
        if ([string]::IsNullOrWhiteSpace($Module) -or -not $moduleFilters.ContainsKey($Module)) { throw "The task profile requires -Module platform, -Module module-onboarding, -Module receiving, -Module labeling, -Module scope, -Module quantity, -Module allocation, -Module textile, -Module batch, -Module result, or -Module billing." }
        Require-Command dotnet
        Invoke-Gate 'dotnet restore (locked)' { dotnet restore OpenLIMS.slnx --locked-mode }
        Invoke-Gate 'dotnet build' { dotnet build OpenLIMS.slnx -c Release --no-restore -warnaserror }
        Invoke-DotNetTest $moduleFilters[$Module]
    }
    'architecture' { Invoke-DotNetTest 'FullyQualifiedName~Architecture' }
    'contracts' { Invoke-DotNetTest 'FullyQualifiedName~Contract' }
    'all' {
        & $PSCommandPath -Profile task -Module platform
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        & $PSCommandPath -Profile architecture
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        & $PSCommandPath -Profile contracts
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        Require-Command corepack
        Invoke-Gate 'pnpm install (frozen)' { corepack pnpm@10.34.5 install --frozen-lockfile }
        Invoke-Gate 'pnpm lint' { corepack pnpm@10.34.5 --dir apps/web lint }
        Invoke-Gate 'pnpm typecheck' { corepack pnpm@10.34.5 --dir apps/web typecheck }
        Invoke-Gate 'pnpm unit tests' { corepack pnpm@10.34.5 --dir apps/web test:unit }
        Invoke-Gate 'pnpm build' { corepack pnpm@10.34.5 --dir apps/web build }
        Require-Command docker
        Invoke-Gate 'docker compose config' { docker compose --env-file deploy/compose/.env.example -f deploy/compose/compose.yaml config --quiet }
        Invoke-Gate 'docker compose pinned image audit' {
            $images = (docker compose --env-file deploy/compose/.env.example -f deploy/compose/compose.yaml config --images)
            if (-not $images) { throw 'Compose configuration did not yield any images.' }
            foreach ($image in $images) {
                if ($image -notmatch '@sha256:[a-f0-9]{64}$') { throw "Compose image is not pinned to a SHA-256 digest: $image" }
            }
        }
        Require-Command python
        Invoke-Gate 'specgen check' { python -m tools.specgen check }
    }
}
