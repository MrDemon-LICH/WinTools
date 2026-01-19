[Setup]
AppName=WinTools
AppVersion=1.0.0
AppPublisher=MrDemon
DefaultDirName={commonpf64}\WinTools
DefaultGroupName=WinTools
UninstallDisplayIcon={app}\WinTools.exe
Compression=lzma2
SolidCompression=yes
OutputDir=publish
OutputBaseFilename=WinTools.Installer
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableDirPage=no
UsePreviousAppDir=no

[Files]
Source: "WinTools.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\WinTools"; Filename: "{app}\WinTools.exe"
Name: "{commondesktop}\WinTools"; Filename: "{app}\WinTools.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Crear icono en el escritorio"; GroupDescription: "Iconos adicionales:"

[Run]
Filename: "{app}\WinTools.exe"; Description: "Ejecutar WinTools"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
begin
  // Cerrar WinTools si está ejecutándose
  if Exec(ExpandConstant('{app}\WinTools.exe'), '--close', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    // Esperar un momento para que se cierre
    Sleep(2000);
  end;

  // Buscar y cerrar cualquier proceso de WinTools que pueda estar ejecutándose
  if Exec('taskkill.exe', '/F /IM WinTools.exe /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    // Proceso terminado forzosamente si era necesario
    Sleep(1000);
  end;

  Result := True;
end;

[UninstallDelete]
Type: filesandordirs; Name: "{app}\*"
Type: files; Name: "{app}\WinTools.exe"
Type: dirifempty; Name: "{app}"
