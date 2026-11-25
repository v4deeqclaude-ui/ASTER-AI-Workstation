#define AppName "ASTER AI Workstation"
#define AppVersion "0.2.0"
#define AppPublisher "ASTER"
#define AppExeName "ASTER.Workstation.exe"

[Setup]
AppId={{A8D1D7B4-6C92-4B72-9E1A-0F8A6D2C5B31}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\ASTER AI Workstation
DefaultGroupName={#AppName}
OutputDir=..\artifacts\installer
OutputBaseFilename=ASTER-AI-Workstation-0.2-Setup-win-x64
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExeName}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Дополнительные параметры:"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Запустить ASTER AI Workstation"; Flags: postinstall nowait skipifsilent
