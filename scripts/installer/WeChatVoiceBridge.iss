; 微信语音桥 安装包脚本
;
; 需要 Inno Setup 6.5 以上版本。
; 编译前用 .\scripts\Publish.ps1 生成完整发布目录。
; 然后编译：
;   .\scripts\Build-Installer.ps1
;
; 安装包是按用户安装的：装到当前用户的 %LOCALAPPDATA%\Programs 下，
; 不需要管理员权限，别人双击就能装。
; 载荷在编译时由 Inno Setup 直接压进安装包，所以对外只有这一个 Setup.exe。

#define AppName "微信语音桥"
#define AppExe "WeChatVoiceBridge.exe"
#define AppPublisher "微信语音桥"
; Build-Installer.ps1 提供 AppVersion 和 PayloadDir。

[Setup]
AppId={{8F2C4B7A-5D16-4E93-B0A7-2C9D6E1F4A88}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
AllowNoIcons=yes
OutputDir=..\..\artifacts
OutputBaseFilename=WeChat-Voice-Bridge-V{#AppVersion}-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
SetupIconFile=..\..\src\VoiceBridge.App\app.ico
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} 安装程序

[Languages]
Name: "chinesesimp"; MessagesFile: "ChineseSimplified.isl"

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "WeChat Voice Bridge"
Name: "{group}\卸载 {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon; Comment: "WeChat Voice Bridge"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："

[Run]
Filename: "{app}\{#AppExe}"; Description: "启动 {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
const
  GwlExStyle = -20;
  WsExAppWindow = $00040000;
  WsExToolWindow = $00000080;
  WmSetIcon = $0080;

function GetWizardWindowStyle(Window: HWND; Index: Integer): Longint;
  external 'GetWindowLongW@user32.dll stdcall';
function SetWizardWindowStyle(Window: HWND; Index: Integer; Style: Longint): Longint;
  external 'SetWindowLongW@user32.dll stdcall';
function ExtractSetupIcons(FileName: String; Index: Integer;
  var LargeIcon, SmallIcon: LongWord; Count: LongWord): LongWord;
  external 'ExtractIconExW@shell32.dll stdcall';
function SetWizardIcon(Window: HWND; Message: LongWord; Kind: LongWord; Icon: LongWord): Longint;
  external 'SendMessageW@user32.dll stdcall';

procedure InitializeWizard;
var
  Style: Longint;
  LargeIcon, SmallIcon: LongWord;
begin
  if WizardSilent then
    Exit;
  Style := GetWizardWindowStyle(WizardForm.Handle, GwlExStyle);
  SetWizardWindowStyle(WizardForm.Handle, GwlExStyle,
    (Style or WsExAppWindow) and not WsExToolWindow);
  if ExtractSetupIcons(ExpandConstant('{srcexe}'), 0, LargeIcon, SmallIcon, 1) > 0 then
  begin
    if SmallIcon <> 0 then
      SetWizardIcon(WizardForm.Handle, WmSetIcon, 0, SmallIcon);
    if LargeIcon <> 0 then
      SetWizardIcon(WizardForm.Handle, WmSetIcon, 1, LargeIcon);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Answer: Integer;
begin
  if (CurUninstallStep = usUninstall) and (not UninstallSilent) then
  begin
    Answer := MsgBox('要不要一起删掉本机的设置和 API Key？' + #13#10 + #13#10 +
      '选「是」会清空设置、缓存和密钥；选「否」保留它们。',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2);
    if Answer = IDYES then
      DelTree(ExpandConstant('{localappdata}\WeChatVoiceBridge'), True, True, True);
  end;
end;
