; MAGI ShiftOptimizer — Inno Setup スクリプト（setup.exe 生成用）
;
; CI（.github/workflows/windows-installer.yml）から次の形で呼ばれる:
;   ISCC.exe /DAppVersion=1.0.0 /DPublishDir=..\publish\win-x64 installer\MagiApp.iss
; PublishDir には「WindowsPackageType=None（unpackaged）＋ self-contained win-x64」で
; dotnet publish した出力フォルダを渡す（.NET ランタイムも WindowsAppSDK ランタイムも同梱済み
; ＝インストール先の PC に事前インストール不要）。
;
; 設計判断:
; - PrivilegesRequired=lowest ＋ {localappdata}\Programs 配下へのインストール
;   ＝ UAC 昇格なし・コード署名なしでも配布できる per-user インストール
;   （社内・個人配布向け。全ユーザー向け Program Files 配布に切り替えるなら
;   DefaultDirName={autopf}\... と PrivilegesRequired=admin へ変更し、コード署名を推奨）。
; - ArchitecturesAllowed=x64compatible は Inno Setup 6.3+ の構文
;   （GitHub Actions の windows-latest ランナーは 6.4 系を同梱）。ARM64 の Windows 11 でも
;   x64 エミュレーションでインストール可能になる。
; - AppId はアンインストーラ/上書きインストールの同一性キー。**バージョンを跨いで変更しない**。

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish\win-x64"
#endif

#define AppName "MAGI ShiftOptimizer"
#define AppExeName "MagiApp.WinUI.exe"

[Setup]
AppId={{7C2E9B41-5A8F-4D63-9E0B-2F4C8A17D5E3}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=MAGI
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputBaseFilename=MagiShiftOptimizer-Setup-{#AppVersion}-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#AppExeName}

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
