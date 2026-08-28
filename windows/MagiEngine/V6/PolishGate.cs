using System.Threading;

namespace MagiEngine.V6;

/// <summary>
/// Faithful port of Kotlin's <c>PolishGate</c> object (in <c>V6HotfixPasses.kt</c>) — a
/// session-only, UI-toggle -&gt; engine-flag settings passthrough (not persisted; same shape as
/// <c>NativeGate</c> per the Kotlin source comment).
///
/// [フェーズ6, 完全移植] Kotlin原本は過去に <c>adaptiveEscapeControl</c>（停滞脱出の適応制御・3.306.0）と
/// <c>portfolioRoleParallelSa</c>/<c>portfolioRoleChains</c>（ロール内並列SA・3.371.0）も持っていたが、
/// 単体 A/B（各15ペア、基準は測定前に固定）で「中立〜有害」（parallelSa は反復数中央値が悪化データセットで
/// むしろ低い＝チェーン分割が希釈になっていた）と判明し、3.409.21 でユーザー選択「両方削除」により
/// Kotlin側からも撤去済み＝この C# 版が現在の Kotlin 原本を完全に反映している（何も端折っていない）。
/// </summary>
public static class PolishGate
{
    /// <summary>
    /// [c3n 回避の範囲拡張, Kotlin 3.303.0] 禁止連続を崩しに行く日を j±1 固定から「パターンがまたぐ
    /// 全日」へ広げる。3連（例: Dﾃ→休→A4）の先頭 j-2 に届くようになる<b>正しい</b>一般化だが、
    /// 実データ3件で利得が一貫しなかったため既定 OFF（golden=中立 / real=weighted −1674 だが
    /// covU 2件を c3n 2件へ付け替え・c1 +14 悪化 / user=weighted +73 悪化）。
    ///
    /// 個々の手は keep-best なので退化しないが、候補が増えると探索の経路が変わり、着地する局所解が
    /// データによって良くも悪くもなる（Kotlin 2.55.0 の戦略的振動・3.94.0 の in-loop レバーと同じ
    /// 結論＝「安全であること」と「有益であること」は別）。計測が支持しない既定変更はしない。
    /// </summary>
    public static volatile bool WideC3nBreakDays = false;

    /// <summary>
    /// ブロック巡回交換で、禁止連続(c3n)が正味増える候補を<b>候補生成の段階で</b>捨てるか。既定 false。
    ///
    /// c3n は HARD なので増える候補は最終的に <c>isBetter</c> が必ず却下する＝ON/OFF で<b>採用結果は
    /// 変わらない</b>（Kotlin 3.296.0 の A/B 実測で最終盤面・採用数が完全一致することを確認済み）。
    /// ON にすると構造的に詰んだ候補へフル checker を呼ばなくなり、評価枠を soft 判定まで進める
    /// 候補へ回せる（実測: 正式評価 48→14〜38 件）。
    /// </summary>
    public static volatile bool FilterC3nIncrease = false;

    private static double _normalStallFraction = 0.9;

    /// <summary>
    /// [Kotlin 3.422.0/ユーザー報告「停滞の早期終了が実質効いていない」への対応・Part B]
    /// <c>V6FinalPort</c> の停滞ウォッチドッグ「通常」分岐（HARD が構造床にまだ届いていない＝
    /// 解ける可能性がある局面）の停滞閾値の割合。既定 <b>0.9</b> ＝旧来の固定値 <c>9/10</c> と厳密に同一。
    ///
    /// [Kotlin 3.424.0で意味論を是正] 適用は <c>V6FinalPort.NormalStallMs</c>＝<b>予算×この割合</b>が
    /// 基本で、その値が探索区間内で一度も発火し得ない帯（実測60秒帯）だけ<b>探索区間×この割合</b>へ
    /// フォールバックする（3.422.0 初版の無条件 <c>searchWindowMs×割合</c> は到達可能な帯まで無計測で
    /// 厳格化していたため復元）。値は <c>NormalStallMs</c> 側の検証で<b>(0,1) 排他・有限のみ</b>＝
    /// 1.0 以上は「閾値&gt;=探索区間」という Part A が直した到達不能バグの再現、NaN は 20秒床への
    /// 暗黙の崩落になるため、丸めず落とす（フェーズ6で <c>V6FinalPort.NormalStallMs</c> を移植する際に
    /// <c>require</c> 相当のガードを追加する）。<b>UI トグルは無し</b>＝コード/計測ハーネスからのみ設定
    /// （<see cref="FilterC3nIncrease"/> 等と違い設定タブには出していない）。
    ///
    /// <b>なぜ「上書き倍率」でなく、この割合自体を対象にしたか</b>: 実装前に算術で検算したところ、
    /// 上書き倍率を対象にする経路は数学的にほぼ無力と判明した（<c>stallMs = 0.9 × 区間</c> に対し
    /// 上書きが区間内に収まるには倍率<c>&lt;1/0.9≈1.111</c>が必要だが、有効範囲のどこを取っても
    /// 「実質早く終わる」効果が出ない）。基準閾値そのものを対象にする方針を採った。
    ///
    /// <b>歴史的後悔との関係</b>: 旧 <c>stallMs=budgetMs/6</c>（300s予算で50s）は HARD=1（まだ解ける
    /// 可能性がある）を早すぎるタイミングで諦め、実機ログで残り250sを無駄にした。この割合を下げすぎると
    /// 同じ後悔を再現しうる＝<b>A/B で実データにより支持された値のみを既定にする</b>
    /// （2.55.0/2.56.0/3.310.1/3.341.1 の規律）。
    ///
    /// [移植メモ] <c>double</c> は C# の <c>volatile</c> 修飾子の対象外（CS0677）のため、Kotlin の
    /// <c>@Volatile var</c> と同じ可視性保証をプロパティ越しの <see cref="System.Threading.Volatile"/>
    /// (<c>Read</c>/<c>Write</c>) で表現する（<c>V6NativeOptimizer.RunSlot.cs</c> の
    /// <c>ThreadLocalRef&lt;T&gt;.Value</c> と同型のパターン）。
    /// </summary>
    public static double NormalStallFraction
    {
        get => Volatile.Read(ref _normalStallFraction);
        set => Volatile.Write(ref _normalStallFraction, value);
    }
}
