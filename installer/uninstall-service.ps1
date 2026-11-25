# uninstall-service.ps1 — Detiene, elimina el servicio y restaura la red.
# EJECUTAR COMO ADMINISTRADOR.
$ErrorActionPreference = "Continue"
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
        ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "Este script debe ejecutarse como Administrador."
}

$serviceName = "GameRouteOptimizerService"

# 1) Emergencia: eliminar reglas de kill switch y restaurar cualquier ruta pendiente
#    ejecutando el binario del servicio una vez con --emergency (si existe).
#    (El servicio también limpia al recibir Ctrl+C o al detenerse.)

Write-Host "Deteniendo servicio..." -ForegroundColor Yellow
sc.exe stop $serviceName
Start-Sleep -Seconds 3

Write-Host "Eliminando servicio..." -ForegroundColor Yellow
sc.exe delete $serviceName

# 2) Limpieza manual de reglas de kill switch (por si quedaran huérfanas).
Write-Host "Eliminando reglas de firewall GameRouteKS_* (si existen)..." -ForegroundColor Yellow
netsh advfirewall firewall delete rule name="GameRouteKS_BlockAll" 2>$null | Out-Null
netsh advfirewall firewall delete rule name="GameRouteKS_Allow_WireGuard" 2>$null | Out-Null
netsh advfirewall firewall delete rule name="GameRouteKS_Allow_Service" 2>$null | Out-Null

# 3) Eliminar túneles de servicio de WireGuard creados por la app (nombre gameroute-*).
$wg = "C:\Program Files\WireGuard\wireguard.exe"
if (Test-Path $wg) {
    Write-Host "Eliminando túneles WireGuard 'gameroute-*'..." -ForegroundColor Yellow
    Get-ChildItem "C:\Program Files\WireGuard\Data\Configurations" -Filter "gameroute-*.conf.dpapi" -ErrorAction SilentlyContinue |
        ForEach-Object {
            $name = $_.BaseName -replace '\.conf\.dpapi$', ''
            & $wg /uninstalltunnelservice $name
        }
}

Write-Host "[OK] Servicio eliminado y red restaurada." -ForegroundColor Green
Write-Host "  Nota: los datos de usuario (perfiles, relays, logs) están en %APPDATA%\GameRouteOptimizer y NO se borran automáticamente."
