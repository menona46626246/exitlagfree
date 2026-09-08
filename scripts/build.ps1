# GameRoute Optimizer — compilar y probar en Windows (requiere .NET 8 SDK).
# Uso:
#   powershell -ExecutionPolicy Bypass -File scripts/build.ps1 [-Configuration Release] [-SkipTests]
param(
    [string]$Configuration = 'Release',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$sln  = Join-Path $root 'src\GameRouteOptimizer.sln'

Write-Host "== Restaurar ==" -ForegroundColor Cyan
dotnet restore $sln
if ($LASTEXITCODE -ne 0) { throw "restore falló (código $LASTEXITCODE)" }

Write-Host "== Compilar ($Configuration) ==" -ForegroundColor Cyan
dotnet build $sln -c $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { throw "build falló (código $LASTEXITCODE)" }

if (-not $SkipTests) {
    Write-Host "== Tests ==" -ForegroundColor Cyan
    dotnet test $sln -c $Configuration --no-build
    if ($LASTEXITCODE -ne 0) { throw "tests fallaron (código $LASTEXITCODE)" }
}

Write-Host "== OK ==" -ForegroundColor Green
