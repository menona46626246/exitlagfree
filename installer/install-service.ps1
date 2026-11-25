# install-service.ps1 — Instala GameRouteOptimizer.Service como servicio de Windows.
# EJECUTAR COMO ADMINISTRADOR. Requiere que WireGuard for Windows esté instalado
# (descarga oficial: https://www.wireguard.com/install/) para el modo túnel.
param(
    [string]$PublishDir = ""
)

$ErrorActionPreference = "Stop"
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
        ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "Este script debe ejecutarse como Administrador."
}

$serviceName = "GameRouteOptimizerService"
$root = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrEmpty($PublishDir)) {
    # Publicar el servicio (self-contained false: requiere runtime .NET 8).
    $PublishDir = Join-Path $root "publish\service"
    Write-Host "Publicando servicio en $PublishDir ..." -ForegroundColor Yellow
    dotnet publish src/GameRouteOptimizer.Service/GameRouteOptimizer.Service.csproj -c Release -o $PublishDir
    if ($LASTEXITCODE -ne 0) { exit 1 }
}

$exe = Join-Path $PublishDir "GameRouteOptimizer.Service.exe"
if (-not (Test-Path $exe)) { Write-Error "No se encontró $exe" }

# Si ya existe, detener y eliminar para reinstalar limpio.
$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Servicio existente detectado: deteniendo y eliminando..." -ForegroundColor Yellow
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $serviceName | Out-Null
    Start-Sleep -Seconds 2
}

Write-Host "Creando servicio '$serviceName'..." -ForegroundColor Yellow
sc.exe create $serviceName binPath= "`"$exe`"" start= demand displayName= "GameRoute Optimizer Service"
if ($LASTEXITCODE -ne 0) { Write-Error "sc.exe create falló" }

sc.exe description $serviceName "Gestiona túneles WireGuard, rutas y kill switch para GameRoute Optimizer. Se comunica con la UI por named pipe local 'gameroute-optimizer-svc'."

# El servicio debe correr como LocalSystem (por defecto) para poder tocar rutas y firewall.
Write-Host "Iniciando servicio..." -ForegroundColor Yellow
sc.exe start $serviceName
if ($LASTEXITCODE -ne 0) { Write-Warning "No se pudo iniciar; revisa el Visor de eventos." }

Write-Host "[OK] Servicio instalado. La app lo detectará automáticamente (pipe 'gameroute-optimizer-svc')." -ForegroundColor Green
Write-Host "  Desinstalar: installer\uninstall-service.ps1 (como administrador)."
