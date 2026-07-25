#define MyAppName "SOLIDWORKS ArUco 零件生成器"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "SolidworksArucoGenerator"
#define MyAppURL "https://github.com/ZHN0019/SolidworksArucoGenerator"
#define MyAppExeName "ArucoSolidWorksAddin.dll"
#define MyOutputBaseName "SOLIDWORKS_ArUco_Generator_Setup_1.1.0_x64"

[Setup]
AppId={{F6A86DEC-0222-4D1E-8B69-E5633667CDE7}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={commonappdata}\Codex\ArucoSolidWorksAddin
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
SetupArchitecture=x64
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
WizardSizePercent=110
Compression=lzma2/max
SolidCompression=yes
OutputDir=dist
OutputBaseFilename={#MyOutputBaseName}
SetupIconFile=assets\ArucoInstaller.ico
UninstallDisplayIcon={app}\ArucoInstaller.ico
UninstallDisplayName={#MyAppName}
VersionInfoVersion=1.1.0.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} 安装程序
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=yes
UsePreviousLanguage=yes
ChangesAssociations=no
ChangesEnvironment=no
MinVersion=10.0.17763

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\bin\x64\Release\net48\ArucoSolidWorksAddin.dll"; DestDir: "{app}"; Flags: replacesameversion
Source: "..\bin\x64\Release\net48\SolidWorks.Interop.sldworks.dll"; DestDir: "{app}"; Flags: replacesameversion
Source: "..\bin\x64\Release\net48\SolidWorks.Interop.swconst.dll"; DestDir: "{app}"; Flags: replacesameversion
Source: "..\bin\x64\Release\net48\SolidWorks.Interop.swpublished.dll"; DestDir: "{app}"; Flags: replacesameversion
Source: "..\README.md"; DestDir: "{app}"; DestName: "README.md"; Flags: ignoreversion
Source: "assets\ArucoInstaller.ico"; DestDir: "{app}"; Flags: ignoreversion

[Registry]
Root: HKLM64; Subkey: "SOFTWARE\SOLIDWORKS\Addins\{{78E6B279-EA99-4BD3-8C1B-CB1C8A309DF1}"; ValueType: dword; ValueName: ""; ValueData: "1"; Flags: uninsdeletekey
Root: HKLM64; Subkey: "SOFTWARE\SOLIDWORKS\Addins\{{78E6B279-EA99-4BD3-8C1B-CB1C8A309DF1}"; ValueType: string; ValueName: "Title"; ValueData: "ArUco 零件生成器"
Root: HKLM64; Subkey: "SOFTWARE\SOLIDWORKS\Addins\{{78E6B279-EA99-4BD3-8C1B-CB1C8A309DF1}"; ValueType: string; ValueName: "Description"; ValueData: "创建 DICT_4X4_50 双实体 ArUco 零件及同名 PNG、STEP"
Root: HKCU; Subkey: "SOFTWARE\SOLIDWORKS\AddInsStartup"; ValueType: dword; ValueName: "{{78E6B279-EA99-4BD3-8C1B-CB1C8A309DF1}"; ValueData: "1"; Flags: uninsdeletevalue

[UninstallRun]
Filename: "{win}\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"; Parameters: """{app}\{#MyAppExeName}"" /unregister /silent"; Flags: runhidden waituntilterminated skipifdoesntexist

[Code]
const
  DotNet48Release = 528040;

function IsSolidWorksInstalled: Boolean;
begin
  Result :=
    RegKeyExists(HKCR64, 'SldWorks.Application') or
    RegKeyExists(HKCR64, 'SldWorks.Application.33');
end;

function IsSolidWorksRunning: Boolean;
var
  ResultCode: Integer;
  PowerShellArgs: String;
begin
  PowerShellArgs :=
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command ' +
    '"if (Get-Process -Name SLDWORKS -ErrorAction SilentlyContinue) ' +
    '{ exit 10 } else { exit 0 }"';
  Result :=
    Exec(
      ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
      PowerShellArgs,
      '',
      SW_HIDE,
      ewWaitUntilTerminated,
      ResultCode) and
    (ResultCode = 10);
end;

function HasDotNet48: Boolean;
var
  Release: Cardinal;
begin
  Result :=
    RegQueryDWordValue(
      HKLM64,
      'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full',
      'Release',
      Release) and
    (Release >= DotNet48Release);
end;

function InitializeSetup: Boolean;
begin
  Result := False;

  if not HasDotNet48 then
  begin
    MsgBox(
      '安装此插件需要 Microsoft .NET Framework 4.8。',
      mbError,
      MB_OK);
    Exit;
  end;

  if not IsSolidWorksInstalled then
  begin
    MsgBox(
      '未检测到 64 位 SOLIDWORKS。请先安装 SOLIDWORKS 2025 或兼容版本。',
      mbError,
      MB_OK);
    Exit;
  end;

  Result := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if IsSolidWorksRunning then
    Result :=
      'SOLIDWORKS 正在运行。请保存工作并关闭所有 SOLIDWORKS 窗口，然后重新执行安装。';
end;

function InitializeUninstall: Boolean;
begin
  Result := not IsSolidWorksRunning;
  if not Result then
    MsgBox(
      'SOLIDWORKS 正在运行。请保存工作并关闭所有 SOLIDWORKS 窗口，然后重新执行卸载。',
      mbError,
      MB_OK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  RegAsmPath: String;
  AddinPath: String;
begin
  if CurStep <> ssPostInstall then
    Exit;

  WizardForm.StatusLabel.Caption := '正在注册 SOLIDWORKS 插件...';
  RegAsmPath :=
    ExpandConstant('{win}\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe');
  AddinPath := ExpandConstant('{app}\{#MyAppExeName}');

  if not Exec(
    RegAsmPath,
    '"' + AddinPath + '" /codebase /silent',
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode) then
    RaiseException('无法启动 64 位 RegAsm.exe。');

  if ResultCode <> 0 then
    RaiseException(
      Format('SOLIDWORKS 插件注册失败，RegAsm 返回代码 %d。', [ResultCode]));
end;
