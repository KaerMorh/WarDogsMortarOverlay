#ifndef AppVersion
  #define AppVersion "0.6.1"
#endif
#ifndef PayloadDir
  #error PayloadDir must point to a clean release payload
#endif
#ifndef WebViewBootstrapper
  #error WebViewBootstrapper must point to the verified Microsoft installer
#endif
[Setup]
AppId={{C5940880-26AF-493E-B639-8535D0981B54}
AppName=WarDogs Overlay
AppVersion={#AppVersion}
AppPublisher=KaerMorh
AppPublisherURL=https://github.com/KaerMorh/WarDogsMortarOverlay
DefaultDirName={localappdata}\Programs\WarDogsOverlay
DefaultGroupName=WarDogs Overlay
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
DisableProgramGroupPage=yes
DisableDirPage=no
OutputDir=..\artifacts\releases
OutputBaseFilename=WarDogsOverlay-{#AppVersion}-win-x64-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\WarDogsOverlay.exe
CloseApplications=yes
RestartApplications=no
[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked
[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#WebViewBootstrapper}"; DestDir: "{tmp}"; Flags: deleteafterinstall
[Icons]
Name: "{group}\WarDogs Overlay"; Filename: "{app}\WarDogsOverlay.exe"
Name: "{autodesktop}\WarDogs Overlay"; Filename: "{app}\WarDogsOverlay.exe"; Tasks: desktopicon
[Run]
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "Installing Microsoft WebView2 (Internet required)..."; Flags: waituntilterminated; Check: NeedsWebView2
Filename: "{app}\WarDogsOverlay.exe"; Description: "Launch WarDogs Overlay"; Flags: nowait postinstall skipifsilent
[Code]
function NeedsWebView2: Boolean;
var Version: String;
begin
  Result := True;
  if RegQueryStringValue(HKLM32, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) then
    if (Version <> '') and (Version <> '0.0.0.0') then Result := False;
  if RegQueryStringValue(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) then
    if (Version <> '') and (Version <> '0.0.0.0') then Result := False;
end;
