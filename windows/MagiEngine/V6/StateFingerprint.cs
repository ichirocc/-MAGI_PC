using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// [フェーズ9] 勤務表の意味を決める入力すべての指紋（<c>StateFingerprint.kt</c> の逐語移植）。
///
/// **何に使うか**（安全機構が2つ、この値の正しさに乗っている）:
///  - 研磨診断（C1頭打ち・回数固定の却下記録）の鮮度判定。観測したときの入力と今の入力が違えば、
///    その診断はもう「今の設定の話」ではないので出さない。
///  - 背景で走らせた最適化の結果を当ててよいかの照合。実行中に別のデータを開く・取り込むと、
///    結果が別の入力に対して計算されたものになる。
///
/// **意図的に読まないフィールド**（<see cref="MagiState"/> の26個のうちこの3つだけ。残り23個は全部読む）:
///  - <c>Schedule</c>＝盤面。診断は「この盤面のもの」を盤面ハッシュで別に見ており、結果の適用では
///    盤面が変わるのは当然。ここに混ぜると結果の照合が必ず不一致になって使えなくなる。
///  - <c>ShiftColors</c>＝表示色。エンジンに影響しない。
///  - <c>Extras</c>＝まだモデル化していない項目を往復のために持っているだけ。エンジンに影響しない。
///
/// 書き忘れると安全機構が黙って効かなくなる（変えたのに気づけず古い診断・古い結果が通る）ので、
/// <c>StateFingerprintTest</c> が入力の族ごとに「変えたら指紋も変わる」ことを固定する。
/// <see cref="MagiState"/> にフィールドを足したら、ここと そのテストの両方に足すこと。
///
/// C#の <c>long</c> は Kotlin の <c>Long</c> と同じ64bit符号付き・オーバーフロー時は黙って
/// 巻き戻る（<c>unchecked</c> がプロジェクト既定＝<c>checked</c>ブロックで囲まない限り自動的に
/// この意味論）。文字コードは <c>char</c>（C#もUTF-16コード単位）をそのまま <c>long</c> へ拡張する
/// だけで Kotlin の <c>Char.code</c> と一致する。
/// </summary>
public static class StateFingerprint
{
    /// <summary>行の区切り。可変長の行を素通しで連結すると、構造が違うのに同じ値になる。</summary>
    private const long Row = 0x5F3759DFL;

    public static long Of(MagiState st)
    {
        long h = -3750763034362895579L;
        void Mix(long v) => h = h * 1099511628211L + v;
        void Txt(string? t)
        {
            if (t is null) { Mix(0); return; }
            foreach (var c in t) Mix(c);
            Mix(1);
        }

        Txt(st.StartDate);
        Txt(st.EndDate);
        Mix(st.Use2Patterns ? 1 : 0);

        foreach (var sh in st.Shifts) { Txt(sh.Name); Txt(sh.Kigou); Txt(sh.Need1); Txt(sh.Need2); }
        foreach (var g in st.Groups) { Txt(g.Name); Txt(g.Kigou); }
        foreach (var g in st.SkillGroups) { Txt(g.Name); Txt(g.Kigou); }
        foreach (var p in st.StaffList) { Txt(p.Name); Mix(p.GroupIdx); Mix(p.SkillIdx); }

        // [3.333.0/外部レビュー] 行の境界を混ぜる。旧は行を素通しで連結していたので
        //   [[1,1],[0]] と [[1],[1,0]] が同じ値になり、担当可否の構造が違うのに指紋が一致した。
        foreach (var row in st.GroupShift) { foreach (var v in row) Mix(v); Mix(Row); }
        foreach (var row in st.GroupShiftApt) { foreach (var v in row) Txt(v); Mix(Row); }

        foreach (var kv in st.Wishes.OrderBy(e => e.Key, StringComparer.Ordinal)) { Txt(kv.Key); Mix(kv.Value); }
        foreach (var kv in st.StaffRange.OrderBy(e => e.Key, StringComparer.Ordinal)) { Txt(kv.Key); Txt(kv.Value.Lo); Txt(kv.Value.Hi); }
        foreach (var kv in st.NeedDay1.OrderBy(e => e.Key, StringComparer.Ordinal)) { Txt(kv.Key); Txt(kv.Value); }
        foreach (var kv in st.NeedDay2.OrderBy(e => e.Key, StringComparer.Ordinal)) { Txt(kv.Key); Txt(kv.Value); }

        foreach (var c in st.Cons1) { Txt(c.Day1); Txt(c.ShiftKigou); Txt(c.Day2); }
        foreach (var c in st.Cons2) { Txt(c.ShiftKigou); Txt(c.Count); }

        // 連続パターンは行の長さが可変。行の境界を入れないと [["A","B"]] と [["A"],["B"]] が衝突する。
        foreach (var fam in new[] { st.Cons3, st.Cons3n, st.Cons3m, st.Cons3mn })
        {
            Mix(2);
            foreach (var row in fam) { foreach (var t in row.Pattern) Txt(t); Mix(Row); }
        }
        foreach (var fam in new[] { st.Cons41, st.Cons41s })
        {
            Mix(3);
            foreach (var c in fam) { Txt(c.GroupKigou); Txt(c.ShiftKigou); Txt(c.L); Txt(c.U); }
        }
        foreach (var fam in new[] { st.Cons42, st.Cons42s })
        {
            Mix(4);
            foreach (var c in fam) { Txt(c.G1Kigou); Txt(c.G2Kigou); Txt(c.S1Kigou); Txt(c.S2Kigou); }
        }

        return h;
    }
}
