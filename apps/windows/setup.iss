[Setup]
AppId=Fignimble.Windows
AppName=Fignimble
AppVersion={#AppVersion}
DefaultDirName={localappdata}\Fignimble\app
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\Fignimble.exe
WizardStyle=modern
Compression=lzma2
SolidCompression=yes

[Files]
Source: "{#AppSource}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\Fignimble"; Filename: "{app}\Fignimble.exe"; WorkingDir: "{app}"

[Run]
Filename: "{app}\Fignimble.exe"; Description: "Open Fignimble"; Flags: postinstall nowait skipifsilent
