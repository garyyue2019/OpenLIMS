param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$SpecgenArgs
)

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Push-Location $Root
try {
    python -m tools.specgen @SpecgenArgs
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
