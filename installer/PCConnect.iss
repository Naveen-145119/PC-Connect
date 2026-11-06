#ifndef AppVersion
#define AppVersion "0.2.0-preview.1"
#endif
#ifndef Rid
#define Rid "win-x64"
#endif
#define ProjectRoot AddBackslash(SourcePath) + ".."
#define PackageDir ProjectRoot + "\outputs\packages\PCConnect-" + Rid
[Setup]
AppId={{FCDBA490-29B3-4725-9539-D32CC9EA1287}
AppName=PC Connect
AppVersion={#AppVersion}
AppVerName=PC Connect {#AppVersion}
AppPublisher=Naveen Reddy
AppPublisherURL=https://github.com/Naveen-145119/PC-Connect
AppSupportURL=https://github.com/Naveen-145119/PC-Connect/issues
AppUpdatesURL=https://github.com/Naveen-145119/PC-Connect/releases
DefaultDirName={localappdata}\Programs\PC Connect
DefaultGroupName=PC Connect
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
#if Rid == "win-arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible and not arm64
ArchitecturesInstallIn64BitMode=x64compatible
#endif
MinVersion=10.0
WizardStyle=modern
SetupIconFile={#ProjectRoot}\src\PCConnect.App\app.ico
UninstallDisplayIcon={app}\PCConnect.exe
LicenseFile={#ProjectRoot}\LICENSE
OutputDir={#ProjectRoot}\outputs
OutputBaseFilename=PCConnect-Setup-{#Rid}
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
AppMutex=Local\PCConnect-{username}
VersionInfoVersion=0.2.0.0
VersionInfoProductName=PC Connect
VersionInfoDescription=PC Connect Setup
[Files]
Source: "{#PackageDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{userdesktop}\PC Connect"; Filename: "{app}\PCConnect.exe"; WorkingDir: "{app}"
Name: "{userprograms}\PC Connect\PC Connect"; Filename: "{app}\PCConnect.exe"; WorkingDir: "{app}"
Name: "{userprograms}\PC Connect\Setup guide"; Filename: "https://github.com/Naveen-145119/PC-Connect/blob/main/docs/SETUP.md"
[Run]
Filename: "{app}\PCConnect.exe"; Description: "Open PC Connect"; Flags: nowait postinstall skipifsilent
