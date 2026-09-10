using System;
using Microsoft.UI.Dispatching;

namespace MagiApp.WinUI;

/// <summary>
/// [2026-09-10, ユーザー報告「カクつく/フリーズする/キャンセルが効かない」] PropertyChanged の連打を
/// 1回の再描画へ間引く。最適化の進捗通知は1秒ごとに約220msのバースト窓へワーカー数(既定は
/// <see cref="Environment.ProcessorCount"/> まで、最大8)ぶんが集中し、さらに1回の進捗更新が
/// <c>UiState</c> の複数プロパティ（BestHard/BestSoft/TotalViolations/Breakdown/ElapsedMs/
/// LiveSchedule）へ別々の <c>PropertyChanged</c> を発火させる。各タブが素朴に「変化のたび
/// フル Render()（グリッド全消去→再構築等）」を呼ぶと、バースト窓内で数十〜百近い重い再描画が
/// UIスレッドのディスパッチキューへ積み上がり、体感の「カクつき」「フリーズ」として現れる。
/// Stop ボタンのクリックもこのキューの後ろに並ぶため「キャンセルが効かない」ように見える
/// （<c>CancellationTokenSource.Cancel</c> 自体は正しく配線済み＝機能が無いのではなく反応が遅いだけ）。
///
/// Android版はCompose の recomposition がフレームクロック単位で自動的に間引く（同じバースト頻度でも
/// 症状が出ない）。WinUI はコードビハインド駆動描画のためこの間引きを自前で持つ必要がある。
///
/// [設計] 「保留中フラグ」1つに要求を畳み、<see cref="DispatcherQueue"/> の次のサイクルで実際の
/// 再描画を1回だけ実行する。要求が保留中の間に何度呼ばれても追加のキュー投入はしない＝
/// バースト全体で最大1回の再描画に収束する。取りこぼし（最後の状態を描き損なう）は無い——
/// 保留中に来た最新の状態は次の1回の Render() が <c>MagiViewModel.Ui</c> を直接読むため反映される。
/// </summary>
public sealed class CoalescedRender
{
    private readonly DispatcherQueue _dispatcher;
    private readonly Action _render;
    private readonly object _lock = new();
    private bool _pending;

    public CoalescedRender(DispatcherQueue dispatcher, Action render)
    {
        _dispatcher = dispatcher;
        _render = render;
    }

    /// <summary>再描画を要求する。既に保留中の要求があれば何もしない（連続要求は1回にまとまる）。</summary>
    public void Request()
    {
        lock (_lock)
        {
            if (_pending) return;
            _pending = true;
        }
        _dispatcher.TryEnqueue(() =>
        {
            lock (_lock) { _pending = false; }
            _render();
        });
    }
}
