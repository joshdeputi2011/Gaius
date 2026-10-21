#ifndef AppVersion
  #define AppVersion "1.2.0"
#endif
[Setup]
AppId={{18C4411C-6AB5-418A-9EED-EC63833DCD01}
AppName=Gaius Office
AppVersion={#AppVersion}
AppPublisher=Gaius
DefaultDirName={localappdata}\Programs\Gaius Office
DefaultGroupName=Gaius Office
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\output
OutputBaseFilename=gaius-office-windows-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\Gaius.Office.exe
[Files]
Source: "..\output\windows\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\Gaius Office"; Filename: "{app}\Gaius.Office.exe"
[Run]
Filename: "{app}\Gaius.Office.exe"; Description: "Open Gaius Office"; Flags: nowait postinstall skipifsilent
