# Windows インストーラの作り方・入れ方

CI: [`.github/workflows/windows-installer.yml`](../../.github/workflows/windows-installer.yml)
（手動実行 `workflow_dispatch`、またはタグ `win-v*` の push で起動。成果物は Actions の Artifacts に14日保存）

## 成果物は2形態

| 形態 | Artifact 名 | 配布先での入れ方 | 証明書 |
|---|---|---|---|
| **setup.exe**（推奨） | `magi-windows-setup-exe` | ダブルクリック→ウィザード。per-user インストール（UAC 昇格なし）。ブラウザ経由で落とすと初回に SmartScreen の「詳細情報」→「実行」。**`curl.exe` で落とすか zip を「ブロックの解除」してから展開すれば警告は出ない**（Mark of the Web が付かないため。`../README.md`「Windows 11 での入れ方」） | 不要（Secrets 設定時のみ Authenticode 二重署名） |
| **GitHub Release**（タグ `win-vX.Y.Z` push 時のみ） | Release の添付 | `https://github.com/ichirocc/-MAGI_PC/releases/latest/download/MagiShiftOptimizer-Setup-x64.exe` が常に最新の setup.exe。README の1行インストールはこれを使う | 不要 |
| **MSIX** | `magi-windows-msix` | 署名済みならダブルクリック（初回のみ同梱 .cer を「信頼されたルート」へ導入）。未署名は開発者モード＋`Add-AppxPackage` のみ | Secrets 設定時のみ署名 |

## 更新版を出す手順（Release）

どちらでもよい:

- **Actions の画面から**: 「Windows Installer」→ Run workflow → `version` に `1.0.1`（空欄なら appxmanifest の版）、
  `publish_release` にチェック → Run。`release` ジョブが `gh release create --target <そのコミット>` でタグ `win-v1.0.1` を
  打ち、Release を作る（git でタグを push できない環境向け。Claude Code のリモート環境はタグ push が 403 になる）。
- **git から**: `git tag win-v1.0.1 && git push origin win-v1.0.1`

タグ push で「Windows Installer」が起動し、`Resolve version` がタグの `1.0.1` を版にする（`Package.appxmanifest` の
書き換え不要）。`installer-exe` ジョブの setup.exe を `release` ジョブが Release `win-v1.0.1` に添付する
（版付きファイル＋固定名 `MagiShiftOptimizer-Setup-x64.exe`、ノートに SHA-256 と1行インストール）。
`publish_release` を付けない手動実行では Release は作らない（Artifacts のみ）。

## setup.exe の中身

`dotnet publish -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true --self-contained true`
（unpackaged＋全ランタイム同梱）の出力を [`MagiApp.iss`](./MagiApp.iss) で包む。
配布先 PC に .NET / WindowsAppSDK / VC++ ランタイムの事前インストールは不要（2026-09-04 から `resources.pri` と
VC++ ランタイム DLL を publish へ写すステップを追加。それ以前の setup.exe はこの2つが欠けて起動時に無言終了していた）。
インストール先は `%LOCALAPPDATA%\Programs\MAGI ShiftOptimizer`。起動しないときは `%LOCALAPPDATA%\Magi\startup_error.log`。

## MSIX 署名の Secrets 設定（任意）

1. Windows の PowerShell で自己署名証明書を作成（Subject は `Package.appxmanifest` の
   `Publisher="CN=MAGI"` と**完全一致**が必須）:

   ```powershell
   $cert = New-SelfSignedCertificate -Type Custom -Subject "CN=MAGI" `
     -KeyUsage DigitalSignature -FriendlyName "MAGI sideload" `
     -CertStoreLocation "Cert:\CurrentUser\My" `
     -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
   Export-PfxCertificate -Cert $cert -FilePath magi.pfx `
     -Password (ConvertTo-SecureString -String "<パスワード>" -Force -AsPlainText)
   [Convert]::ToBase64String([IO.File]::ReadAllBytes("magi.pfx")) | Set-Clipboard
   ```

2. GitHub リポジトリの Settings → Secrets and variables → Actions へ:
   - `MSIX_PFX_BASE64` … クリップボードの base64 文字列
   - `MSIX_PFX_PASSWORD` … 上で決めたパスワード

Secrets が無い場合もワークフローは失敗せず、未署名 .msix を成果物にする。

## setup.exe の Authenticode 署名（任意）

Secrets に以下の2つを登録すると、[`sign-files.ps1`](./sign-files.ps1) が signtool.exe
（Windows SDK 同梱・パスは動的探索）で **二重署名** する:

- `WINDOWS_CERTIFICATE_BASE64` … コード署名証明書 .pfx の Base64 文字列
  （`[Convert]::ToBase64String([IO.File]::ReadAllBytes("cert.pfx")) | Set-Clipboard`）
- `WINDOWS_CERTIFICATE_PASSWORD` … .pfx のエクスポートパスワード

署名の順序と仕様:

1. publish 直後に**中身の `MagiApp.WinUI.exe`** を署名（インストーラへ入れる前）
2. Inno Setup でインストーラ化
3. **生成された setup.exe** を署名

いずれも `/fd SHA256`＋RFC 3161 タイムスタンプ（`/tr http://timestamp.digicert.com /td SHA256`）
付き＝証明書の有効期限が切れた後も「署名時点で有効だった」と検証される。
PFX はランナーの一時ファイルへ展開し、成否に関わらず `finally` で削除する。

注意:

- 自己署名証明書でも署名自体は通るが、SmartScreen の警告は消えない（配布先が証明書を
  信頼しても「不明な発行元」扱い）。スマート アプリ コントロールにも効かない。警告を消すには CA 発行の
  コード署名証明書が必要。**証明書なしで警告を出さない方法**（Mark of the Web を付けない／外す）は
  `../README.md`「Windows 11 での入れ方」。
- EV 証明書（物理トークン必須）やクラウド管理鍵（Azure Key Vault / DigiCert ONE 等）を使う場合は、
  PFX をランナーへ展開するこの方式ではなく `Azure/trusted-signing-action` 等のベンダー提供
  アクションへ置き換える。
- Secrets 未設定ならこのステップは何もせず成功する（署名なし配布＝従来どおり）。

## バージョン表記

`workflow_dispatch` の `version` 入力（例 `1.2.0`）＞ 空欄なら
`Package.appxmanifest` の `Identity Version`（4桁）を3桁に丸めて使用。
`Assets/*.png` は現状プレースホルダ（無地の深緑青）。MSIX 生成にはマニフェストが参照する
これらのファイルの実在が必須のため、正式アイコンができたら同名で差し替えるだけでよい。
