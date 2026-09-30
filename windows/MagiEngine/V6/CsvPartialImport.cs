using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// Android <c>CsvPartialImport.kt</c> の移植（同名・同分割）。引用符が閉じていない勤務表CSV（重ね合わせ取込）を、
/// 読めたところまで取り込むかの判断。画面も ViewModel も持たない純粋な関数だけ（テストできる）。保留の保持と適用は ViewModel 側。
/// Kotlin の <c>fun readable</c> は型 <see cref="Readable"/> と名前が衝突するため <see cref="ReadableOf"/>。
/// </summary>
public static class CsvPartialImport
{
    public const string NothingReadable = "取り込める行がありませんでした（引用符が閉じていません）";
    public const string Stale = "盤面が変わったため取込をやめました。もう一度取り込んでください";
    public const string Cancelled = "取込をやめました";
    public const string ConfirmLabel = "この部分だけ取り込む";
    public const string CancelLabel = "やめる";

    public const string AppliedWarning = "｜⚠ 引用符（\"）が閉じていません。ここから後ろの行は読めていません";

    public static string PromptText(int endLine, int matched) =>
        $"CSV の {endLine}行目までは読めました（{matched} 名分）。その先は引用符が閉じていないため読めません。この部分だけ取り込みますか？";

    /// <summary><paramref name="Records"/>＝改行で終わった行の数、<paramref name="EndLine"/>＝その最後の行の最終物理行（1 始まり、0＝無し）、<paramref name="PrefixText"/>＝読めた部分の本文（閉じていれば全文）。</summary>
    public sealed record Readable(bool UnclosedQuote, int Records, int EndLine, string PrefixText);

    public static Readable ReadableOf(string raw)
    {
        var p = CsvUtil.ParseCsvFull(raw);
        return new Readable(p.UnclosedQuote, p.ReadableRows, p.ReadableEndLine, raw.Substring(0, p.ReadableEndOffset));
    }

    public abstract class Verdict
    {
        private Verdict() { }

        /// <summary>引用符は閉じている＝従来の取込へ。</summary>
        public sealed class WellFormed : Verdict
        {
            public static readonly WellFormed Instance = new();
        }

        /// <summary>引用符が閉じておらず、読めた部分に職員の行が1つも無い＝何も変えずに断る。</summary>
        public sealed class NothingReadable : Verdict
        {
            public static readonly NothingReadable Instance = new();
        }

        /// <summary>読めた部分だけの結果（吸い込まれた行は含まない）。確認のあとにそのまま適用できる。</summary>
        public sealed class Ask : Verdict
        {
            public ScheduleRunResult Result { get; }
            public int EndLine { get; }

            public Ask(ScheduleRunResult result, int endLine)
            {
                Result = result;
                EndLine = endLine;
            }

            public int Matched => Result.Matched;
            public string Prompt => PromptText(EndLine, Matched);
        }
    }

    public static Verdict Judge(string raw, MagiState state, int[][] baseSchedule)
    {
        var r = ReadableOf(raw);
        if (!r.UnclosedQuote) return Verdict.WellFormed.Instance;
        var res = ScheduleCsvBridge.Parse(r.PrefixText, state, baseSchedule);
        if (res.Matched <= 0) return Verdict.NothingReadable.Instance;
        return new Verdict.Ask(res, r.EndLine);
    }

    /// <summary>確認待ちの取込。<paramref name="StateKey"/>/<paramref name="BoardKey"/> は結果を作った時点の設定と盤面の指紋。</summary>
    public sealed record Pending(Verdict.Ask Ask, long StateKey, long BoardKey);

    public abstract class Resolution
    {
        private Resolution() { }

        public sealed class Apply : Resolution
        {
            public Pending Pending { get; }
            public Apply(Pending pending) { Pending = pending; }
        }

        public sealed class Stale : Resolution
        {
            public static readonly Stale Instance = new();
        }

        public sealed class NoPending : Resolution
        {
            public static readonly NoPending Instance = new();
        }
    }

    /// <summary>「この部分だけ取り込む」が押されたとき、いまの指紋が保留の作成時と同じなら適用、違えば捨てる。</summary>
    public static Resolution Resolve(Pending? pending, long stateKeyNow, long boardKeyNow)
    {
        if (pending is null) return Resolution.NoPending.Instance;
        if (pending.StateKey != stateKeyNow || pending.BoardKey != boardKeyNow) return Resolution.Stale.Instance;
        return new Resolution.Apply(pending);
    }
}
