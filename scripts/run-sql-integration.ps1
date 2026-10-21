param([switch]$AllowDisposableSql, [string]$Image = 'mcr.microsoft.com/mssql/server:2022-latest')
$ErrorActionPreference = 'Stop'
if (-not $AllowDisposableSql) { throw 'Explicit -AllowDisposableSql is required. This run creates and migrates only a disposable Docker database.' }
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'Blocked: Docker is not installed/on PATH. No database fallback is allowed.' }
docker info --format '{{.OSType}}'
if ($LASTEXITCODE -ne 0) { throw 'Blocked: Docker daemon is unavailable. Use Linux containers.' }
docker image inspect $Image --format '{{.Id}}'
if ($LASTEXITCODE -ne 0) { throw 'Blocked: SQL Server image is not cached. Obtain it explicitly; this script never pulls images.' }
$previousOptIn = $env:LMS_SQL_INTEGRATION
$previousImage = $env:LMS_SQL_IMAGE
try {
    $env:LMS_SQL_INTEGRATION = '1'
    $env:LMS_SQL_IMAGE = $Image
    $workspace = Split-Path $PSScriptRoot -Parent
    # Windows PowerShell's native argument quoting can escape the closing quote
    # when a path containing spaces ends in a backslash, swallowing later options.
    $testOutput = Join-Path $workspace 'artifacts/release/bin'
    dotnet test (Join-Path $workspace 'LMS.Tests/LMS.Tests.csproj') --no-restore "-p:OutDir=$testOutput" --filter 'Category=SqlServerIntegration' --logger 'console;verbosity=normal'
    if ($LASTEXITCODE -ne 0) { throw 'SQL integration failed or prerequisites were blocked. Do not report this run as passing.' }
}
finally {
    $env:LMS_SQL_INTEGRATION = $previousOptIn
    $env:LMS_SQL_IMAGE = $previousImage
}
