#ifndef MyAppVersion
  #error MyAppVersion is required
#endif
#ifndef SourceDir
  #error SourceDir is required
#endif
#ifndef OutputDir
  #error OutputDir is required
#endif

[Setup]
AppId={{C9223114-E517-4E89-9900-CBD62767A94D}
AppName=Windows AI Desktop Pet
AppVersion={#MyAppVersion}
AppPublisher=Windows AI Desktop Pet
DefaultDirName={localappdata}\Programs\WindowsAiDesktopPet
DefaultGroupName=Windows AI Desktop Pet
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=windows-ai-desktop-pet-v{#MyAppVersion}-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\WindowsAiDesktopPet.exe
CloseApplications=yes
RestartApplications=no

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Windows AI Desktop Pet"; Filename: "{app}\WindowsAiDesktopPet.exe"
Name: "{autodesktop}\Windows AI Desktop Pet"; Filename: "{app}\WindowsAiDesktopPet.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加快捷方式："; Flags: unchecked

[Run]
Filename: "{app}\WindowsAiDesktopPet.exe"; Description: "启动 Windows AI Desktop Pet"; Flags: nowait postinstall skipifsilent
