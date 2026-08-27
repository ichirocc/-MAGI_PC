// MagiEngine.GoldenGen — 使い捨てのオラクル生成ツール（非配布）。
//
// フェーズ0時点ではまだ何もしなかった。フェーズ4（初期解生成＋薄い入口）で、計画書自身が
// 提案する「フィクスチャ読込→生成→検査→hard/soft表示」を通す薄いコンソールハーネスとして
// 最小実装した — 人間が実際に目で見て確認できる中間チェックポイント（合否ゲートは引き続き
// MagiEngine.Tests のフィクスチャ回帰テストが担う。これは補助的な可視化のみ）。
//
// フェーズ3（パリティ三角形）でのKotlin側オラクル一括生成という当初の想定用途は、実際には
// パリティテストが独立に導出した期待値で足りたため未使用のまま。将来その用途が要れば
// このファイルへ追記する。

using MagiEngine;
using MagiEngine.Model;
using MagiEngine.V6;

Console.WriteLine($"MagiEngine.GoldenGen — ported from Kotlin {EngineInfo.PortedFromVersion}");

string? root = AppContext.BaseDirectory;
while (root is not null && !File.Exists(Path.Combine(root, "Magi.sln"))) root = Path.GetDirectoryName(root);
if (root is null)
{
    Console.WriteLine("Magi.sln が見つかりません（ソリューションルート外で実行された可能性）。");
    return 1;
}
string fixturesDir = Path.Combine(root, "MagiEngine.Tests", "Fixtures");

string[] fixtures = { "golden_state.json", "sample_state_v6.json", "blocked_covu_state.json", "sept2026_state.json" };
foreach (var name in fixtures)
{
    var path = Path.Combine(fixturesDir, name);
    if (!File.Exists(path)) { Console.WriteLine($"{name}: (フィクスチャが見つかりません: {path})"); continue; }

    var state = StateJsonSerializer.Parse(File.ReadAllText(path));
    Console.WriteLine($"\n== {name} ({state.StaffCount}名 × {state.DayCount}日 × {state.ShiftCount}シフト) ==");

    var smart = SmartInitialScheduler.Generate(state);
    Console.WriteLine($"  SmartInitialScheduler : HARD={smart.Report.Hard,-4} SOFT={smart.Report.Soft,-5} " +
        $"total={smart.Report.Total,-5} weighted={smart.Report.WeightedScore}");

    var greedy = GreedyMirrorScheduler.Generate(state);
    Console.WriteLine($"  GreedyMirrorScheduler  : HARD={greedy.Report.Hard,-4} SOFT={greedy.Report.Soft,-5} " +
        $"total={greedy.Report.Total,-5} weighted={greedy.Report.WeightedScore}");

    var check = V6FinalPort.HandleCheck(state);
    Console.WriteLine($"  V6FinalPort.HandleCheck(入力そのまま): HARD={check.Report.Hard,-4} SOFT={check.Report.Soft,-5} " +
        $"total={check.Report.Total}");
}

return 0;
