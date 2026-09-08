; GameRoute Optimizer — script Inno Setup (6.x)
; 1) Publica primero: powershell -File scripts/package-win-x64.ps1
; 2) Compila este archivo con Inno Setup (o: iscc installer\GameRouteOptimizer.iss)

#define MyAppName "GameRoute Optimizer"
#define MyAppVersion "0.9.0"
#define MyAppPublisher "GameRoute Optimizer"
#define MyAppExeName "GameRouteOptimizer.App.exe"

[Setup]
AppId={{B2E1C4A9-7D3F-4E9B-9C2A-7A7E0D4F2B11}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\GameRouteOptimizer
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=..\dist\installer
OutputBaseFilename=GameRouteOptimizer-{#MyAppVersion}-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
; La app no requiere admin para instalarse; las operaciones de red piden UAC en tiempo de ejecución.

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"; GroupDescription: "Accesos directos:"

[Files]
Source: "..\dist\win-x64\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\dist\win-x64\service\*"; DestDir: "{app}\service"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Iniciar {#MyAppName}"; Flags: nowait postinstall skipifsilent

; Nota: la desinstalación NO borra %LOCALAPPDATA%\GameRouteOptimizer (juegos, relays,
; sesiones y claves cifradas son del usuario). Para borrarlo todo, elimina la carpeta a mano.
