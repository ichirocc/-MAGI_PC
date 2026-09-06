# Authenticode 署名の共通スクリプト（GitHub Actions windows ランナー用）。
#
# 使い方: 環境変数 CERT_BASE64（.pfx の Base64）/ CERT_PASSWORD を設定して
#   ./sign-files.ps1 -Paths @("C:\path\to\app.exe", "C:\path\to\dir")
#   ディレクトリを渡すと配下の *.exe / *.msi を再帰で署名する。
#
# 設計（windows-installer.yml の2箇所から共通利用・二重署名構成）:
#   ① publish 後・Inno ビルド前に「中身の MagiApp.WinUI.exe」を署名
#   ② Inno ビルド後に「生成された setup.exe」を署名
# - CERT_BASE64 未設定なら何もせず成功（署名なし配布＝SmartScreen初回警告のみ、従来どおり）。
# - タイムスタンプ(/tr /td SHA256)付与＝証明書の期限切れ後も「署名時点で有効」と検証される。
# - PFX は一時ファイルへ展開し、成否に関わらず finally で確実に削除する。
# - -RequireSignature を付けると、Secrets が無い／片方だけのときに**失敗**する（公開 Release 向け。
#   既定は従来どおりスキップして成功）。-ValidateOnly は署名せず Secrets の有無だけを検査する
#   （publish やビルドに時間を使う前に、署名が必須なのに用意が無い run を先頭で落とすため）。
param(
  [string[]]$Paths = @(),
  [switch]$RequireSignature,
  [switch]$ValidateOnly
)
$ErrorActionPreference = "Stop"

$hasCert = -not [string]::IsNullOrWhiteSpace($env:CERT_BASE64)
$hasPass = -not [string]::IsNullOrWhiteSpace($env:CERT_PASSWORD)
if ($hasCert -ne $hasPass) {
  throw "WINDOWS_CERTIFICATE_BASE64 と WINDOWS_CERTIFICATE_PASSWORD は両方設定してください（片方だけ設定されています）。"
}
if (-not $hasCert) {
  if ($RequireSignature) {
    throw "この run は署名が必須ですが Secrets: WINDOWS_CERTIFICATE_BASE64 / WINDOWS_CERTIFICATE_PASSWORD が未設定です。"
  }
  Write-Host "署名スキップ（Secrets: WINDOWS_CERTIFICATE_BASE64 未設定）"
  exit 0
}
if ($ValidateOnly) {
  Write-Host "署名の Secrets を確認しました（署名は後続ステップで行う）。"
  exit 0
}
if (-not $Paths) { throw "-Paths を指定してください。" }

$certPath = Join-Path $env:RUNNER_TEMP "signing-cert.pfx"
[IO.File]::WriteAllBytes($certPath, [Convert]::FromBase64String($env:CERT_BASE64))

try {
  # Windows SDK 内の signtool.exe を動的に探索（SDK バージョン違いに耐える）
  $signtool = Get-ChildItem -Path "C:\Program Files (x86)\Windows Kits\10\bin" -Filter "signtool.exe" -Recurse |
              Where-Object { $_.FullName -like "*x64*" } |
              Select-Object -First 1 -ExpandProperty FullName
  if (-not $signtool) { throw "signtool.exe not found in Windows SDK path." }
  Write-Host "Using SignTool: $signtool"

  $targets = foreach ($p in $Paths) {
    if (Test-Path $p -PathType Container) {
      Get-ChildItem -Path $p -Include "*.exe", "*.msi" -Recurse
    } else {
      Get-Item $p
    }
  }
  if (-not $targets) { throw "署名対象が見つかりません: $($Paths -join ', ')" }

  foreach ($file in $targets) {
    Write-Host "Signing: $($file.FullName)"
    & $signtool sign `
      /f $certPath `
      /p $env:CERT_PASSWORD `
      /fd SHA256 `
      /tr "http://timestamp.digicert.com" `
      /td SHA256 `
      /v $file.FullName
    if ($LASTEXITCODE -ne 0) { throw "SignTool failed with exit code $LASTEXITCODE" }
  }
}
finally {
  if (Test-Path $certPath) {
    Remove-Item -Force $certPath
    Write-Host "Temporary certificate removed."
  }
}
