# create-installer.ps1 — Empaqueta una distribución zip (portable) de la app.
# Uso: powershell -ExecutionPolicy Bypass -File installer\create-installer.ps1
param(
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$dist = Join-Path $root "dist"
$ver = "1.0.0"
$rid = "win-x64"
$out = Join-Path $dist "GameRouteOptimizer-$ver-win-x64"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force -Path $dist | Out-Null

Write-Host "=== Publicando App WPF ($rid) ===" -ForegroundColor Cyan
$selfContainedText = if ($SelfContained) { "true" } else { "false" }
$appArgs = @("publish", "src/GameRouteOptimizer.App/GameRouteOptimizer.App.csproj",
    "-c", "Release", "-r", $rid, "--self-contained", $selfContainedText,
    "-o", "$out\app")
dotnet @appArgs
if ($LASTEXITCODE -ne 0) { exit 1 }

Write-Host "=== Publicando Servicio ===" -ForegroundColor Cyan
dotnet publish src/GameRouteOptimizer.Service/GameRouteOptimizer.Service.csproj -c Release -r $rid `
    --self-contained $selfContainedText -o "$out\service"
if ($LASTEXITCODE -ne 0) { exit 1 }

Write-Host "=== Copiando scripts, docs y ejemplos ===" -ForegroundColor Cyan
Copy-Item installer/install-service.ps1 $out
Copy-Item installer/uninstall-service.ps1 $out
Copy-Item -Recurse docs "$out\docs"
Copy-Item -Recurse examples "$out\examples"
Copy-Item README.md $out

Write-Host "=== Comprimiendo ===" -ForegroundColor Cyan
$zip = "$out.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $out -DestinationPath $zip

Write-Host "[OK] Distribución creada: $zip" -ForegroundColor Green
Write-Host "  Instalación: descomprimir -> ejecutar app\GameRouteOptimizer.exe" -ForegroundColor Green
Write-Host "  Modo túnel: ejecutar installer\install-service.ps1 como administrador." -ForegroundColor Green
