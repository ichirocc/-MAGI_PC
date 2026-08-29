using System.Text.Json;
using MagiApp.ViewModels.Tests.TestSupport;
using MagiApp.ViewModels.Work;
using MagiEngine.Model;
using MagiEngine.V6;
// MagiEngine.Model.Range vs System.Range — same alias as MinimalState.cs, for the same reason.
using Range = MagiEngine.Model.Range;

namespace MagiApp.ViewModels.Tests;

/// <summary>
/// [フェーズ9 ピース10] <c>MagiViewModel.Csv.cs</c>（出力クラスタ・取込クラスタ・ファイルI/O通知
/// ヘルパー）の検証。Kotlin原本には専用テストが無い（他ピースと同じ経緯）。
///
/// このピースの入口は3種の非同期経路にまたがる:
///  - <see cref="MagiViewModel.LastLoadTask"/> 経由（<c>ImportCsvSmart</c>/<c>ImportRosterAs</c>
///    のロースター/フラットロースター成功時）。<c>LoadAsyncCoreAsync</c> は成功時に自前で
///    <c>LogOp("I", "読込 …")</c> を追記するため、呼出元が先に積んだ操作ログ行は完了後
///    <c>OpLog[0]</c> ではなく後方へ押し出される——このピースの経路では**全件検索**でアサートする
///    （<see cref="MagiViewModelWs1Test"/> 等の <c>RefreshCheck()</c> 経由と同じ理由・同じ回避策）。
///  - <see cref="MagiViewModel.LastImportCsvTask"/> 経由（<c>ImportCsv</c>）。
///  - <see cref="MagiViewModel.LastApplyStructureWithMessageTask"/> 経由（<c>ImportStaffCsv</c>/
///    <c>ImportWishesCsv</c>/<c>ImportConstraintsCsv</c>）。<c>ApplyStructureWithMessageCoreAsync</c>
///    はどの分岐でも <c>LogOp</c> を呼ばないため、呼出元が先に積んだ行は完了後も <c>OpLog[0]</c> の
///    ままで安全にアサートできる。
///
/// 編集ガードはすべて <see cref="MagiViewModel.OptimizeInFlight"/> 経由で
/// <see cref="OptimizationRepository.Running"/> を読むため、他ピースと同じ直列コレクションに属する。
/// ファイルI/Oは伴わない（<c>_hydrated</c> が既定 false のため <c>AutoSave()</c> は常に無害な no-op）
/// ので <c>DataDir</c> は不要。
/// </summary>
[Collection("OptimizationRepositoryState")]
public class MagiViewModelCsvTest
{
    public MagiViewModelCsvTest()
    {
        OptimizationRepository.SetRunning(false);
        OptimizationRepository.Clear();
    }

    // ===================================================================
    // フィクスチャ
    // ===================================================================

    /// <summary>
    /// <c>MagiEngine.Tests/V6/RosterCsvImportTest.Sample</c>（private のためプロジェクトを跨いで
    /// 参照できず複製）。柳(古泉/山本)・桐(上條)の2ユニット・3日・凡例4シフト(A4,Aｱ,B4,休)。
    /// asWishes=true で希望7件（"0,0"=Aｱ・"0,2"=休・"1,0"=A4・"2,0"=B4・"2,2"=Aｱ、"0,1"は希望なし）。
    /// </summary>
    private static readonly string RosterSample = string.Join("\n", new[]
    {
        "令和8年,,,7,月",
        "ユニット名：,,柳,,1,2,3",
        "№,,氏 名,,水,木,金",
        "1,リーダー,古泉 健一,予定,Aｱ,,休",
        "2,,山本 昌幸,予定,A4,休,",
        "3,,,予定,,,",
        ",,,,,,",
        "ユニット名：,,桐,,1,2,3",
        "№,,氏 名,,水,木,金",
        "1,主任,上條 洋平,予定,B4,休,Aｱ",
        ",,,,,,",
        ",記号,時刻,休憩時間,水,木,金",
        ",A4,6:00～15:00,1h,1,0,1",
        ",Aｱ,7:30～16:30,1h,2,0,1",
        ",B4,8:30～17:30,1h,1,0,0",
        ",休,定休,,1,2,1",
    });

    /// <summary>凡例ブロックを持たないロースター（「ユニット名」は含む＝Detect=true）。
    /// <c>RosterCsvImport.Parse</c> は解析に成功するが、休シフトを補うだけで凡例が無いぶん
    /// ShiftCount==1 になる（全公休化の兆候）。</summary>
    private const string RosterNoLegend = "令和8年,,,7,月\nユニット名：,,柳,,1\n№,,氏 名,,水\n1,,職員A,予定,A4\n";

    /// <summary>ユニット列形式の最小フィクスチャ。氏名列(idx3)の右から3日分（1,2,3）。
    /// 病棟A所属2名、記号=休(先頭固定)+A(本表セルから収集)。</summary>
    private const string FlatRosterSample =
        "ユニット,No,役職,氏名,1,2,3\n病棟A,1,,職員X,A,休,A\n病棟A,2,,職員Y,休,A,休\n";

    /// <summary>ユニット列形式と判定はされる（「ユニット」+「氏名」を含む）が、氏名列の右が
    /// 連番(1,2,3…)でないため <c>FlatRosterCsvImport.Parse</c> は解析不能(t=0)で null を返す。</summary>
    private const string FlatRosterUnparseable = "ユニット,No,役職,氏名,X\n病棟A,1,,職員A,休\n";

    /// <summary>
    /// <see cref="CsvUtil.CsvBody"/> の「先頭セルがヘッダ文言と一致する行はまるごと除去する」性質を
    /// 使い、①本文が0行(=Parseがnullを返す) ②生テキストに「ユニット名」を含む(=RosterCsvImport.Detect
    /// がtrueになる)、の両方を同時に満たす1行CSV。<see cref="MagiViewModel.ComponentImportMismatchHint"/>
    /// の「勤務表全体CSVの取り違え」分岐を、氏名/種別どちらのヘッダでも共通して発火させられる。
    /// </summary>
    private static string HeaderStripMismatch(string headerFirstCell) => $"{headerFirstCell},ユニット名,x\n";

    // ===================================================================
    // EnvironmentLine
    // ===================================================================

    [Fact]
    public void EnvironmentLineIncludesInjectedFieldsCoreCountAndNativeInfo()
    {
        var vm = new MagiViewModel
        {
            AppVersionInfo = "9.9.9 (1)",
            DeviceInfo = "TestBox",
            OsInfo = "TestOS 1.0",
        };
        vm.Ui.Workers = 6;

        var line = vm.EnvironmentLine();

        Assert.Contains("9.9.9 (1)", line);
        Assert.Contains("TestBox", line);
        Assert.Contains("TestOS 1.0", line);
        Assert.Contains("並列ワーカー設定=6", line);
        Assert.Contains("非使用（フルマネージドC#実装）", line);
    }

    // ===================================================================
    // ExportCsv / ExportStaffCsv / ExportWishesCsv / ExportConstraintsCsv
    // ===================================================================

    [Fact]
    public void ExportsReturnNullWithoutState()
    {
        var vm = new MagiViewModel();
        Assert.Null(vm.ExportCsv());
        Assert.Null(vm.ExportStaffCsv());
        Assert.Null(vm.ExportWishesCsv());
        Assert.Null(vm.ExportConstraintsCsv());
    }

    [Fact]
    public void ExportCsvReturnsNullWithoutSchedule()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() };
        Assert.Null(vm.ExportCsv());
    }

    [Fact]
    public void ExportsProduceExpectedHeadersWithState()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };

        var sched = vm.ExportCsv();
        Assert.NotNull(sched);
        Assert.Contains("スタッフ", sched);
        Assert.Contains("日付", sched);
        Assert.Contains("職員A", sched);

        var staff = vm.ExportStaffCsv();
        Assert.NotNull(staff);
        Assert.Contains("氏名", staff);
        Assert.Contains("グループ", staff);
        Assert.Contains("スキル", staff);

        var wishes = vm.ExportWishesCsv();
        Assert.NotNull(wishes);
        Assert.Contains("氏名", wishes);
        Assert.Contains("希望シフト", wishes);

        var cons = vm.ExportConstraintsCsv();
        Assert.NotNull(cons);
        Assert.Contains("種別", cons);
    }

    // ===================================================================
    // ExportLogs / ExportLogsJson
    // ===================================================================

    [Fact]
    public void ExportLogsReturnsNullWhenNothingToExport()
    {
        var vm = new MagiViewModel();
        Assert.Null(vm.ExportLogs());
    }

    [Fact]
    public void ExportLogsIncludesEnvironmentAndOpLogEntry()
    {
        var vm = new MagiViewModel();
        vm.Notify("ひとつめの操作");

        var text = vm.ExportLogs();

        Assert.NotNull(text);
        Assert.Contains("MAGI ログ (Native)", text);
        Assert.Contains(vm.EnvironmentLine(), text);
        Assert.Contains("操作ログ（新しい順 1件）", text);
        Assert.Contains("ひとつめの操作", text);
        // 実行外(Run=0)の操作なので "#N " の帰属プレフィクスは付かない。
        Assert.DoesNotContain("実行#", text);
    }

    [Fact]
    public void ExportLogsMarksMultipleRunsWithSpanAndNote()
    {
        var vm = new MagiViewModel();

        var t1 = vm.BeginBoardJob("計算その1", engineRun: true);
        vm.Notify("1回目の実行中に記録");
        vm.EndBoardJob(t1);

        var t2 = vm.BeginBoardJob("計算その2", engineRun: true);
        vm.Notify("2回目の実行中に記録");
        vm.EndBoardJob(t2);

        var text = vm.ExportLogs();

        Assert.NotNull(text);
        Assert.Contains("実行#1〜#2", text);
        Assert.Contains("操作ログ（新しい順 2件・実行#1〜#2）", text);
        Assert.Contains("#1 1回目の実行中に記録", text);
        Assert.Contains("#2 2回目の実行中に記録", text);
        // 実行が2回以上あるので、診断ログがどちらの実行のものかを明示する注記が付く。
        Assert.Contains("※操作ログは複数回の実行を含みます", text);
    }

    [Fact]
    public void ExportLogsJsonReturnsNullWhenNothingToExport()
    {
        var vm = new MagiViewModel();
        Assert.Null(vm.ExportLogsJson());
    }

    [Fact]
    public void ExportLogsJsonIncludesExpectedFields()
    {
        var vm = new MagiViewModel();
        vm.Notify("JSON書き出し確認用の操作");

        var json = vm.ExportLogsJson();
        Assert.NotNull(json);
        var doc = JsonDocument.Parse(json!);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("environment", out var env));
        Assert.Equal(vm.EnvironmentLine(), env.GetString());
        Assert.True(root.TryGetProperty("opLog", out var opLog));
        Assert.Equal(1, opLog.GetArrayLength());
        Assert.Contains("JSON書き出し確認用の操作", opLog[0].GetString());
        Assert.True(root.TryGetProperty("diagRun", out var diagRun));
        Assert.Equal(0, diagRun.GetInt32()); // 実行外
        Assert.True(root.TryGetProperty("runsInOpLog", out var runs));
        Assert.Equal(0, runs.GetArrayLength()); // Run>0の実行が無い
        Assert.True(root.TryGetProperty("satisfactionMeaning", out _));
        Assert.True(root.TryGetProperty("breakdown", out _));
        // 実行時診断（lastRunDiagLog）はエンジン実行を経ていないため付かない。
        Assert.False(root.TryGetProperty("lastRunDiagLog", out _));
    }

    // ===================================================================
    // ImportCsvSmart
    // ===================================================================

    [Fact]
    public async Task ImportCsvSmartLoadsRosterTemplate()
    {
        var vm = new MagiViewModel();

        vm.ImportCsvSmart(RosterSample);
        Assert.NotNull(vm.LastLoadTask);
        await vm.LastLoadTask!;

        Assert.NotNull(vm._state);
        Assert.Equal(3, vm._state!.StaffCount);
        Assert.Equal(3, vm._state.DayCount);
        Assert.Equal(4, vm._state.ShiftCount);
        Assert.Equal(2, vm._state.GroupCount);
        Assert.False(vm.Ui.Running);
        Assert.Contains(vm.Ui.OpLog, l => l.Contains("勤務表CSVを新規取込") && l.Contains("3名"));
        Assert.Contains("期間は「2026-07-01」から", vm.Ui.Message);
    }

    [Fact]
    public void ImportCsvSmartRejectsRosterWithoutLegend()
    {
        var vm = new MagiViewModel();

        vm.ImportCsvSmart(RosterNoLegend);

        Assert.Null(vm._state); // Load は呼ばれない
        Assert.Null(vm.LastLoadTask);
        Assert.True(vm.Ui.MessageIsError);
        Assert.Contains("凡例", vm.Ui.Message);
        Assert.Contains(vm.Ui.OpLog, l => l.Contains("凡例なし"));
    }

    [Fact]
    public async Task ImportCsvSmartLoadsFlatRoster()
    {
        var vm = new MagiViewModel();

        vm.ImportCsvSmart(FlatRosterSample);
        Assert.NotNull(vm.LastLoadTask);
        await vm.LastLoadTask!;

        Assert.NotNull(vm._state);
        Assert.Equal(2, vm._state!.StaffCount);
        Assert.Equal(3, vm._state.DayCount);
        Assert.Equal(2, vm._state.ShiftCount); // 休(先頭)+A
        Assert.Equal(1, vm._state.GroupCount);
        Assert.Contains(vm.Ui.OpLog, l => l.Contains("ユニット列形式") && l.Contains("2名"));
        Assert.Contains("推定", vm.Ui.Message + string.Join("", vm.Ui.OpLog)); // 期間は推定である旨がどこかに出る
    }

    [Fact]
    public void ImportCsvSmartRejectsUnparseableFlatRoster()
    {
        var vm = new MagiViewModel();

        vm.ImportCsvSmart(FlatRosterUnparseable);

        Assert.Null(vm._state);
        Assert.Null(vm.LastLoadTask);
        Assert.True(vm.Ui.MessageIsError);
        Assert.Contains("ユニット列形式と判定しましたが解析できませんでした", vm.Ui.Message);
        Assert.Contains(vm.Ui.OpLog, l => l.Contains("解析不能"));
    }

    [Fact]
    public void ImportCsvSmartGivesGuidanceWithNoStateAndUnrecognizedFormat()
    {
        var vm = new MagiViewModel();

        vm.ImportCsvSmart("foo,bar\nbaz,qux\n");

        Assert.Null(vm._state);
        Assert.True(vm.Ui.MessageIsError);
        Assert.Contains("データを開く", vm.Ui.Message);
    }

    [Fact]
    public async Task ImportCsvSmartDelegatesToImportCsvWhenStateExists()
    {
        var st = MinimalState.Build();
        var sched = MinimalState.BuildSchedule();
        var vm = new MagiViewModel { _state = st, _currentSchedule = sched };
        var csvText = ScheduleCsvBridge.Build(st, sched);

        vm.ImportCsvSmart(csvText);

        Assert.NotNull(vm.LastImportCsvTask);
        await vm.LastImportCsvTask!;
        Assert.Contains("CSV取込完了", vm.Ui.Message);
    }

    // ===================================================================
    // ImportRosterAs
    // ===================================================================

    [Fact]
    public async Task ImportRosterAsScheduleLoadsGridDirectly()
    {
        var vm = new MagiViewModel();

        vm.ImportRosterAs(RosterSample, asWishes: false);
        Assert.NotNull(vm.LastLoadTask);
        await vm.LastLoadTask!;

        Assert.NotNull(vm._state);
        Assert.Equal(3, vm._state!.StaffCount);
        Assert.Empty(vm._state.Wishes);
        var k = vm._state.Shifts.Select((sh, idx) => (sh.Kigou, idx)).ToDictionary(x => x.Kigou, x => x.idx);
        Assert.Equal(k["Aｱ"], vm._state.Schedule[0][0]);
        Assert.Equal(k["A4"], vm._state.Schedule[1][0]);
        Assert.Equal(k["B4"], vm._state.Schedule[2][0]);
        Assert.Contains(vm.Ui.OpLog, l => l.Contains("勤務表として新規取込"));
    }

    [Fact]
    public async Task ImportRosterAsWishesFillsWishesAndSeedsScheduleFromThem()
    {
        var vm = new MagiViewModel();

        vm.ImportRosterAs(RosterSample, asWishes: true);
        Assert.NotNull(vm.LastLoadTask);
        await vm.LastLoadTask!;

        Assert.NotNull(vm._state);
        var st = vm._state!;
        var k = st.Shifts.Select((sh, idx) => (sh.Kigou, idx)).ToDictionary(x => x.Kigou, x => x.idx);
        var rest = k["休"];

        // RosterCsvImport.Parse(asWishes:true) 自体は勤務表を全公休で返す（Wishes に7件を登録）が、
        // Load() → LoadAsyncCoreAsync() は Problem.InitialAssignment() を通す。これは希望が
        // 担当可能な限り勤務表へ重ねる（HF143相当、Problem.cs:536）ため、読込後の _state.Schedule は
        // 「希望のある日=その希望シフト・無い日=元の全公休のまま」になる。希望自体が「休」の日は
        // 見た目上は変化しない。
        Assert.Equal(7, st.Wishes.Count);
        Assert.Equal(k["Aｱ"], st.Wishes["0,0"]);
        Assert.False(st.Wishes.ContainsKey("0,1"));
        Assert.Equal(rest, st.Wishes["0,2"]);
        Assert.Equal(k["A4"], st.Wishes["1,0"]);
        Assert.Equal(k["B4"], st.Wishes["2,0"]);
        Assert.Equal(k["Aｱ"], st.Wishes["2,2"]);

        Assert.Equal(k["Aｱ"], st.Schedule[0][0]);
        Assert.Equal(rest, st.Schedule[0][1]);   // 希望なし→元の全公休のまま
        Assert.Equal(rest, st.Schedule[0][2]);   // 希望が「休」
        Assert.Equal(k["A4"], st.Schedule[1][0]);
        Assert.Equal(rest, st.Schedule[1][1]);   // 希望が「休」
        Assert.Equal(rest, st.Schedule[1][2]);   // 希望なし→元の全公休のまま
        Assert.Equal(k["B4"], st.Schedule[2][0]);
        Assert.Equal(rest, st.Schedule[2][1]);   // 希望が「休」
        Assert.Equal(k["Aｱ"], st.Schedule[2][2]);

        Assert.Contains(vm.Ui.OpLog, l => l.Contains("希望シフトとして新規取込") && l.Contains("希望7件"));
    }

    [Fact]
    public void ImportRosterAsFailsForUnparseableText()
    {
        var vm = new MagiViewModel();

        vm.ImportRosterAs("not,a,roster\ncsv,at,all\n", asWishes: false);

        Assert.Null(vm._state);
        Assert.Null(vm.LastLoadTask);
        Assert.True(vm.Ui.MessageIsError);
    }

    [Fact]
    public void ImportRosterAsFailsWhenNoLegend()
    {
        var vm = new MagiViewModel();

        vm.ImportRosterAs(RosterNoLegend, asWishes: false);

        Assert.Null(vm._state);
        Assert.Null(vm.LastLoadTask);
        Assert.True(vm.Ui.MessageIsError);
        Assert.Contains("凡例", vm.Ui.Message);
        Assert.Contains(vm.Ui.OpLog, l => l.Contains("凡例なし"));
    }

    // ===================================================================
    // ImportCsv（既存データへ勤務表だけ重ねる従来の取込）
    // ===================================================================

    [Fact]
    public void ImportCsvNoOpWithoutStateOrSchedule()
    {
        var vm = new MagiViewModel();
        vm.ImportCsv("スタッフ \\ 日付,1\n職員A,A\n");
        Assert.Null(vm.LastImportCsvTask);
    }

    [Fact]
    public async Task ImportCsvFullMatchUpdatesScheduleAndPushesReport()
    {
        var st = MinimalState.Build();
        var sched = MinimalState.BuildSchedule();
        var vm = new MagiViewModel { _state = st, _currentSchedule = sched };
        var csvText = ScheduleCsvBridge.Build(st, sched);

        vm.ImportCsv(csvText);
        Assert.NotNull(vm.LastImportCsvTask);
        await vm.LastImportCsvTask!;

        Assert.False(vm.Ui.Running);
        Assert.False(vm.Ui.MessageIsError);
        Assert.True(vm.Ui.HasResult);
        Assert.Contains("CSV取込完了", vm.Ui.Message);
        Assert.Contains("2名を更新", vm.Ui.Message);
        Assert.Contains(vm.Ui.OpLog, l => l.Contains("CSV取込 完了 2名一致"));
    }

    [Fact]
    public async Task ImportCsvZeroMatchFailsWithoutMutatingState()
    {
        var st = MinimalState.Build();
        var sched = MinimalState.BuildSchedule();
        var vm = new MagiViewModel { _state = st, _currentSchedule = sched };
        // 誰の氏名とも一致しない勤務表CSV。
        var csvText = "スタッフ \\ 日付,1日目\n誰でもない人,A\n";

        vm.ImportCsv(csvText);
        Assert.NotNull(vm.LastImportCsvTask);
        await vm.LastImportCsvTask!;

        Assert.False(vm.Ui.Running);
        Assert.True(vm.Ui.MessageIsError);
        Assert.Contains("一致する職員名がありませんでした", vm.Ui.Message);
        Assert.Same(sched, vm._currentSchedule); // 適用されず元のまま
        Assert.Contains(vm.Ui.OpLog, l => l.Contains("職員名が0件一致"));
    }

    [Fact]
    public void ImportCsvBlockedWhileOptimizationRunning()
    {
        var st = MinimalState.Build();
        var sched = MinimalState.BuildSchedule();
        var vm = new MagiViewModel { _state = st, _currentSchedule = sched };
        OptimizationRepository.SetRunning(true);

        vm.ImportCsv(ScheduleCsvBridge.Build(st, sched));

        Assert.Null(vm.LastImportCsvTask);
        Assert.True(vm.Ui.MessageIsError);
        Assert.Contains("バックグラウンド計算の実行中です", vm.Ui.Message);
        Assert.Contains(vm.Ui.OpLog, l => l.Contains("CSV取込 を取り消しました"));
    }

    // ===================================================================
    // ImportStaffCsv
    // ===================================================================

    [Fact]
    public void ImportStaffCsvGivesGuidanceWithoutState()
    {
        var vm = new MagiViewModel();
        vm.ImportStaffCsv("氏名,グループ,スキル\n職員A,G0,\n");
        Assert.False(vm.Ui.MessageIsError);
        Assert.Contains("先にデータを開いてください", vm.Ui.Message);
    }

    [Fact]
    public async Task ImportStaffCsvAddsUpdatesAndWarnsOnUnknownGroup()
    {
        var st = MinimalState.Build();
        var sched = MinimalState.BuildSchedule();
        var vm = new MagiViewModel { _state = st, _currentSchedule = sched };
        var csv = "氏名,グループ,スキル\n職員A,G0,\n新人C,ZZ,\n";

        vm.ImportStaffCsv(csv);
        Assert.NotNull(vm.LastApplyStructureWithMessageTask);
        await vm.LastApplyStructureWithMessageTask!;

        Assert.Equal(3, vm._state!.StaffCount);
        var added = vm._state.StaffList[2];
        Assert.Equal("新人C", added.Name);
        Assert.Equal(0, added.GroupIdx); // 未知記号「ZZ」→先頭グループへ
        Assert.NotNull(vm._currentSchedule);
        Assert.All(vm._currentSchedule![2], v => Assert.Equal(0, v)); // 休で埋まる（既定は全シフト担当可）
        Assert.Contains("1名を新規追加", vm.Ui.Message);
        Assert.Contains("1名を更新", vm.Ui.Message);
        Assert.Contains("「ZZ」1件", vm.Ui.Message);
        Assert.Contains(vm.Ui.OpLog, l => l.Contains("職員一覧CSV取込") && l.Contains("追加1 更新1"));
    }

    [Fact]
    public void ImportStaffCsvFailsWithMismatchHintForRosterCsv()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };

        vm.ImportStaffCsv(HeaderStripMismatch("氏名"));

        Assert.Null(vm.LastApplyStructureWithMessageTask);
        Assert.True(vm.Ui.MessageIsError);
        Assert.Contains("追加0・更新0", vm.Ui.Message);
        Assert.Contains("データ全体（新規）", vm.Ui.Message);
        Assert.Contains(vm.Ui.OpLog, l => l.Contains("職員一覧CSV取込 失敗: 0件"));
    }

    // ===================================================================
    // ImportWishesCsv
    // ===================================================================

    [Fact]
    public void ImportWishesCsvGivesGuidanceWithoutState()
    {
        var vm = new MagiViewModel();
        vm.ImportWishesCsv("氏名,日,希望シフト\n職員A,1,A\n");
        Assert.False(vm.Ui.MessageIsError);
        Assert.Contains("先にデータを開いてください", vm.Ui.Message);
    }

    [Fact]
    public async Task ImportWishesCsvAppliesValidRow()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };

        vm.ImportWishesCsv("氏名,日,希望シフト\n職員A,1,A\n");
        Assert.NotNull(vm.LastApplyStructureWithMessageTask);
        await vm.LastApplyStructureWithMessageTask!;

        Assert.Single(vm._state!.Wishes);
        Assert.Equal(1, vm._state.Wishes["0,0"]); // 職員A(idx0)・1日目(idx0)・シフトA(idx1)
        Assert.Contains("希望シフトを取込: 1件を反映", vm.Ui.Message);
        Assert.Contains(vm.Ui.OpLog, l => l.Contains("希望シフトCSV取込: 1件を反映"));
    }

    [Fact]
    public void ImportWishesCsvAbortsOnRejectedRow()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };

        vm.ImportWishesCsv("氏名,日,希望シフト\n不明太郎,1,A\n");

        Assert.Null(vm.LastApplyStructureWithMessageTask);
        Assert.False(vm.Ui.MessageIsError);
        Assert.Contains("読めない行が1件", vm.Ui.Message);
        Assert.Empty(vm._state!.Wishes); // 全部読めたときだけ置換するので変化なし
        Assert.Contains(vm.Ui.OpLog, l => l.Contains("希望シフトCSV取込 中止"));
    }

    [Fact]
    public void ImportWishesCsvFailsWithMismatchHint()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };

        vm.ImportWishesCsv(HeaderStripMismatch("氏名"));

        Assert.Null(vm.LastApplyStructureWithMessageTask);
        Assert.True(vm.Ui.MessageIsError);
        Assert.Contains("取り込める行が0件", vm.Ui.Message);
        Assert.Contains("データ全体（新規）", vm.Ui.Message);
    }

    // ===================================================================
    // ImportConstraintsCsv
    // ===================================================================

    [Fact]
    public void ImportConstraintsCsvGivesGuidanceWithoutState()
    {
        var vm = new MagiViewModel();
        vm.ImportConstraintsCsv("種別,a,b,c,d,e\n回数下限,A,3\n");
        Assert.False(vm.Ui.MessageIsError);
        Assert.Contains("先にデータを開いてください", vm.Ui.Message);
    }

    [Fact]
    public async Task ImportConstraintsCsvAppliesValidRow()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };

        vm.ImportConstraintsCsv("種別,a,b,c,d,e\n回数下限,A,3\n");
        Assert.NotNull(vm.LastApplyStructureWithMessageTask);
        await vm.LastApplyStructureWithMessageTask!;

        Assert.Single(vm._state!.Cons2);
        Assert.Equal("A", vm._state.Cons2[0].ShiftKigou);
        Assert.Equal("3", vm._state.Cons2[0].Count);
        Assert.Contains("各制約を取込: 1件を反映", vm.Ui.Message);
        Assert.Contains(vm.Ui.OpLog, l => l.Contains("各制約CSV取込: 1件を反映"));
    }

    [Fact]
    public void ImportConstraintsCsvAbortsOnRejectedRow()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };

        vm.ImportConstraintsCsv("種別,a,b,c,d,e\n個人レンジ,不明太郎,A,2,4\n");

        Assert.Null(vm.LastApplyStructureWithMessageTask);
        Assert.False(vm.Ui.MessageIsError);
        Assert.Contains("読めない行が1件", vm.Ui.Message);
        Assert.Empty(vm._state!.StaffRange);
        Assert.Contains(vm.Ui.OpLog, l => l.Contains("各制約CSV取込 中止"));
    }

    [Fact]
    public void ImportConstraintsCsvFailsWithMismatchHint()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };

        vm.ImportConstraintsCsv(HeaderStripMismatch("種別"));

        Assert.Null(vm.LastApplyStructureWithMessageTask);
        Assert.True(vm.Ui.MessageIsError);
        Assert.Contains("取り込める行が0件", vm.Ui.Message);
        Assert.Contains("データ全体（新規）", vm.Ui.Message);
    }

    // ===================================================================
    // NotifySave / NotifyOpenFailure / IoReason（IoOutcome経由）
    // ===================================================================

    [Fact]
    public void NotifySaveReportsSuccess()
    {
        var vm = new MagiViewModel();
        vm.NotifySave(MagiViewModel.IoOutcome.Ok(), "設定");
        Assert.False(vm.Ui.MessageIsError);
        Assert.Equal("設定を保存しました", vm.Ui.Message);
    }

    [Fact]
    public void NotifySaveReportsFailureWithReason()
    {
        var vm = new MagiViewModel();
        vm.NotifySave(MagiViewModel.IoOutcome.Fail(new UnauthorizedAccessException()), "設定");
        Assert.True(vm.Ui.MessageIsError);
        Assert.Contains("設定を保存できませんでした", vm.Ui.Message);
        Assert.Contains("アクセスが許可されていません", vm.Ui.Message);
    }

    [Fact]
    public void NotifyOpenFailureReportsReason()
    {
        var vm = new MagiViewModel();
        vm.NotifyOpenFailure(MagiViewModel.IoOutcome.Fail(new FileNotFoundException()), "バックアップ");
        Assert.True(vm.Ui.MessageIsError);
        Assert.Contains("バックアップを開けませんでした", vm.Ui.Message);
        Assert.Contains("ファイルが見つからないか", vm.Ui.Message);
    }

    [Theory]
    [InlineData(null, "内容が空でした")]
    [InlineData("space-error", "保存先の空き容量が足りません")]
    [InlineData("fallback", "InvalidOperationException")]
    public void IoReasonMapsExceptionsToUserFacingText(string? kind, string expectedSubstring)
    {
        Exception? ex = kind switch
        {
            null => null,
            "space-error" => new IOException("There is not enough space on the disk."),
            "fallback" => new InvalidOperationException("something else"),
            _ => null,
        };
        var vm = new MagiViewModel();
        vm.NotifySave(MagiViewModel.IoOutcome.Fail(ex), "何か");
        Assert.Contains(expectedSubstring, vm.Ui.Message);
    }
}
