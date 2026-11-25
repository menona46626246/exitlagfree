# build.ps1 — Compilación completa en Windows (requiere .NET SDK 8).
# Uso:
#   powershell -ExecutionPolicy Bypass -File scripts\build.ps1            # Debug
#   powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Release   # Release
param(
    [switch]$Release
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$config = if ($Release) { "Release" } else { "Debug" }
Write-Host "=== GameRoute Optimizer — build $config ===" -ForegroundColor Cyan

dotnet --version | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Error "No se encontró el SDK de .NET 8. Instálalo desde https://dotnet.microsoft.com/download/dotnet/8.0"
}

Write-Host "[1/3] Restaurando paquetes..." -ForegroundColor Yellow
dotnet restore GameRouteOptimizer.sln
if ($LASTEXITCODE -ne 0) { exit 1 }

Write-Host "[2/3] Compilando solución (incluye la App WPF)..." -ForegroundColor Yellow
dotnet build GameRouteOptimizer.sln -c $config
if ($LASTEXITCODE -ne 0) { exit 1 }

Write-Host "[3/3] Ejecutando tests..." -ForegroundColor Yellow
dotnet test src/GameRouteOptimizer.Tests/GameRouteOptimizer.Tests.csproj -c $config --no-build --verbosity minimal
if ($LASTEXITCODE -ne 0) { exit 1 }

Write-Host "[OK] Build y tests OK. Ejecuta la app con: dotnet run --project src/GameRouteOptimizer.App -c $config" -ForegroundColor Green
