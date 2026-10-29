#ifndef AppVersion
  #define AppVersion "0.4.0-preview.2"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\current"
#endif
[Setup]
AppId={{89556E97-8D31-460D-89D0-43DA9B5C55E5}
AppName=IsLet-Todo
AppVersion={#AppVersion}
AppPublisher=syyyclover
AppPublisherURL=https://github.com/syyyclover/IsLet-Todo
DefaultDirName={localappdata}\Programs\IsLet-Todo
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir=..\artifacts\release
OutputBaseFilename=IsLet-Todo-{#AppVersion}-setup-x64
SetupIconFile=..\src\TaskIsland.WindowsIntegration\Assets\islet.ico
UninstallDisplayIcon={app}\TaskIsland.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
AppMutex=Local\TaskIsland.Resident

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\IsLet-Todo"; Filename: "{app}\TaskIsland.exe"; AppUserModelID: "TaskIsland.Desktop"

[Run]
Filename: "{app}\TaskIsland.exe"; Description: "启动 IsLet-Todo"; Flags: nowait postinstall skipifsilent

; User data, attachments and backups under TaskIsland are never removed by uninstall.
