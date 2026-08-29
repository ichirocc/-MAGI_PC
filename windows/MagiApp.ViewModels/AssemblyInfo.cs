using System.Runtime.CompilerServices;

// [フェーズ9] Kotlin原本の MagiViewModel.kt はUI層(package com.magi.app.ui)としてAndroidに依存するため
// ホストJVMでコンパイル・実行できず、専用の単体テストが元々存在しない（MagiApp.ViewModels.Tests の
// UiStateTest.cs 冒頭に同じ経緯を記録済み）。このC#移植はプラットフォーム非依存のクラスライブラリへ
// 切り出したことで初めてテスト可能になった——これは MagiEngine.Tests のために internal を公開している
// 確立済みの規約（AssemblyInfo.cs 参照）と同じ理由付けだが、対象がここでは「Kotlin原本の internal」
// ではなく「元々privateだったがC#移植で初めてテスト対象になり得るメンバ」である点が異なる
// （MagiViewModel.cs の各メンバに [テスト可視性のためinternal化] として個別に理由を記録している）。
[assembly: InternalsVisibleTo("MagiApp.ViewModels.Tests")]
