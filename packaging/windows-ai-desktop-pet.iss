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
AppPublisherURL=https://github.com/mawenshui/windows-ai-desktop-pet
AppSupportURL=https://github.com/mawenshui/windows-ai-desktop-pet/issues
AppUpdatesURL=https://github.com/mawenshui/windows-ai-desktop-pet/releases
AppComments=方块伙伴：桌面宠物、搜索、快捷启动、待办与提醒助手
VersionInfoCompany=Windows AI Desktop Pet Contributors
VersionInfoDescription=Windows AI Desktop Pet 安装程序
VersionInfoProductName=Windows AI Desktop Pet
VersionInfoProductVersion={#MyAppVersion}
SetupIconFile={#SourceDir}\assets\icons\windows-ai-desktop-pet.ico
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
#ifdef SignBuild
SignTool=aipet
SignedUninstaller=yes
SignedUninstallerDir={#SourceDir}\signed-uninstaller
#else
SignedUninstaller=no
#endif

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Windows AI Desktop Pet"; Filename: "{app}\WindowsAiDesktopPet.exe"
Name: "{autodesktop}\Windows AI Desktop Pet"; Filename: "{app}\WindowsAiDesktopPet.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加快捷方式："; Flags: unchecked

[Run]
Filename: "{app}\WindowsAiDesktopPet.exe"; Description: "启动 Windows AI Desktop Pet"; Flags: nowait postinstall skipifsilent
