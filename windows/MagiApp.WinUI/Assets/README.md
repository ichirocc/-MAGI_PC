# Assets（アイコン/ブランディング）

[フェーズ11] `Package.appxmanifest` が参照するアイコン類（`StoreLogo.png`／`Square150x150Logo.png`／
`Square44x44Logo.png`／`Wide310x150Logo.png`／`SplashScreen.png`）と、非パッケージ(unpackaged)
ビルド（`installer/MagiApp.iss` が配布する exe）の Win32 アイコン（`app.ico`）に実体を用意した。

## 経緯・出典

Android版の launcher icon（`app/src/main/res/mipmap-*/ic_launcher_foreground.png`）が持つ
「勤務表(3行) ＋ チェックマーク」の意匠を土台にしたが、その launcher icon 自体の配色（青〜紫〜
オレンジのグラデーション）は現行の docs/DESIGN.md（トークン一次ソース＝`MainActivity.MagiTheme`）の
ブランド配色（ディープティール、`Styles/MagiTheme.xaml` で移植済み）と一致していなかった
（launcher icon が更新されないまま取り残されたと判断）。そのため配色は現行ブランド
（背景=`MagiPrimaryColor` #00504A・行=白・チェックバッジ=`MagiTertiaryContainerColor`/
`MagiTertiaryColor`）に揃え、意匠（3行＋チェック）だけを引き継いだ。

生成スクリプトはこのサンドボックス限定の使い捨て（Pillow で図形描画、リポジトリには残していない）。
再生成・調整が要る場合は同じ配色トークン（`Styles/MagiTheme.xaml` 参照）を使って作り直すこと。

## 既知の限界

このサンドボックスには専門のデザインツールが無く、幾何図形の組み合わせによる簡易な意匠に留まる
（アンチエイリアシングの微調整・実機でのタイル/タスクバー表示確認は未実施）。実機での見え方確認と
微調整は、実際にビルド・インストールできる環境（フェーズ11の残課題）で行うこと。
