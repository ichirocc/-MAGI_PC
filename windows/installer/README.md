# Windows インストーラの作り方・入れ方

CI: [`.github/workflows/windows-installer.yml`](../../.github/workflows/windows-installer.yml)
（手動実行 `workflow_dispatch`、またはタグ `win-v*` の push で起動。成果物は Actions の Artifacts に14日保存）

## 成果物は2形態

| 形態 | Artifact 名 | 配布先での入れ方 | 証明書 |
|---|---|---|---|
| **setup.exe**（推奨） | `magi-windows-setup-exe` | ダブルクリック→ウィザード。per-user インストール（UAC 昇格なし）。初回は SmartScreen の「詳細情報」→「実行」が必要 | 不要 |
| **MSIX** | `magi-windows-msix` | 署名済みならダブルクリック（初回のみ同梱 .cer を「信頼されたルート」へ導入）。未署名は開発者モード＋`Add-AppxPackage` のみ | Secrets 設定時のみ署名 |

## setup.exe の中身

`dotnet publish -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true --self-contained true`
（unpackaged＋全ランタイム同梱）の出力を [`MagiApp.iss`](./MagiApp.iss) で包む。
配布先 PC に .NET / WindowsAppSDK の事前インストールは不要。
インストール先は `%LOCALAPPDATA%\Programs\MAGI ShiftOptimizer`。

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

## バージョン表記

`workflow_dispatch` の `version` 入力（例 `1.2.0`）＞ 空欄なら
`Package.appxmanifest` の `Identity Version`（4桁）を3桁に丸めて使用。
`Assets/*.png` は現状プレースホルダ（無地の深緑青）。MSIX 生成にはマニフェストが参照する
これらのファイルの実在が必須のため、正式アイコンができたら同名で差し替えるだけでよい。
