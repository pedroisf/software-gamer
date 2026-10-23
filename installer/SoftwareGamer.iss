#define AppVersion "0.1.0"
[Setup]
AppId={{9F654FAB-31C7-4ADB-A595-F0827F7D9D39}
AppName=Software Gamer
AppVersion={#AppVersion}
AppPublisher=Software Gamer
AppPublisherURL=https://github.com/pedroisf/software-gamer
DefaultDirName={localappdata}\Programs\SoftwareGamer
DefaultGroupName=Software Gamer
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts
OutputBaseFilename=SoftwareGamer-Setup-x64
SetupIconFile=..\src\SoftwareGamer.App\Assets\app.ico
UninstallDisplayIcon={app}\SoftwareGamer.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=no
MinVersion=10.0.22000
[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
[Files]
Source: "..\artifacts\portable\SoftwareGamer.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\docs\GUIA_DO_USUARIO.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\docs\LIMITACOES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion
[Icons]
Name: "{group}\Software Gamer"; Filename: "{app}\SoftwareGamer.exe"
Name: "{group}\Desinstalar Software Gamer"; Filename: "{uninstallexe}"
[Run]
Filename: "{app}\SoftwareGamer.exe"; Description: "Abrir Software Gamer"; Flags: nowait postinstall skipifsilent
[Code]
function CheckRecovery(AppExe: String): Boolean;
var Code: Integer;
begin
  Result := True;
  if FileExists(AppExe) then
  begin
    if not Exec(AppExe, '--uninstall-recover', '', SW_HIDE, ewWaitUntilTerminated, Code) then
      Result := False
    else
      Result := Code = 0;
    if not Result then
      MsgBox('Feche o Software Gamer e resolva os ajustes e arquivos em quarentena na tela Recuperação antes de continuar.', mbInformation, MB_OK);
  end;
end;
function InitializeUninstall(): Boolean;
begin
  Result := CheckRecovery(ExpandConstant('{app}\SoftwareGamer.exe'));
end;
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not CheckRecovery(ExpandConstant('{app}\SoftwareGamer.exe')) then
    Result := 'Resolva a recuperação da versão instalada antes de atualizar.';
end;
