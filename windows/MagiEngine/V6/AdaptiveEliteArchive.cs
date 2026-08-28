namespace MagiEngine.V6;

/// <summary>Why an elite is retained after an adaptive hypothesis epoch.</summary>
public enum AdaptiveEliteTier { Quality, Diversity, Bridge }

/// <summary>
/// One immutable elite snapshot. <see cref="Bridge"/> means the schedule is search material only: it
/// may be one HARD point above the current best and must never be returned without a later official
/// checker improvement.
///
/// [C#移植上の判断] Kotlin原本の <c>tier</c> 既定値（<c>if (bridge) BRIDGE else QUALITY</c>）は他の
/// パラメータに依存するため C# のコンストラクタ既定引数（コンパイル時定数のみ許容）では表現できない。
/// <see cref="Create"/> がその既定値計算を担う（Kotlin の
/// <c>AdaptiveElite(schedule, report, role, worker, epoch, bridge)</c> — tier省略呼出——と等価）。
/// Kotlin の <c>.copy(tier = ...)</c>/<c>.copy(schedule = ...)</c> は C# の <c>with</c> 式に対応する。
/// </summary>
public sealed record AdaptiveElite(
    int[][] Schedule,
    ViolationReport Report,
    HypothesisEpochRole Role,
    int Worker,
    int Epoch,
    bool Bridge,
    AdaptiveEliteTier Tier)
{
    public static AdaptiveElite Create(int[][] schedule, ViolationReport report, HypothesisEpochRole role, int worker, int epoch, bool bridge) =>
        new(schedule, report, role, worker, epoch, bridge, bridge ? AdaptiveEliteTier.Bridge : AdaptiveEliteTier.Quality);
}

/// <summary>
/// Faithful port of Kotlin's <c>AdaptiveEliteArchive.kt</c> (185 lines). Thread-safe bounded elite
/// archive for the asynchronous island portfolio (<c>V6NativeOptimizer.RunAdaptivePortfolio</c>,
/// phase 5d).
///
/// Exact duplicates are replaced only by an officially better report. Compression deliberately keeps
/// three different populations instead of simply taking the scalar top-N:
///  - quality: best HARD -&gt; weightedScore -&gt; total schedules,
///  - diversity: schedules far from the selected set while staying within best HARD + 1,
///  - bridge: temporary best HARD + 1 schedules used only as relinking/fusion material.
///
/// [C#移植上の判断・可視性] Kotlin原本は <c>internal class</c>。既存の踏襲パターン
/// （<see cref="HypothesisDiversityPolicy"/>・<see cref="AdaptiveHypothesisEpochPolicy"/> 等）に倣い
/// C# 側は <c>public</c> に格上げする。
///
/// [C#移植上の判断・ロック] Kotlin の <c>@Synchronized</c>（インスタンスの intrinsic monitor lock、
/// Java同様リエントラント）を、専用の <c>_gate</c> オブジェクトへの <c>lock</c> 文へ変換する（C#の
/// <c>lock</c>/<c>Monitor</c> も同一スレッドに対してリエントラント——<see cref="Register"/>（lock内）
/// から <see cref="CompactRaw"/> 経由で <see cref="Snapshot"/>（同じく lock を取る）を呼んでも
/// デッドロックしない、という Kotlin 原本の性質をそのまま保つ）。
/// </summary>
public sealed class AdaptiveEliteArchive
{
    private readonly int _rawCapacity;
    private readonly List<AdaptiveElite> _entries = new();
    private readonly object _gate = new();

    public AdaptiveEliteArchive(int rawCapacity = 64)
    {
        _rawCapacity = rawCapacity;
    }

    public void Clear()
    {
        lock (_gate) { _entries.Clear(); }
    }

    public void Register(int[][] schedule, ViolationReport report, HypothesisEpochRole role, int worker, int epoch, bool bridge)
    {
        lock (_gate)
        {
            var hash = ScheduleHash(schedule);
            for (var idx = 0; idx < _entries.Count; idx++)
            {
                var old = _entries[idx];
                if (ScheduleHash(old.Schedule) != hash || !SameSchedule(old.Schedule, schedule)) continue;
                if (Better(report, old.Report) || (SameObjective(report, old.Report) && old.Bridge && !bridge))
                {
                    _entries[idx] = AdaptiveElite.Create(schedule.Copy2D(), report, role, worker, epoch, bridge);
                }
                return;
            }
            _entries.Add(AdaptiveElite.Create(schedule.Copy2D(), report, role, worker, epoch, bridge));
            if (_entries.Count > _rawCapacity) CompactRaw();
        }
    }

    public int Size()
    {
        lock (_gate) { return _entries.Count; }
    }

    public List<AdaptiveElite> Snapshot(
        int[][] referenceSchedule,
        ViolationReport referenceReport,
        int maxQuality = 4,
        int maxDiversity = 4,
        int maxBridge = 4)
    {
        lock (_gate)
        {
            if (_entries.Count == 0) return new List<AdaptiveElite>();
            var selected = new List<AdaptiveElite>(maxQuality + maxDiversity + maxBridge);

            var quality = _entries
                .Where(e => !e.Bridge && e.Report.Hard <= referenceReport.Hard)
                .OrderBy(e => e, EliteComparer)
                .ToList();
            foreach (var e in quality)
            {
                if (selected.Count >= maxQuality) break;
                AddUnique(selected, e with { Tier = AdaptiveEliteTier.Quality });
            }

            var diversityPool = _entries
                .Where(e => !e.Bridge && e.Report.Hard <= referenceReport.Hard + 1)
                .Where(candidate => !selected.Any(s => SameSchedule(s.Schedule, candidate.Schedule)))
                .ToList();
            for (var n = 0; n < maxDiversity; n++)
            {
                if (diversityPool.Count == 0) continue;
                var bestIndex = 0;
                var bestDistance = int.MinValue;
                for (var idx = 0; idx < diversityPool.Count; idx++)
                {
                    var candidate = diversityPool[idx];
                    var distance = selected.Count == 0
                        ? ScheduleDistance(referenceSchedule, candidate.Schedule)
                        : selected.Min(s => ScheduleDistance(s.Schedule, candidate.Schedule));
                    if (distance > bestDistance ||
                        (distance == bestDistance && Better(candidate.Report, diversityPool[bestIndex].Report)))
                    {
                        bestDistance = distance;
                        bestIndex = idx;
                    }
                }
                var chosen = diversityPool[bestIndex] with { Tier = AdaptiveEliteTier.Diversity };
                diversityPool.RemoveAt(bestIndex);
                AddUnique(selected, chosen);
            }

            var bridgePool = _entries
                .Where(e => e.Bridge || e.Report.Hard == referenceReport.Hard + 1)
                .Where(e => e.Report.Hard <= referenceReport.Hard + 1)
                .OrderByDescending(e => ScheduleDistance(referenceSchedule, e.Schedule))
                .ThenBy(e => e, EliteComparer)
                .ToList();
            var bridges = 0;
            foreach (var e in bridgePool)
            {
                if (bridges >= maxBridge) break;
                if (AddUnique(selected, e with { Tier = AdaptiveEliteTier.Bridge })) bridges++;
            }

            return selected.Select(e => e with { Schedule = e.Schedule.Copy2D() }).ToList();
        }
    }

    public List<AdaptiveElite> AllForTest()
    {
        lock (_gate) { return _entries.Select(e => e with { Schedule = e.Schedule.Copy2D() }).ToList(); }
    }

    // [Kotlin原本の性質・リエントラントロック] Register の lock 内から呼ばれる（同一スレッドの再入なので
    //   下の Snapshot 呼出は安全）。Kotlin原本は private fun（@Synchronizedなし）— 呼出元が既にロック
    //   済みであることに依拠する設計をそのまま踏襲する。
    private void CompactRaw()
    {
        if (_entries.Count <= _rawCapacity) return;
        var best = _entries.MinBy(e => e, EliteComparer);
        if (best is null) return;
        var keep = Snapshot(best.Schedule, best.Report, maxQuality: 8, maxDiversity: 8, maxBridge: 8);
        _entries.Clear();
        _entries.AddRange(keep.Take(_rawCapacity));
    }

    private static bool AddUnique(List<AdaptiveElite> target, AdaptiveElite candidate)
    {
        if (target.Any(e => SameSchedule(e.Schedule, candidate.Schedule))) return false;
        target.Add(candidate);
        return true;
    }

    // ── companion object 相当（static） ──

    /// <summary>[3.352.0, Kotlin原本] 3キーを写さず MirrorCore.reportComparator（ここでは
    /// <see cref="UnifiedViolationChecker.ReportComparer"/>）へ委譲。</summary>
    public static readonly IComparer<AdaptiveElite> EliteComparer =
        Comparer<AdaptiveElite>.Create((a, b) => CompareReports(a.Report, b.Report));

    public static int CompareReports(ViolationReport a, ViolationReport b) => UnifiedViolationChecker.ReportComparer.Compare(a, b);

    public static bool Better(ViolationReport a, ViolationReport b) => CompareReports(a, b) < 0;

    public static bool SameObjective(ViolationReport a, ViolationReport b) =>
        a.Hard == b.Hard && a.Total == b.Total && a.WeightedScore == b.WeightedScore;

    /// <summary>
    /// [3.266.0/hypothesis basin diversity, Kotlin原本] 変更セル数。差分幅（差分セル数＋行長の
    /// 食い違い＋どちらかにしか無い行の全セル）を数える距離関数。
    /// </summary>
    public static int ScheduleDistance(int[][] a, int[][] b)
    {
        var d = 0;
        var rows = Math.Min(a.Length, b.Length);
        for (var i = 0; i < rows; i++)
        {
            var cols = Math.Min(a[i].Length, b[i].Length);
            for (var j = 0; j < cols; j++) if (a[i][j] != b[i][j]) d++;
            d += Math.Abs(a[i].Length - b[i].Length);
        }
        for (var i = rows; i < a.Length; i++) d += a[i].Length;
        for (var i = rows; i < b.Length; i++) d += b[i].Length;
        return d;
    }

    public static bool SameSchedule(int[][] a, int[][] b)
    {
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++)
            if (!a[i].SequenceEqual(b[i])) return false;
        return true;
    }

    public static long ScheduleHash(int[][] schedule)
    {
        var h = -0x340d631b7bdddcdbL;
        foreach (var row in schedule)
        {
            h = unchecked((h ^ (long)row.Length) * 0x100000001b3L);
            foreach (var v in row) h = unchecked((h ^ ((long)v + 0x9e3779b9L)) * 0x100000001b3L);
        }
        return h;
    }
}
