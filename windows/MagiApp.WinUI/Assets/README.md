# Assets（プレースホルダー）

`Package.appxmanifest` が参照するアイコン類（`StoreLogo.png`／`Square150x150Logo.png`／
`Square44x44Logo.png`／`Wide310x150Logo.png`／`SplashScreen.png`）はまだ実体が無い。

このサンドボックス環境では画像生成・WinUI3ビルド確認のいずれも不可（フェーズ0の設計どおり）。
**フェーズ11（パッケージング/配布）でアイコン/ブランディングを用意する**まで、実機での
MSIXパッケージビルドはこの欠落で失敗しうる。フェーズ0〜10は非パッケージ実行
（`dotnet run` 相当。Visual Studio では「配置なし」でのデバッグ実行）で進める前提。

Android版の launcher icon（`app/src/main/res/mipmap-*/`）を土台に、
`tools/make_launcher_icon.py` 相当の手法で書き出す想定。
