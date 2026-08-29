namespace MagiEngine.V6;

/// <summary>
/// [フェーズ9] MAGI デザインシステムの意味色パレット（<c>MagiTokens.kt</c> の <c>object MagiAccent</c> の
/// 逐語移植・値のみ）。
///
/// 一次ソースは Material3/WinUI3 の colorScheme/テーマロール（primary/secondary/surface 等）。ここでは
/// テーマロールに無い「意味色」だけを補い、二重管理を避ける（<c>MagiTokens.kt</c> 冒頭コメントのとおり）。
/// 色は <c>#RRGGBB</c> の16進文字列で表現する（<see cref="MagiState.ShiftColors"/>・
/// <see cref="ShiftAppearance"/> と同じ、この移植でのColorの唯一の表現。実際の
/// <c>Windows.UI.Color</c>/ブラシへの変換は WinUI3 の View 層が担う＝このクラスは Windows App SDK に
/// 依存しないプラットフォーム非依存のデータ）。
/// </summary>
public static class MagiAccent
{
    // [3.89.0 "Ward" 調和・由来をそのまま記録] ネオン Tailwind-500 系 → ディープティール地／冷たい
    //   ペーパーに馴染む一段深い「診療チャート」調へ。7色の色相位置(青/緑/橙/紫/桃/赤/灰)は据え置き＝
    //   認識性を保つ。保存済みのユーザー指定シフト色(shiftColors 等)は不変。ここは既定パレット＋
    //   直接使用アクセントのみの値。Kotlin側の <c>Color(0xFFrrggbb)</c>（不透明・ARGB）から
    //   アルファを剥がした <c>#rrggbb</c>（6桁RGB）表現。
    public const string Blue = "#3B6FD4";    // 実行中 / 早番（スティールブルー）
    public const string Green = "#2E9E62";   // 成功 / 日勤（リーフ、ティール主色と弁別）
    public const string Orange = "#E08A1E";  // 警告 / 夜勤（アンバー）
    public const string Purple = "#8A5CD1";  // 遅番 / 個人属性（ミュートバイオレット）
    public const string Pink = "#D24D89";    // 希望 / 個人属性（ローズ）
    public const string Red = "#D23B34";     // 重大違反 / NG制約（明快なアラート赤）
    public const string Gray = "#8A979B";    // 休み / 無効（クールスレート、ペーパーに調和）

    /// <summary>色ピッカー等で提示する既定パレット（Kotlin原本の並び順を保持）。</summary>
    public static readonly IReadOnlyList<string> All = new[] { Blue, Green, Orange, Purple, Pink, Red, Gray };

    // [MagiTokens.kt の magiWarnColors()・MagiSpacing はここでは移植しない]
    // magiWarnColors() は「現在のテーマの surface 明度」を実行時に読んで明/暗を判定する
    // @Composable 関数＝Compose の反応的テーマ読み取りに本質的に依存し、プラットフォーム非依存の
    // 純粋関数として抽出できない。WinUI3 の ThemeDictionaries（Light/Dark キー付きリソース）として
    // View 層（MagiApp.WinUI、フェーズ8/9のXAML作業）で表現する。値は正確に転記のため記録しておく：
    //   暗テーマ: container=#5B4300, onContainer=#FBEAD0
    //   明テーマ: container=#FBEAD0, onContainer=#6B4E00
    // MagiSpacing（4dpグリッドの余白トークン: xs=4/sm=8/md=12/lg=16/xl=20/section=20/screenH=16）は
    // 移植計画が明示するとおり ResourceDictionary/ThemeResources.xaml（Windows専用の MagiApp.WinUI
    // プロジェクト）へ置く＝このサンドボックスでビルド・検証できない領域のため、実際のXAML作業時に移す。
}
