; Inno Setup script for Lumeshow.
; Build:  iscc /DAppVersion=1.0.0 /DSourceDir=..\publish installer\Lumeshow.iss

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif

#define AppName "Lumeshow"
#define AppExe "Lumeshow.exe"
#define AppPublisher "Lumeshow contributors"
#define AppUrl "https://github.com/vothalvino/Lumeshow"
; Until 1.8 the app was called LuxEngine. The AppId below is unchanged, so this setup replaces such an install.
#define AppGuid "20B253BE-BC07-4689-85AE-FCB719434540"
#define OldName "LuxEngine"
#define OldExe "LuxEngine.exe"

[Setup]
AppId={{{#AppGuid}}
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
OutputBaseFilename=Lumeshow-Setup
SetupIconFile=..\src\Lumeshow.App\Assets\Lumeshow.ico
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
Name: "association"; Description: "Open .luxshow files with Lumeshow"; GroupDescription: "File types:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"

[InstallDelete]
; Shortcuts with the old name, in case the old uninstaller could not be run (see [Code]).
Type: files; Name: "{autoprograms}\{#OldName}.lnk"
Type: files; Name: "{autodesktop}\{#OldName}.lnk"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Show files keep the .luxshow extension from the time the app was called LuxEngine.
Root: HKA; Subkey: "Software\Classes\.luxshow"; ValueType: string; ValueName: ""; ValueData: "Lumeshow.Show"; Flags: uninsdeletevalue; Tasks: association
Root: HKA; Subkey: "Software\Classes\{#OldName}.Show"; ValueType: none; Flags: deletekey dontcreatekey
Root: HKA; Subkey: "Software\Classes\Lumeshow.Show"; ValueType: string; ValueName: ""; ValueData: "Lumeshow show"; Flags: uninsdeletekey; Tasks: association
Root: HKA; Subkey: "Software\Classes\Lumeshow.Show\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExe},0"; Tasks: association
Root: HKA; Subkey: "Software\Classes\Lumeshow.Show\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: association

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
// Upgrading from LuxEngine (1.8 and older): the old program is removed with its own uninstaller, which
// deletes exactly what it installed, and Lumeshow goes into a folder with the new name next to it.
// Settings and shows are not touched here; Lumeshow brings the settings over when it first starts.
var
  OldDir: String;

function OldUninstaller: String;
begin
  Result := '';
  if not RegQueryStringValue(HKA, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\' + '{' + '{#AppGuid}' + '}_is1',
                             'UninstallString', Result) then
    Result := '';
  Result := RemoveQuotes(Result);
  if (Result = '') or not FileExists(Result) then
    Result := OldDir + '\unins000.exe';
end;

procedure InitializeWizard;
var
  Dir: String;
begin
  OldDir := '';
  // With an earlier install present, Setup has already put its folder here.
  Dir := RemoveBackslashUnlessRoot(WizardForm.DirEdit.Text);
  if FileExists(Dir + '\{#OldExe}') then
  begin
    OldDir := Dir;
    // ...\LuxEngine becomes ...\Lumeshow. A folder the user named themselves is kept.
    if CompareText(ExtractFileName(Dir), '{#OldName}') = 0 then
      WizardForm.DirEdit.Text := ExtractFilePath(Dir) + '{#AppName}';
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Uninstaller: String;
  Code: Integer;
begin
  Result := '';
  if OldDir = '' then Exit;

  // A program that is running cannot be deleted: that is how we notice LuxEngine is still open.
  if FileExists(OldDir + '\{#OldExe}') and not DeleteFile(OldDir + '\{#OldExe}') then
  begin
    Result := '{#OldName} is still running. Please close it and start this setup again.';
    Exit;
  end;

  Uninstaller := OldUninstaller;
  if FileExists(Uninstaller) then
  begin
    if not Exec(Uninstaller, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE, ewWaitUntilTerminated, Code) then
      Log('The old uninstaller could not be started: ' + SysErrorMessage(Code))
    else
      Log('The old uninstaller finished with code ' + IntToStr(Code));
  end;
end;

