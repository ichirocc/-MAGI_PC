namespace MagiEngine.V6;

public static partial class V6FinalPort
{
    /// <summary>
    /// [3.230.0/停滞ウォッチドッグの分離] 「フェーズ公平猶予」と「真の頭打ち検知」を分離した判定を
    /// 純関数として抽出（壁時計に依存する周囲のコードから切り離してユニットテスト可能にする）。
    /// 旧実装は <c>max(lastBestImproveMs, lastPhaseChangeMs)</c> を単一のstallMs(=予算9/10、300s予算で
    /// 270s)と比較しており、20〜90秒間隔で頻発するフェーズ遷移のたびにタイマがリセットされ続け、270秒
    /// という長い閾値には実質的に一度も到達し得なかった（改善が本当に無くても検知できない）。
    /// 本関数は two-condition AND: ①現フェーズ自身が phaseGraceMs 以上経過（起動直後の誤検知防止のみ）
    /// ②最終改善から effStall 以上経過（フェーズ遷移でリセットしない＝真の頭打ち）。
    ///
    /// [3.408.0/実機ログで確定・ユーザー指示「フェーズ名を停滞判定に使うべきではない」]
    /// ①のフェーズ猶予が<b>並列ワーカーによって恒久的な拒否権になっていた</b>。適応ポートフォリオの
    /// 8ワーカーは1本のフェーズ文字列を共有するため <c>lastPhaseChangeMs</c> が絶えず更新され、①が
    /// ほぼ真にならない。実機ログ(2026-08-19)は
    /// 「停滞274s・実効閾値37s・発火=なし・未発火の理由=現フェーズ猶予未達(実測0s/7s)」＝
    /// <b>275秒まるごと無改善なのに一度も発火しない</b>という形でこれを記録している。
    /// フェーズ猶予は「始まったばかりのフェーズを即殺しない」ための<b>遅延</b>であって、
    /// 頭打ちの検知そのものを止めてよい根拠は無い。よって①を<b>遅延に降格</b>し、
    /// 無改善が閾値の <see cref="StallOverrideFactor"/> 倍に達したらフェーズ猶予に関わらず発火する。
    ///
    /// 代償は測ってある: 3.341.1 の実測で早期終了を<b>外す</b>と weighted 中央 −3.5%（p≈0.075＝有意でない）
    /// ＝発火を早めるとごく僅かに品質を落とし、時間と電池を大きく節約する。倍率2は
    /// 「本当に詰まっている run は閾値の2倍まで待つ」保守側の設定。
    /// </summary>
    internal const int StallOverrideFactor = 2;

    /// <summary>Faithful port of Kotlin's <c>internal fun watchdogStagnationFired(...)</c>. See <see cref="StallOverrideFactor"/> for the design rationale.</summary>
    internal static bool WatchdogStagnationFired(
        long now, long startMs, long minRunMs,
        long lastPhaseChangeMs, long phaseGraceMs,
        long lastBestImproveMs, long effStall)
    {
        if (now - startMs <= minRunMs) return false;
        var stalled = now - lastBestImproveMs;
        if (stalled <= effStall) return false;
        // フェーズ猶予は遅延であって拒否権ではない（並列ワーカーのフェーズ更新で永久に塞がれない）。
        return now - lastPhaseChangeMs > phaseGraceMs || stalled > effStall * StallOverrideFactor;
    }

    /// <summary>
    /// [3.281.0/停滞レビューA] ウォッチドッグの実効停滞閾値の選択を純関数として抽出（ユニットテスト用）。
    /// 従来: 「bestHard&lt;=hardFloor(構造的covU床) かつ 非covU HARD=0」のときだけ短い stallHardMs＝
    /// c3n が1件でも残ると常に stallMs(=予算9/10)で、300s予算では発火に270s必要＝<b>構造的に発火不能</b>
    /// だった（実機ログ: 125s以降150s無改善のまま探索275sを完走・追加精製0）。covU には
    /// structuralHardFloor という「解けないHARD」の静的判定があるのに c3n には無い非対称が根本原因。
    /// 新規: 残る非covU HARD が <b>c3n のみ</b>で、かつ 3.280.0 ForbiddenDiag が全 run の塞がりを
    /// <b>証明</b>した（c3nWallProven）場合も plateau とみなし stallHardMs へ移行する。証明つきのため
    /// 誤発火なし・早期終了は時間/電池の節約のみで品質は keep-best が担保（退化不能）。
    /// </summary>
    internal static long EffectiveStallMs(
        int bestHard, int hardFloor, int nonCovUHard, bool nonCovUAllC3n,
        bool c3nWallProven, long stallHardMs, long stallMs)
    {
        var basePlateau = bestHard <= hardFloor && nonCovUHard == 0;
        var c3nWallPlateau = nonCovUHard > 0 && nonCovUAllC3n
            && bestHard <= hardFloor + nonCovUHard && c3nWallProven;
        return basePlateau || c3nWallPlateau ? stallHardMs : stallMs;
    }

    /// <summary>
    /// [3.422.0/Part B・3.424.0で基準を是正] 「通常」分岐（HARD がまだ構造床に届いていない＝解ける
    /// 可能性がある局面）の停滞閾値 <c>stallMs</c> の算出（純関数＝<see cref="EffectiveStallMs"/>/
    /// <see cref="WatchdogStagnationFired"/> と同じ理由でユニットテスト可能にする）。
    ///
    /// 意味論: <b>予算×割合</b>（旧来の <c>budgetMs*9/10</c> と既定で厳密に同一）。ただしその値が
    /// 探索区間(<paramref name="searchWindowMs"/>) 内で一度も発火し得ない帯（後処理予約の下限クランプが
    /// 探索区間を大きく削る中程度の予算＝実測60秒帯。判定は <c>raw &gt;= searchWindowMs</c>＝
    /// <c>stalled &gt; effStall</c> が探索終了まで真になれない）だけ、<b>探索区間×割合</b>へ
    /// フォールバックする。
    ///
    /// [3.424.0/code-review指摘の是正] 3.422.0 の初版は無条件に <c>searchWindowMs*fraction</c> として
    /// おり、到達可能だった帯まで無計測で厳格化していた（300s予算: 270s→247.5s＝−8.3%）。計測が支持
    /// しない既定変更はしない（2.55.0/3.310.1/3.341.1）ため、予算基準を復元し到達不能帯だけを直す形へ。
    /// 60秒帯（3.423.0 の A/B で測った帯）はフォールバック側＝挙動不変。
    ///
    /// <paramref name="fraction"/> は既定で <see cref="PolishGate.NormalStallFraction"/> を読む
    /// （<c>fraction ?? PolishGate.NormalStallFraction</c>＝Kotlinのデフォルト引数「呼び出し時評価」を
    /// 表す、<c>V6HotfixPasses.AdaptiveBlockSwap.cs</c> の <c>filterC3nIncrease</c> と同じ配線パターン。
    /// C#は非定数式を引数の既定値にできないため <c>double?</c> + null合体演算子で表す）。
    /// <b>(0,1) 排他・有限のみ受け付ける</b>: fraction&gt;=1.0 は「閾値&gt;=探索区間」＝Part A が直した
    /// 到達不能バグの再現、NaN は「20秒床への暗黙の崩落」＝最凶の早期終了へ静かに化けるため、丸めず
    /// 落とす（<see cref="GlsPenalty.Decay"/> の <c>ArgumentException</c> ガードと同じ型）。
    /// </summary>
    internal static long NormalStallMs(long budgetMs, long searchWindowMs, double? fraction = null)
    {
        var f = fraction ?? PolishGate.NormalStallFraction;
        if (!(double.IsFinite(f) && f > 0.0 && f < 1.0))
            throw new ArgumentException($"normalStallFraction は (0,1) の有限値のみ: {f}");
        var raw = Math.Max((long)(budgetMs * f), 20_000L);
        if (raw < searchWindowMs) return raw;
        return Math.Max((long)(searchWindowMs * f), 20_000L);
    }
}
