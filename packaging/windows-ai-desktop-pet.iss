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

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  RunCommand: String;
  InstalledCommand: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    InstalledCommand := '"' + ExpandConstant('{app}\WindowsAiDesktopPet.exe') + '" --tray';
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'WindowsAiDesktopPet', RunCommand) then
      if CompareText(RunCommand, InstalledCommand) = 0 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'WindowsAiDesktopPet');
  end;
end;

function InitializeUninstall(): Boolean;
var
  ExitCode: Integer;
begin
  Result := True;
  { Silent uninstall always preserves personal data. Interactive default is also Keep. }
  if not UninstallSilent then
  begin
    if MsgBox('是否同时清理当前账户的设置、待办、提醒队列、快捷入口、索引、日志及已记录的 AI 凭据？此操作无法撤销。选择“否”保留数据。', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    begin
      if (not Exec(ExpandConstant('{app}\WindowsAiDesktopPet.exe'), '--remove-local-data --confirmed-by-uninstaller', '', SW_HIDE, ewWaitUntilTerminated, ExitCode)) or (ExitCode <> 0) then
      begin
        MsgBox('数据未完整清理，卸载已停止。请先退出桌宠，检查文件权限与凭据后重试；也可重新卸载并选择保留数据。', mbError, MB_OK);
        Result := False;
      end;
    end;
  end;
end;
