#ifndef AppVersion
  #error AppVersion must be supplied by build-installer.ps1
#endif
#ifndef PublishDir
  #error PublishDir must be supplied by build-installer.ps1
#endif
#ifndef OutputDir
  #error OutputDir must be supplied by build-installer.ps1
#endif

#ifdef IsolationId
  #define SetupAppId "{{" + IsolationId + "}"
  #define InstallerMutex "Local\LaunchDesk-Accept-" + IsolationId
#else
  #define SetupAppId "{{9A2D379B-6292-4F1F-98D1-68F47AB28483}"
  #define InstallerMutex "Local\LaunchDesk-InstallerGuard"
#endif

[Setup]
AppId={#SetupAppId}
AppName=LaunchDesk
AppVersion={#AppVersion}
AppPublisher=hailaij
AppPublisherURL=https://github.com/hailaij/LaunchDesk
AppSupportURL=https://github.com/hailaij/LaunchDesk/issues
DefaultDirName={localappdata}\Programs\LaunchDesk
DefaultGroupName=LaunchDesk
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#OutputDir}
OutputBaseFilename=LaunchDesk-v{#AppVersion}-win-x64-setup
SetupIconFile=..\src\LaunchDesk.App\Assets\LaunchDesk.ico
UninstallDisplayIcon={app}\LaunchDesk.exe
LicenseFile=..\LICENSE
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
AppMutex={#InstallerMutex}
CloseApplications=no
RestartApplications=no
UsePreviousAppDir=yes
#ifdef SignedBuild
SignedUninstaller=yes
SignedUninstallerDir={#SigningDir}
#else
SignedUninstaller=no
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "data\*,portable.flag"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\LaunchDesk"; Filename: "{app}\LaunchDesk.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\LaunchDesk"; Filename: "{app}\LaunchDesk.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\LaunchDesk.exe"; Description: "Launch LaunchDesk (may start configured services)"; Flags: nowait postinstall skipifsilent unchecked

[Code]
function InitializeUninstall(): Boolean;
begin
  Result := not CheckForMutexes('{#InstallerMutex}');
  if not Result then
    MsgBox('Exit LaunchDesk from its tray menu before uninstalling. Exiting stops managed services. User configuration and logs will be preserved.', mbError, MB_OK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Command: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    { Remove only the login command owned by this installation. }
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'LaunchDesk', Command) then
      if CompareText(Command, '"' + ExpandConstant('{app}\LaunchDesk.exe') + '" --autostart') = 0 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'LaunchDesk');
  end;
end;
