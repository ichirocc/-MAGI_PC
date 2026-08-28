namespace MagiEngine.V6;

/// <summary>
/// [C1 頭打ちの構造化診断 / 3.322.0 移植元] 「窓の要件(c1)が最後まで残った理由」を、ログ文字列でなく
/// 構造化データとして UI まで運ぶ。
///
/// ## なぜ観測ベースなのか（静的証明にしなかった理由）
/// 「休の回数が固定（lo==hi）だから窓不足を直せない」は<b>証明できない</b>。窓の不足は
/// 「窓の外にある同じシフトを窓の中へ移す」（<c>V6HotfixPasses.ApplyC1WindowPolish</c> の手R1/R2/R3＝
/// 回数保存の再配置）でも解消しうるため、回数が動かせないことは即「直せない」を意味しない。
/// よってこの診断は<b>実際に研磨が候補を作って却下した記録</b>（観測）だけを根拠にする。
/// 「構造的に不能」とは言わない — 言えるのは「いまの設定のもとで、<b>試した</b>手が却下された」までで、
/// 試していない手の存在を否定はしない。
///
/// ## 証明つきの壁との住み分け
/// <see cref="C1RepairAnalysis.ProvenWalls"/>（coverage 入替でどう並べても焦点を解消できないことを
/// 厳密に証明する A4 診断）は別物で、こちらは一切変更しない。本診断はその手前の
/// 「探索は動いたが採用に至らなかった」層を説明する。
///
/// ## 観測の出どころ（3.349.0/敵対検証で明記）
/// 却下の記録を作っているのは <b><c>V6HotfixPasses.ApplyC1WindowPolish</c> だけ</b>。同じ後処理の
/// C1 index 駆動修復・時系列フロー・広域ビーム・厳密窓修復は c1 を直しにいくが <c>plateau</c> を返さない。
/// よって内訳は「c1 を直そうとした全部の手」ではなく「<b>同日交換・自己再配置・玉突きで試した手</b>」の
/// 範囲。<see cref="C1PlateauCause.NoCandidate"/> の文言が「この直し方では」と限定しているのはこのため。
/// </summary>
public enum C1PlateauCause
{
    /// <summary>厳密ピン(lo==hi)を目標から遠ざけるため却下された手が最多。回数固定を緩めれば通る可能性がある。</summary>
    PinConstrained,

    /// <summary>候補は作れたが、他の族が悪化するため総合的に却下された手が最多。</summary>
    ScoreTradeoff,

    /// <summary>入れ替え相手・再配置先が1つも見つからなかった（候補が生成できていない）。</summary>
    NoCandidate,
}

/// <summary>根拠の強さ。「証明」は名乗らない（<see cref="C1PlateauCause"/> のドキュメント参照）。</summary>
public enum C1PlateauEvidence
{
    /// <summary>実際に候補を作って却下された記録がある。</summary>
    Observed,

    /// <summary>候補が1件も作れず、なぜ作れないかまでは分かっていない。</summary>
    Unknown,
}

/// <summary>
/// 残った窓の要件についての内訳。<b>粒度は職員×シフト×期間の決まり（cons1 の規則）</b>。
///
/// [3.326.0] 規則index をキーに含めた。旧は職員×シフトだけで、同じシフトに複数の決まり
/// （例「休 5日で1回以上」と「休 15日で4回以上」）があると別の決まりで却下された理由が混ざって並んだ。
/// <b>同一規則の複数の窓は依然まとめて数える</b> — 1日は複数の不足窓に属しうるので代表窓を選べない
/// （選べば恣意的になる）。この限界は <see cref="RuleLabel"/> を表示して読み手が区別できる形で残す。
/// </summary>
public sealed record C1PlateauEntry(
    int Staff,
    int Shift,
    /// <summary><c>Problem.Cons1</c> の添字。同じシフトの別の決まりと区別するためのキー。</summary>
    int RuleIndex,
    string StaffName,
    string ShiftKigou,
    /// <summary>決まりの内容（例「5日で1回以上」）。どの決まりで詰まったかを画面で示すため。</summary>
    string RuleLabel,
    C1PlateauCause Cause,
    C1PlateauEvidence Evidence,
    /// <summary>厳密ピンを崩すため却下された候補の数。</summary>
    int RejectedByPin,
    /// <summary>目的関数（必須／重み／件数）で却下された候補の数。</summary>
    int RejectedByScore,
    /// <summary>候補そのものが作れなかった回数。</summary>
    int NoCandidate,
    /// <summary>目的関数で却下された候補が最も悪化させた族（重み付き・多い順）。</summary>
    IReadOnlyList<(string Family, int Count)> TopScoreCulprits)
{
    /// <summary>却下の総数（原因の判定に使った母数）。</summary>
    public int Observations => RejectedByPin + RejectedByScore + NoCandidate;

    public string Label => $"{StaffName} {ShiftKigou}（{RuleLabel}）";

    /// <summary>
    /// 利用者が次に取れる手。文言はここ1か所に置き、族名の日本語化だけ呼出側から受ける
    /// （族名の対応表は UI 層が持っている＝エンジンに複製すると必ずドリフトする）。
    /// </summary>
    /// <param name="labelOf">族キー→表示名。省略時は素のキーのまま渡す（ログ向け）。</param>
    public string RecommendedAction(Func<string, string>? labelOf = null)
    {
        var label = labelOf ?? (s => s);
        switch (Cause)
        {
            case C1PlateauCause.PinConstrained:
                // [3.324.0/外部レビュー] 「すべて」「1回ぶん」は断定しすぎ。観測できたのは
                //   「試した手のうち多くが回数固定で却下された」ことまでで、全空間の主張はできない。
                //   緩め幅の優劣は実測でデータによって逆転したので幅を決め打ちしない（HF77 と整合）。
                return "試した直し方の多くが、回数を固定している（下限＝上限）ために却下されています。" +
                    "この職員の回数の幅を見直すか、窓の要件を下げると通る可能性があります。";
            case C1PlateauCause.ScoreTradeoff:
            {
                var fam = TopScoreCulprits.Count > 0 ? TopScoreCulprits[0].Family : (string?)null;
                var famTxt = fam is null ? "他の条件" : $"「{label(fam)}」";
                return $"直し方は見つかりましたが、{famTxt}が悪化するため採用されていません。" +
                    $"{famTxt}の設定を緩めるか、窓の要件を下げてください。";
            }
            case C1PlateauCause.NoCandidate:
                // [3.327.0/外部レビュー] 観測できたのは「**この直し方（同日交換・玉突き・自己再配置）が**
                //   候補を1件も作れなかった」ことまで。他の研磨パスや探索本体はここを観測していないので、
                //   「相手が居ない」と言い切らない（3.263.0 で covU 側を正直化したのと同じ理由）。
                return "この直し方では入れ替え相手が見つかりませんでした（別の直し方までは確かめていません）。" +
                    "このシフトを担当できる職員を増やすか、窓の要件を下げると通る可能性があります。";
            default:
                throw new ArgumentOutOfRangeException(nameof(Cause), Cause, null);
        }
    }
}

/// <summary>
/// 最後の研磨で残った窓の要件の内訳。<see cref="Entries"/> が空でも <see cref="RemainingC1"/> &gt; 0 は
/// ありうる（研磨が起点に取れなかった＝観測が無い場合）。
/// </summary>
public sealed record C1PlateauDiagnosis(int RemainingC1, IReadOnlyList<C1PlateauEntry> Entries)
{
    public bool HasEntries => Entries.Count > 0;

    /// <summary>
    /// c1 は残っているのに却下の観測が1件も無い＝<b>原因未確定</b>。
    /// 研磨が起点を取れなかった／後続パスが別の窓を直して観測分だけ消えた、などで起こる。
    /// このとき「直せない理由」を語ってはいけない（何も観測していない）。
    /// </summary>
    public bool CauseUnknown => RemainingC1 > 0 && Entries.Count == 0;

    /// <summary>回数固定による却下が最多だった件数。設定画面へ誘導するかの判断に使う。</summary>
    public int PinConstrained => Entries.Count(e => e.Cause == C1PlateauCause.PinConstrained);

    /// <summary>
    /// [3.331.0/実機ログで判明] 後処理は C1研磨を<b>複数巡</b>回すので、巡ごとの観測を<b>合算</b>する。
    ///
    /// 旧実装は <c>c1Plateau = it</c> で最後の巡が前の巡を上書きしていた。2巡目は1巡目が直したあとの盤面を
    /// 見るので観測が少なく、実機ログでは<b>7箇所のうち3箇所しか説明が出ず</b>（5日窓4件は理由が一切
    /// 出ない）、件数も 24/16/22 → 6/8/12 と実際より小さく出ていた。この数は「計測できた候補試行数」と
    /// 名乗っているのだから、全巡の合計でなければ意味が合わない。
    ///
    /// 同じ (職員, シフト, 決まり) の件数を足し、主因の族も足し合わせて分類し直す。
    /// <see cref="RemainingC1"/> は新しい方（最後に観測した時点の残数）を採る。
    /// </summary>
    public C1PlateauDiagnosis MergedWith(C1PlateauDiagnosis other)
    {
        if (Entries.Count == 0) return other;
        if (other.Entries.Count == 0) return new C1PlateauDiagnosis(other.RemainingC1, Entries);

        // [insertion-order] Kotlin原本は LinkedHashMap で挿入順を保証する。C# の Dictionary は
        // 挿入順反復が事実上成立するが仕様として保証されないため、明示の順序リストを別に持つ。
        var byKey = new Dictionary<(int Staff, int Shift, int RuleIndex), C1PlateauEntry>();
        var order = new List<(int Staff, int Shift, int RuleIndex)>();
        foreach (var e in Entries.Concat(other.Entries))
        {
            var key = (e.Staff, e.Shift, e.RuleIndex);
            if (!byKey.TryGetValue(key, out var prev))
            {
                byKey[key] = e;
                order.Add(key);
                continue;
            }
            int pin = prev.RejectedByPin + e.RejectedByPin;
            int score = prev.RejectedByScore + e.RejectedByScore;
            var culprits = new Dictionary<string, int>();
            var culpritOrder = new List<string>();
            foreach (var (fam, n) in prev.TopScoreCulprits.Concat(e.TopScoreCulprits))
            {
                if (culprits.TryGetValue(fam, out var c)) culprits[fam] = c + n;
                else { culprits[fam] = n; culpritOrder.Add(fam); }
            }
            byKey[key] = prev with
            {
                Cause = CauseOf(pin, score),
                Evidence = pin + score > 0 ? C1PlateauEvidence.Observed : C1PlateauEvidence.Unknown,
                RejectedByPin = pin,
                RejectedByScore = score,
                NoCandidate = prev.NoCandidate + e.NoCandidate,
                TopScoreCulprits = culpritOrder
                    .Select(fam => (Family: fam, Count: culprits[fam]))
                    .OrderByDescending(c => c.Count)
                    .ToList(),
            };
        }
        // [3.347.0/敵対検証] 合算後も観測数の多い順に並べ直す。Build は並べていたが merge/refresh は
        //   並べ替えておらず、巡ごとに合算した結果 logLines().Take(8) と画面の一覧が「上位8件」でなく
        //   1巡目の順のまま出ていた（3.331.0 で合算を入れたときの取り残し）。
        return new C1PlateauDiagnosis(
            other.RemainingC1,
            order.Select(k => byKey[k]).OrderByDescending(e => e.Observations).ToList());
    }

    /// <summary>
    /// 後続の研磨パスが解消した箇所を落として最終盤面に合わせ直す。
    /// 診断は C1 研磨の時点で作られるが、その後に別のパスが同じ窓を直すことがあるため
    /// （残っていない箇所を「直せなかった」と見せない）。
    /// </summary>
    /// <param name="stillDeficient">最終盤面で当該窓がまだ不足しているか。</param>
    public C1PlateauDiagnosis RefreshedAgainst(int remainingC1, Func<int, int, int, bool> stillDeficient) =>
        new C1PlateauDiagnosis(
            remainingC1,
            Entries.Where(e => stillDeficient(e.Staff, e.Shift, e.RuleIndex))
                .OrderByDescending(e => e.Observations)
                .ToList());

    public IReadOnlyList<string> LogLines()
    {
        if (CauseUnknown) return new List<string>
        {
            $"[W] C1Plateau: 窓の要件(c1) {RemainingC1}件が残存 — 却下の観測がなく原因未確定",
        };
        if (!HasEntries) return Array.Empty<string>();
        var outLines = new List<string>
        {
            $"[W] C1Plateau: 窓の要件(c1) {RemainingC1}件が残存 — 直せなかった理由の内訳",
        };
        foreach (var e in Entries.Take(8))
        {
            var causeTxt = e.Cause switch
            {
                C1PlateauCause.PinConstrained => "回数固定で却下",
                C1PlateauCause.ScoreTradeoff => "他の条件とのトレードオフ",
                C1PlateauCause.NoCandidate => "候補なし",
                _ => throw new ArgumentOutOfRangeException(nameof(e), e.Cause, null),
            };
            var culprits = string.Join(" ", e.TopScoreCulprits.Take(2).Select(c => $"{c.Family}:{c.Count}"));
            outLines.Add(
                $"[W] C1Plateau: {e.Label} — {causeTxt}" +
                $"(ピン破り:{e.RejectedByPin} スコア却下:{e.RejectedByScore} 候補なし:{e.NoCandidate}" +
                (culprits.Length == 0 ? "" : $" 主因 {culprits}") + ")");
        }
        if (Entries.Count > 8) outLines.Add($"[W] C1Plateau: ほか{Entries.Count - 8}件");
        return outLines;
    }

    // ---- companion object 相当（Kotlin原本のトップレベル const/静的関数） ----

    /// <summary>却下理由の名前（<c>V6HotfixPasses.ApplyC1WindowPolish</c> の <c>RecordBlock</c> が使う文字列と対応）。</summary>
    public const string REASON_PIN = "ピン破り";
    public const string REASON_SCORE = "不採用";
    public const string REASON_NO_CANDIDATE = "候補なし";
    public const string REASON_NO_REPACK = "再配置候補なし";

    /// <summary>
    /// [分類規則] 「候補なし」は「入れ替え相手が見つかりません」という強い主張なので、
    /// 候補が1件でも作れて却下されているならこれを名乗らない（件数で多数決すると、実データで
    /// 「スコア却下8・候補なし10」→「相手が見つかりません」と案内してしまい、実際には相手が居て
    /// 禁止連続で落ちていた、という取り違えが起きる）。候補が作れているときだけ件数で比べる。
    ///
    /// <see cref="Build"/> と <see cref="MergedWith"/> の両方から呼ぶ（片方だけ直して分類がずれるのを防ぐ）。
    /// </summary>
    public static C1PlateauCause CauseOf(int pin, int score)
    {
        if (pin + score == 0) return C1PlateauCause.NoCandidate;
        if (pin > score) return C1PlateauCause.PinConstrained;
        return C1PlateauCause.ScoreTradeoff;
    }

    /// <summary>
    /// 研磨が記録した (職員,シフト,規則index)→理由別件数 から診断を組み立てる。
    /// </summary>
    /// <param name="blockStats">理由文字列→件数。上記 REASON_* のいずれか。</param>
    /// <param name="culpritStats">スコア却下時に最も悪化した族→件数。</param>
    /// <param name="stillDeficient">最終盤面でなお当該窓が不足している (職員,シフト,規則index) だけを残すための述語。</param>
    public static C1PlateauDiagnosis Build(
        int remainingC1,
        IReadOnlyDictionary<(int Staff, int Shift, int RuleIndex), IReadOnlyDictionary<string, int>> blockStats,
        IReadOnlyDictionary<(int Staff, int Shift, int RuleIndex), IReadOnlyDictionary<string, int>> culpritStats,
        Func<int, string> staffName,
        Func<int, string> shiftKigou,
        Func<int, string> ruleLabel,
        Func<int, int, int, bool> stillDeficient)
    {
        var entries = new List<C1PlateauEntry>();
        foreach (var (key, reasons) in blockStats)
        {
            var (i, x, ri) = key;
            if (!stillDeficient(i, x, ri)) continue;
            int pin = reasons.TryGetValue(REASON_PIN, out var p) ? p : 0;
            int score = reasons.TryGetValue(REASON_SCORE, out var s) ? s : 0;
            int none = (reasons.TryGetValue(REASON_NO_CANDIDATE, out var nc) ? nc : 0) +
                (reasons.TryGetValue(REASON_NO_REPACK, out var nr) ? nr : 0);
            if (pin + score + none == 0) continue;
            // [分類規則] 「候補なし」は「入れ替え相手が見つかりません」という強い主張なので、
            //   候補が1件でも作れて却下されているならこれを名乗らない（件数で多数決すると、
            //   実データで「スコア却下8・候補なし10」→「相手が見つかりません」と案内してしまい、
            //   実際には相手が居て禁止連続で落ちていた、という取り違えが起きる）。
            //   候補が作れているときだけ、ピン破りとスコア却下を件数で比べる。
            var cause = CauseOf(pin, score);
            var culprits = culpritStats.TryGetValue(key, out var c) ? c : new Dictionary<string, int>();
            entries.Add(new C1PlateauEntry(
                Staff: i,
                Shift: x,
                RuleIndex: ri,
                StaffName: staffName(i),
                ShiftKigou: shiftKigou(x),
                RuleLabel: ruleLabel(ri),
                Cause: cause,
                Evidence: pin + score > 0 ? C1PlateauEvidence.Observed : C1PlateauEvidence.Unknown,
                RejectedByPin: pin,
                RejectedByScore: score,
                NoCandidate: none,
                TopScoreCulprits: culprits
                    .Select(kv => (Family: kv.Key, Count: kv.Value))
                    .OrderByDescending(kv => kv.Count)
                    .ToList()));
        }
        return new C1PlateauDiagnosis(remainingC1, entries.OrderByDescending(e => e.Observations).ToList());
    }
}
