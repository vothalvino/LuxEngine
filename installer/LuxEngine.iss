; Inno Setup script for LuxEngine.
; Build:  iscc /DAppVersion=1.0.0 /DSourceDir=..\publish installer\LuxEngine.iss

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif

#define AppName "LuxEngine"
#define AppExe "LuxEngine.exe"
#define AppPublisher "LuxEngine contributors"
#define AppUrl "https://github.com/vothalvino/LuxEngine"

[Setup]
AppId={{20B253BE-BC07-4689-85AE-FCB719434540}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=Output
OutputBaseFilename=LuxEngine-Setup
SetupIconFile=..\src\LuxEngine.App\Assets\LuxEngine.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
; Let the user choose "install for me only" (no admin rights needed) or for everyone.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ChangesAssociations=yes
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "association"; Description: "Open .luxshow files with LuxEngine"; GroupDescription: "File types:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Classes\.luxshow"; ValueType: string; ValueName: ""; ValueData: "LuxEngine.Show"; Flags: uninsdeletevalue; Tasks: association
Root: HKA; Subkey: "Software\Classes\LuxEngine.Show"; ValueType: string; ValueName: ""; ValueData: "LuxEngine show"; Flags: uninsdeletekey; Tasks: association
Root: HKA; Subkey: "Software\Classes\LuxEngine.Show\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExe},0"; Tasks: association
Root: HKA; Subkey: "Software\Classes\LuxEngine.Show\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: association

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
