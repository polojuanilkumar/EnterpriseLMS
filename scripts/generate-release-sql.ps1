param()
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$output = Join-Path $workspace 'artifacts/release/bin'
$deps = Join-Path $output 'LMS.Api.deps.json'
$runtime = Join-Path $output 'LMS.Api.runtimeconfig.json'
$assembly = Join-Path $output 'LMS.Infrastructure.dll'
$toolRoot = Join-Path $env:USERPROFILE '.dotnet/tools/.store/dotnet-ef/10.0.11'
$ef = Get-ChildItem -LiteralPath $toolRoot -Recurse -Filter ef.dll | Select-Object -First 1
if (-not $ef -or -not (Test-Path -LiteralPath $assembly)) { throw 'Build the solution into artifacts/release/bin with --no-restore and use the cached EF 10.0.11 tool. This script installs nothing.' }
$latest = '20261006135202_AddAuditEvents'
$prior = '20260929175839_AddPdfPresentations'
foreach ($item in @(
    @{ From = $prior; To = $latest; File = 'release-pending-up.sql' },
    @{ From = '0'; To = $latest; File = 'release-full-chain-up.sql' },
    @{ From = $latest; To = $prior; File = 'release-pending-down-review-only.sql' }
)) {
    $sql = Join-Path $workspace ('docs/sql/' + $item.File)
    & dotnet exec --depsfile $deps --runtimeconfig $runtime $ef.FullName migrations script $item.From $item.To --output $sql --assembly $assembly --startup-assembly $assembly --context LMSDbContext -- --offline
    if ($LASTEXITCODE -ne 0) { throw 'Offline SQL generation failed.' }
}
& dotnet exec --depsfile $deps --runtimeconfig $runtime $ef.FullName migrations has-pending-model-changes --assembly $assembly --startup-assembly $assembly --context LMSDbContext -- --offline
if ($LASTEXITCODE -ne 0) { throw 'Offline EF model differs from the migration snapshot.' }
