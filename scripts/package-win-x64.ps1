# GameRoute Optimizer — empaquetar publicación autocontenida win-x64 (app + helpers).
# Uso:
#   powershell -ExecutionPolicy Bypass -File scripts/package-win-x64.ps1 [-Configuration Release]
param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root 'dist\win-x64'

Write-Host "== Publicar App (win-x64) ==" -ForegroundColor Cyan
dotnet publish (Join-Path $root 'src\GameRouteOptimizer.App\GameRouteOptimizer.App.csproj') `
    -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:EnableCompressionInSingleFile=false `
    -o (Join-Path $dist 'app')
if ($LASTEXITCODE -ne 0) { throw "publish App falló ($LASTEXITCODE)" }

Write-Host "== Publicar CLI helper (win-x64) ==" -ForegroundColor Cyan
dotnet publish (Join-Path $root 'src\GameRouteOptimizer.Cli\GameRouteOptimizer.Cli.csproj') `
    -c $Configuration -r win-x64 --self-contained false `
    -o (Join-Path $dist 'app')
if ($LASTEXITCODE -ne 0) { throw "publish CLI falló ($LASTEXITCODE)" }

Write-Host "== Publicar Service (win-x64) ==" -ForegroundColor Cyan
dotnet publish (Join-Path $root 'src\GameRouteOptimizer.Service\GameRouteOptimizer.Service.csproj') `
    -c $Configuration -r win-x64 --self-contained false `
    -o (Join-Path $dist 'service')
if ($LASTEXITCODE -ne 0) { throw "publish Service falló ($LASTEXITCODE)" }

# Plantilla de configuración WireGuard de ejemplo.
Copy-Item (Join-Path $root 'examples\relay-ejemplo.conf') (Join-Path $dist 'app\relay-ejemplo.conf') -Force

Write-Host "== Publicado en $dist ==" -ForegroundColor Green
Write-Host "Para el instalador: instala Inno Setup y compila installer/GameRouteOptimizer.iss"
