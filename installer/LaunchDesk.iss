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
ShowLanguageDialog=yes
UsePreviousLanguage=yes
#ifdef SignedBuild
SignedUninstaller=yes
SignedUninstallerDir={#SigningDir}
#else
SignedUninstaller=no
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "Languages\ChineseSimplified.isl"

[CustomMessages]
english.DesktopIcon=Create a desktop shortcut
english.Shortcuts=Shortcuts:
english.LaunchApp=Launch LaunchDesk (may start configured services)
english.ExitBeforeUninstall=Exit LaunchDesk from its tray menu before uninstalling. Exiting stops managed processes. User configuration and logs will be preserved. Independently installed Windows services must be uninstalled separately.
chinesesimplified.DesktopIcon=创建桌面快捷方式
chinesesimplified.Shortcuts=快捷方式：
chinesesimplified.LaunchApp=启动 LaunchDesk（可能启动已配置的启动项）
chinesesimplified.ExitBeforeUninstall=请先从托盘菜单退出 LaunchDesk，再卸载。退出会停止桌面管理的进程；配置和日志将保留。独立安装的 Windows 服务需单独卸载。

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; GroupDescription: "{cm:Shortcuts}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "data\*,portable.flag"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\LaunchDesk"; Filename: "{app}\LaunchDesk.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\LaunchDesk"; Filename: "{app}\LaunchDesk.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\LaunchDesk.exe"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent unchecked

[Code]
function InitializeUninstall(): Boolean;
begin
  Result := not CheckForMutexes('{#InstallerMutex}');
  if not Result then
    MsgBox(ExpandConstant('{cm:ExitBeforeUninstall}'), mbError, MB_OK);
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
