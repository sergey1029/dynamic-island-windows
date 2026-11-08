; Установщик Dynamic Island (Inno Setup 6).
; Сборка: сначала dotnet publish в папку publish\, затем
;   iscc /DMyAppVersion=1.0.0 installer\DynamicIsland.iss
; Готовый установщик появится в папке dist\.

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppName "Dynamic Island"
#define MyAppExe "DynamicIsland.exe"
#define MyAppUrl "https://github.com/sergey1029/dynamic-island-windows"
#define RunKey "Software\Microsoft\Windows\CurrentVersion\Run"

[Setup]
AppId={{8F3C2A51-6E2B-4C1D-9A7E-2D5B0C4F9E31}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher=sergey1029
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}/issues
AppUpdatesURL={#MyAppUrl}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableWelcomePage=no
; ставится в профиль пользователя и не требует прав администратора
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\dist
OutputBaseFilename=DynamicIsland-Setup-{#MyAppVersion}
SetupIconFile=..\Assets\icon.ico
LicenseFile=..\LICENSE
UninstallDisplayIcon={app}\{#MyAppExe}
UninstallDisplayName={#MyAppName}
WizardStyle=modern
WizardImageFile=..\Assets\wizard-large.bmp,..\Assets\wizard-large-2x.bmp
WizardSmallImageFile=..\Assets\wizard-small.bmp,..\Assets\wizard-small-2x.bmp
Compression=lzma2/max
SolidCompression=yes
; если остров запущен — предложить закрыть его перед установкой
AppMutex=DynamicIsland.SingleInstance
CloseApplications=yes

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
russian.AutostartTask=Запускать Dynamic Island вместе с Windows
english.AutostartTask=Start Dynamic Island with Windows
russian.SettingsShortcut=Настройки Dynamic Island
english.SettingsShortcut=Dynamic Island Settings
russian.LaunchApp=Запустить Dynamic Island
english.LaunchApp=Launch Dynamic Island
russian.StartupGroup=Запуск:
english.StartupGroup=Startup:

[Tasks]
Name: "autostart"; Description: "{cm:AutostartTask}"; GroupDescription: "{cm:StartupGroup}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\publish\{#MyAppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{autoprograms}\{cm:SettingsShortcut}"; Filename: "{app}\{#MyAppExe}"; Parameters: "--settings"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Registry]
; то же значение, что включает переключатель «Запускать вместе с Windows» в настройках
Root: HKCU; Subkey: "{#RunKey}"; ValueType: string; ValueName: "DynamicIsland"; ValueData: """{app}\{#MyAppExe}"""; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#MyAppExe} /F"; Flags: runhidden; RunOnceId: "StopIsland"

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
begin
  // без галочки автозапуска убираем старую запись (например, от предыдущей установки)
  if (CurStep = ssPostInstall) and not WizardIsTaskSelected('autostart') then
    RegDeleteValue(HKEY_CURRENT_USER, '{#RunKey}', 'DynamicIsland');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  // автозапуск мог быть включён и из настроек приложения — убираем в любом случае
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKEY_CURRENT_USER, '{#RunKey}', 'DynamicIsland');
end;
