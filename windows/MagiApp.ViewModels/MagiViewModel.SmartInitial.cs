using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>
/// [フェーズ9, Services/UseCases/DI層] <c>MagiViewModel.kt</c> の <c>generateSmartInitial()</c>
/// （969-1018行）の移植——希望シフト→C1(窓の要件)→日別必要人数→個人下限→残り埋め の順で
/// 初期解を組み立てる「初期解生成(賢い版)」。本最適化(SA/ALNS)へは続けない
/// （続けて最適化したい場合は利用者がこの後に別途「勤務表をつくる」を押す）。
///
/// [Services/UseCases/DI層を通さない理由] <c>RunV6FullOptimize</c>/<c>RunSoftPolish</c>
/// （<c>MagiViewModel.Optimize.cs</c>）と異なり、この関数が呼ぶ <see cref="V6FinalPort.HandleSmartInitial"/>
/// は <see cref="Services.IOptimizationService"/> の境界に切り出されていない
/// （同インターフェースのクラスKDoc「<c>runV6FullOptimize</c>/<c>runSoftPolish</c> が使う2つの
/// エンジン入口だけをこの境界に切り出す」を参照——<c>HandleSmartInitial</c> は <c>HandleCheck</c> と
/// 同様、既存ピースが直接呼ぶ対象として明示的にスコープ外とされている）。したがってこのピースも
/// <see cref="V6FinalPort.HandleSmartInitial"/> を直接呼ぶ。
///
/// [Task.Run が必要な理由] <see cref="V6FinalPort.HandleSmartInitial"/> は（<c>HandleOptimize</c> と異なり）
/// 素の同期メソッド。<c>RefreshCheckCoreAsync</c>（<c>MagiViewModel.Persistence.cs</c>）が同じ理由で
/// <c>HandleCheck</c> を <c>Task.Run</c> で包むのと同じ手法で、呼出し元スレッドから外す。
///
/// [job フィールドの共有] Kotlin原本の <c>job</c>（クラスフィールド）は
/// <c>load</c>/<c>runV6FullOptimize</c>/<c>runSoftPolish</c>/<c>generateSmartInitial</c>/<c>stop</c> で
/// 共有される単一の <c>Job?</c>。この移植でも同じ <c>private CancellationTokenSource? _job</c>
/// （<c>MagiViewModel.Persistence.cs</c> で宣言済み）を再利用する（<c>MagiViewModel.Optimize.cs</c>
/// クラスKDocと同じ方針——複製すると <c>stop()</c>（未移植・別ピース）が正しいトークンを掴めなくなる）。
///
/// [finally が薄い理由] Kotlin原本の <c>generateSmartInitial</c> の <c>finally</c> は
/// <c>endBoardJob(boardToken)</c> のみ（<c>runV6FullOptimize</c> の
/// <c>terminalLogged</c>/<c>LiveSchedule</c>クリア/<c>running</c>強制解除は無い）。この移植も
/// Kotlin原本を忠実に踏襲し、意図的にこの非対称を保つ（HF77＝「直す」対象ではない）。
/// </summary>
public sealed partial class MagiViewModel
{
    /// <summary>[テスト可視性のための追加] 直近の <see cref="GenerateSmartInitial"/> 呼出しが背後で走らせる Task。</summary>
    internal Task? LastGenerateSmartInitialTask { get; private set; }

    /// <summary>
    /// 初期解生成(賢い版)。Kotlin原本 <c>generateSmartInitial()</c>（969-1018行）の移植。
    /// </summary>
    public void GenerateSmartInitial()
    {
        var st = _state;
        var sched = _currentSchedule;
        if (st is null || sched is null) return;
        // [3.271.0, 実機ログ起因の由来をそのまま記録] 実行中ガード。旧: ガード無しのため
        //   「勤務表をつくる」の直後に隣接する本ボタンを連続タップすると、走行中の最適化と
        //   初期解生成が併走し、job 参照の上書き（走行中jobが停止不能のゾンビ化）と
        //   currentSchedule の同時書き換えが起きていた。runV6FullOptimize/runSoftPolish と
        //   同じガードに統一するが、Kotlin原本はここだけ messageIsError=false（他とは異なり
        //   RunBlockedByInFlight を使わない専用の穏やかな文言）——逐語移植のためそのまま踏襲する。
        if (OptimizeInFlight())
        {
            Ui.MessageIsError = false;
            Ui.Message = "計算の実行中は下書きをつくれません（完了または「やめる」の後にどうぞ）";
            return;
        }
        if (!EnsureValidForRun(st, sched)) return;
        PushUndo();
        Ui.MessageIsError = false;
        Ui.Running = true;
        Ui.HasResult = false;
        Ui.Message = "下書きをつくっています…";
        // [3.404.0の由来をそのまま記録] 完了時に currentSchedule/state を丸ごと差し替えるので、
        //   その間の編集を止める旗を立てる。
        var boardToken = BeginBoardJob("下書きづくり", engineRun: true);
        var cts = new CancellationTokenSource();
        _job = cts;
        LastGenerateSmartInitialTask = GenerateSmartInitialCoreAsync(st, sched.Copy2D(), boardToken, cts.Token);
    }

    private async Task GenerateSmartInitialCoreAsync(MagiState st, int[][] sched, int boardToken, CancellationToken ct)
    {
        try
        {
            var res = await Task.Run(() => V6FinalPort.HandleSmartInitial(st.WithSchedule(sched), allowImpossible: true), ct);
            _currentSchedule = res.Schedule.Copy2D();
            AutoSave();
            _resultSchedule = res.Schedule.Copy2D();
            _state = st.WithSchedule(res.Schedule);
            await PushReportAsync(_state ?? st, res.Schedule, res.Report, runLabel: "下書きづくり", transform: ui =>
            {
                ui.MessageIsError = false;
                ui.Running = false;
                ui.HasResult = true;
                ui.ElapsedMs = 0;
                ui.Message = $"下書きをつくりました: 必須違反={res.Report.Hard} 合計={res.Report.Total}";
            }, ct: ct);
            LogOp("I", $"初期解生成 完了 必須={res.Report.Hard} 合計={res.Report.Total}");
        }
        catch (OperationCanceledException)
        {
            // [3.404.0の由来をそのまま記録] 停止・ジョブ上書きを「失敗」と呼ばない。
            LogOp("I", "初期解生成 停止");
            Ui.MessageIsError = false;
            Ui.Running = false;
            Ui.Message = "下書きづくりを停止しました";
            throw;
        }
        catch (Exception e)
        {
            // [3.271.0の由来をそのまま記録] 失敗を操作ログにも残す。
            LogOp("W", $"初期解生成 失敗: {e.GetType().Name}: {e.Message}");
            Ui.Running = false;
            Ui.Message = $"下書きをつくれませんでした（{e.GetType().Name}）";
            Ui.MessageIsError = true;
        }
        finally
        {
            EndBoardJob(boardToken);
        }
    }
}
