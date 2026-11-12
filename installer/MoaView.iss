#ifndef AppVersion
  #define AppVersion "0.1.13"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif
#ifndef WebViewBootstrapper
  #define WebViewBootstrapper "..\.tools\MicrosoftEdgeWebview2Setup.exe"
#endif

[Setup]
AppId={{23C80132-6E95-43B2-AD9B-A283239A5947}
AppName=MoaView
AppVersion={#AppVersion}
AppVerName=MoaView {#AppVersion}
AppPublisher=MoaView
DefaultDirName={localappdata}\Programs\MoaView
DefaultGroupName=MoaView
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ChangesAssociations=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename=MoaView-{#AppVersion}-Setup-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\MoaView\Assets\Brand\MoaView.ico
UninstallDisplayIcon={app}\MoaView.exe
CloseApplications=yes
CloseApplicationsFilter=MoaView.exe
RestartApplications=no
SetupLogging=yes
VersionInfoVersion={#AppVersion}.0
VersionInfoProductName=MoaView
VersionInfoDescription=MoaView Setup

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "{cm:StartAtLogin}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"
Source: "{#WebViewBootstrapper}"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: NeedsWebView2
#ifdef ExplorerIdentityDir
Source: "{#ExplorerIdentityDir}\MoaView.ExplorerCommand.dll"; DestDir: "{app}"; MinVersion: 10.0.22000; Flags: ignoreversion
Source: "{#ExplorerIdentityDir}\MoaView.ExplorerIdentity.msix"; DestDir: "{app}"; MinVersion: 10.0.22000; Flags: ignoreversion
Source: "ExplorerIdentity\Update-ExplorerIdentity.ps1"; DestDir: "{app}"; MinVersion: 10.0.22000; Flags: ignoreversion
#endif

[Icons]
Name: "{group}\MoaView"; Filename: "{app}\MoaView.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\MoaView"; Filename: "{app}\MoaView.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Classes\*\shell\MoaView.Preview"; ValueType: string; ValueName: "MUIVerb"; ValueData: "{cm:ExplorerPreview}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\*\shell\MoaView.Preview"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\MoaView.exe"",0"
Root: HKCU; Subkey: "Software\Classes\*\shell\MoaView.Preview"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Single"
Root: HKCU; Subkey: "Software\Classes\*\shell\MoaView.Preview\command"; ValueType: string; ValueData: """{app}\MoaView.exe"" --preview ""%1"""
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "MoaView"; ValueData: """{app}\MoaView.exe"" --background"; Flags: uninsdeletevalue; Tasks: startup
; Retain an existing opt-in across upgrades and remove the app-managed value at uninstall, including entries enabled in Settings.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "MoaView"; Flags: uninsdeletevalue

[Run]
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; Description: "{cm:InstallWebView2}"; Flags: postinstall skipifsilent waituntilterminated; Check: NeedsWebView2
Filename: "{app}\MoaView.exe"; Description: "{cm:LaunchProgram,MoaView}"; Flags: nowait postinstall skipifsilent

#ifdef ExplorerIdentityDir
[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File ""{app}\Update-ExplorerIdentity.ps1"" -Remove"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveMoaViewExplorerIdentity"; MinVersion: 10.0.22000
#endif

[CustomMessages]
korean.ExplorerPreview=MoaView로 미리보기
english.ExplorerPreview=Preview with MoaView
korean.StartAtLogin=Windows 로그인 시 MoaView 자동 실행
english.StartAtLogin=Start MoaView when Windows starts
korean.ExplorerIdentityFailed=Windows 11 기본 메뉴를 등록하지 못했습니다. 패키지 서명 인증서를 신뢰하는지 확인하세요. 지금은 추가 옵션 표시에서 미리보기를 사용할 수 있습니다.
english.ExplorerIdentityFailed=The Windows 11 menu could not be registered. Check that Windows trusts the package signing certificate. Preview is available under Show more options.
korean.InstallWebView2=이미지·Rive·3D 미리보기용 Microsoft Edge WebView2 설치 (인터넷 필요)
english.InstallWebView2=Install Microsoft Edge WebView2 for image, Rive and 3D previews (internet required)

[Code]
#ifdef ExplorerIdentityDir
procedure CurStepChanged(CurStep: TSetupStep);
var
  Version: TWindowsVersion;
  ExitCode: Integer;
begin
  if CurStep <> ssPostInstall then Exit;
  GetWindowsVersionEx(Version);
  if (Version.Major < 10) or (Version.Build < 22000) then Exit;
  if Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' +
    ExpandConstant('{app}\Update-ExplorerIdentity.ps1') + '"', '', SW_HIDE,
    ewWaitUntilTerminated, ExitCode) and (ExitCode = 0) then
  begin
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\*\shell\MoaView.Preview');
    Log('Registered MoaView Windows 11 Explorer menu.');
  end else
  begin
    Log('MoaView Windows 11 menu registration failed. Kept the legacy menu.');
    if not WizardSilent then MsgBox(CustomMessage('ExplorerIdentityFailed'), mbInformation, MB_OK);
  end;
end;
#endif

function NeedsWebView2: Boolean;
var
  Version: String;
begin
  Result := True;
  if RegQueryStringValue(HKLM32, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) then
    if (Version <> '') and (Version <> '0.0.0.0') then Result := False;
  if RegQueryStringValue(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) then
    if (Version <> '') and (Version <> '0.0.0.0') then Result := False;
end;
