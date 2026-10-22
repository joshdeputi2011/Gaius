#ifndef AppVersion
  #define AppVersion "1.2.1"
#endif
#ifndef AppSource
  #define AppSource "..\output\windows"
#endif
[Setup]
AppId={{18C4411C-6AB5-418A-9EED-EC63833DCD01}
AppName=Gaius Office
AppVersion={#AppVersion}
AppPublisher=Gaius
AppComments=A native workspace for documents, spreadsheets, presentations, PDFs and images.
VersionInfoDescription=Gaius Office Setup
SetupIconFile=Assets\gaius-suite.ico
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
WizardStyle=modern dynamic windows11
WizardSizePercent=120,120
WizardResizable=yes
WizardImageFile=InstallerAssets\wizard.png
WizardImageFileDynamicDark=InstallerAssets\wizard-dark.png
WizardSmallImageFile=InstallerAssets\wizard-small.png
WizardSmallImageFileDynamicDark=InstallerAssets\wizard-small.png
WizardImageStretch=yes
WizardImageBackColor=$854417
WizardImageBackColorDynamicDark=$2E170C
DisableWelcomePage=no
DisableProgramGroupPage=yes
DisableReadyPage=no
ShowLanguageDialog=no
UninstallDisplayIcon={app}\Gaius.Office.exe
[Messages]
WelcomeLabel1=Meet Gaius Office
WelcomeLabel2=Make space for your next idea with Write, Sheets, Slides, PDF and Image.%n%nThis will install Gaius Office for your Windows account. Your saved documents and settings stay in your personal workspace.
FinishedHeadingLabel=Your workspace is ready
FinishedLabel=Gaius Office has been installed. Start with a template, open a file, or explore your recent work.
ReadyLabel1=Ready to make Gaius Office your workspace.
ClickInstall=Choose Install to continue, or Back to review the installation folder.
[Files]
Source: "{#AppSource}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\Gaius Office"; Filename: "{app}\Gaius.Office.exe"
[Run]
Filename: "{app}\Gaius.Office.exe"; Description: "Open Gaius Office"; Flags: nowait postinstall skipifsilent
[UninstallDelete]
Type: files; Name: "{group}\Gaius Write.lnk"
Type: files; Name: "{group}\Gaius Sheets.lnk"
Type: files; Name: "{group}\Gaius Slides.lnk"
Type: files; Name: "{group}\Gaius PDF.lnk"
Type: files; Name: "{group}\Gaius Image.lnk"
[Code]
function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo,
  MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := 'Gaius Office for Windows' + NewLine + NewLine +
    'Write  -  documents and reports' + NewLine +
    'Sheets  -  spreadsheets, formulas and charts' + NewLine +
    'Slides  -  presentations and visual stories' + NewLine +
    'PDF and Image  -  conversion, compression and editing' + NewLine + NewLine +
    'Installation folder:' + NewLine + Space + ExpandConstant('{app}') + NewLine + NewLine +
    'Installed for your account. Your saved files and settings are preserved.';
end;
