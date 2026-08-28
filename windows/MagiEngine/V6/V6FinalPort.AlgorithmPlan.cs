namespace MagiEngine.V6;

public static partial class V6FinalPort
{
    /// <summary>
    /// Faithful port of Kotlin's file-top-level <c>const val MAX_OPTIMIZE_SEC</c> (declared outside
    /// <c>object V6FinalPort</c> in the Kotlin source, but referenced only from within it — the
    /// natural home in C# is as a member of the class it actually gates). 勤務表最適化のタイムアウト
    /// 上限（秒）。高精度を保ったまま5分(300s)以内へ圧縮（停滞早期脱出＋RSI++をこの予算に収める）。
    /// </summary>
    public const int MaxOptimizeSec = 300;

    /// <summary>Faithful port of Kotlin's <c>AlgorithmLabel</c> data class.</summary>
    public sealed record AlgorithmLabel(string Icon, string Name, string Desc, string Tech);

    /// <summary>
    /// Faithful port of Kotlin's <c>sealed class OptimizationPlan</c> with its 4 nested
    /// <c>data class</c> variants (<see cref="V5"/>/<see cref="ALNS"/>/<see cref="RSIThenALNS"/>/
    /// <see cref="Portfolio"/>).
    ///
    /// [C#移植上の判断] Kotlin の <c>sealed class</c> は「同一モジュール内で宣言された既知の派生
    /// クラスにしか継承を許さない」制約を持つ。C# の <c>abstract record</c>（既定コンストラクタは
    /// <c>protected</c>）はこの制約に近い——本体に何も宣言しなければコンパイラが自動生成する
    /// パラメータなし <c>protected</c> コンストラクタにより、4つの入れ子 <c>sealed record</c> 以外
    /// からの直接継承が事実上できない（外部の型がこの階層へ新しい派生型を追加するには、この
    /// アセンブリ内から <c>protected</c> コンストラクタを呼べる位置に自分自身を置く必要があり、
    /// 実質的に閉じている）。
    /// </summary>
    public abstract record OptimizationPlan
    {
        public sealed record V5(int Seconds) : OptimizationPlan;

        public sealed record ALNS(int Seconds, int Restarts) : OptimizationPlan;

        public sealed record RSIThenALNS(int RsiSec, int AlnsSec, int AlnsRestarts) : OptimizationPlan;

        /// <summary>
        /// [Kotlin 3.266.0] 旧RSIPlusから改称。211秒以上はRSI++クローン群でなく、ALNS/RSI/RSI++の
        /// 異種非同期適応ポートフォリオ（<c>V6Algorithm.PORTFOLIO</c>、フェーズ5で移植済み）を使う
        /// （<c>HypothesisDiversityPolicy.AutoAlgorithmForBudget</c> と同じ閾値・同じ意図。旧実装は
        /// ここが独立した別ロジックのままで、<c>V6NativeOptimizer.ChooseAlgorithm</c>側だけを直しても
        /// 実際のAUTOフローには反映されなかった、というKotlin原本の過去の回帰の教訓）。
        /// </summary>
        public sealed record Portfolio(int Seconds) : OptimizationPlan;
    }

    /// <summary>
    /// Faithful port of Kotlin's <c>fun optimizationPlan(seconds: Int): OptimizationPlan</c>.
    ///
    /// [C#移植上の判断・命名] Kotlin原本の関数名は <c>optimizationPlan</c>（小文字始まり）で、
    /// 型名 <c>OptimizationPlan</c>（大文字始まり）とは大文字小文字で区別される——KotlinもJVMも
    /// メソッドと型は別の名前空間に属するためこれで衝突しない。C#はメソッド名にもPascalCaseを
    /// 使うため直訳すると同名の入れ子型と衝突する（C#はメンバー名としてのメソッドと入れ子型を
    /// 区別しない）。姉妹関数 <c>getAlgorithmLabel</c>→<see cref="GetAlgorithmLabel"/> と対称的な
    /// <c>Get</c> 接頭辞を付けて <see cref="GetOptimizationPlan"/> とした（機械的に必要な改名であり、
    /// 挙動は一切変えていない）。
    /// </summary>
    public static OptimizationPlan GetOptimizationPlan(int seconds)
    {
        // [review #4, Kotlin原本] Honor the user's budget: the algorithm is still chosen by range, but
        // the run time uses the requested `seconds` (previously fixed 10/30/90/150+75... regardless).
        // [5分圧縮] 上限300sでも最上位のフェーズが回るよう閾値を前倒し。
        var s = Math.Max(seconds, 1);
        if (s <= 30) return new OptimizationPlan.V5(s);
        // [実機指摘「60秒予算を1つだけのアルゴリズムで使用」, Kotlin原本] 旧: 31〜90s は ALNS 単発で、
        //   詰まった HARD 族（c3n 等）を狙う RSI フェーズが一度も走らなかった。短予算でも複合
        //   （RSI=違反集中 2/3 → ALNS=研磨 1/3）へ。各段は入力比 keep-best 番兵つき＝退化なし。
        if (s <= 210)
        {
            var rsi = s * 2 / 3;
            return new OptimizationPlan.RSIThenALNS(rsi, s - rsi, 2);
        }
        // [Kotlin 3.266.0] 211秒以上は同型RSI++クローン8本でなく、ALNS/RSI/RSI++の異種非同期適応
        //   ポートフォリオ(V6Algorithm.PORTFOLIO)を使う。旧実装はここが一貫してRSIPlusを返すため、
        //   V6NativeOptimizer.chooseAlgorithm側の同種の変更だけでは実際のAUTOフローに反映されず、
        //   本来の狙い（長時間AUTOでの基盤/役割多様化）が発現しない欠陥があった。
        return new OptimizationPlan.Portfolio(s);
    }

    /// <summary>Faithful port of Kotlin's <c>fun getAlgorithmLabel(seconds: Int): AlgorithmLabel</c>.</summary>
    public static AlgorithmLabel GetAlgorithmLabel(int seconds)
    {
        if (seconds <= 10) return new AlgorithmLabel("⚡", "高速", "短時間でサッと作成", "v5");
        if (seconds <= 30) return new AlgorithmLabel("★", "標準", "速さと品質のバランス", "v5");
        // [実機指摘, Kotlin原本] 31〜210s は複合（違反集中→研磨）に統一。表示ラベルもプランと同期。
        if (seconds <= 210) return new AlgorithmLabel("🧬", "学習+研磨", "RSI違反集中→ALNS研磨", "RSI→ALNS");
        // [Kotlin 3.266.0] 表示ラベルもプラン(Portfolio)と同期。同型RSI++クローン8本でなく、
        //   ALNS/RSI/RSI++が異なる基盤・役割から非同期に探索し、停滞/重複を検知して再配属する。
        if (seconds <= 300) return new AlgorithmLabel("🌈", "究極(5分)", "ALNS/RSI/RSI++ 異種並列探索(適応epoch)", "PORTFOLIO");
        return new AlgorithmLabel("🌈", "究極", $"最大限の品質 ({seconds / 60}分)", "PORTFOLIO拡張");
    }
}
