using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>
/// [S6] 確定の巻き戻し用の控え（<c>docs/s6_relax_trial.md</c> §15）。RelaxAndApply が書き換える画面の欄・結果盤面・Undo/Redo の段・他の案を
/// 書き換えの前に写し、事後確認の不一致・保存失敗で <see cref="Restore"/> がそのまま戻す。S＝Undo の段、R＝結果盤面、A＝他の案の控え。
/// Kotlin <c>RelaxRollback</c> の移植。C# の UiState は可変なので、Kotlin が UiState ごと持つところを戻す欄だけの値で持つ（editRev は C# に無い）。
/// </summary>
public sealed class RelaxRollback<S, R, A>
{
    private readonly bool _hasResult, _engineRan, _ranBeforeEdit, _relaxedBoard, _structureEdited, _canUndo, _canRedo, _messageIsError, _fixSearched;
    private readonly ChangeSummary? _runSummary;
    private readonly V6FinalPort.StopSummary? _stopSummary;
    private readonly IReadOnlyList<IReadOnlyList<int>> _schedule;
    private readonly IReadOnlyList<FixSuggestion> _fixSuggestions;
    private readonly FixTierView? _fixTiers;
    private readonly string _fixDoneKey, _fixFailedKey;
    private readonly IReadOnlyList<string> _stalledHardFamilies, _alternatives;
    private readonly int _alternativeApplied;

    public IReadOnlyList<S> Undo { get; }
    public IReadOnlyList<S> Redo { get; }
    public R? ResultSchedule { get; }
    public A Alts { get; }

    public RelaxRollback(UiState ui, IReadOnlyList<S> undo, IReadOnlyList<S> redo, R? resultSchedule, A alts)
    {
        _hasResult = ui.HasResult; _engineRan = ui.EngineRan; _ranBeforeEdit = ui.RanBeforeEdit; _relaxedBoard = ui.RelaxedBoard;
        _structureEdited = ui.StructureEdited; _runSummary = ui.RunSummary; _stopSummary = ui.StopSummary; _schedule = ui.Schedule;
        _canUndo = ui.CanUndo; _canRedo = ui.CanRedo; _messageIsError = ui.MessageIsError;
        _fixSuggestions = ui.FixSuggestions; _fixTiers = ui.FixTiers; _fixSearched = ui.FixSearched; _fixDoneKey = ui.FixDoneKey;
        _fixFailedKey = ui.FixFailedKey; _stalledHardFamilies = ui.StalledHardFamilies; _alternatives = ui.Alternatives;
        _alternativeApplied = ui.AlternativeApplied;
        Undo = undo.ToList(); Redo = redo.ToList(); ResultSchedule = resultSchedule; Alts = alts;
    }

    /// <summary>いまの <paramref name="cur"/> に、控えた欄だけを戻す（実行中の表示・保存状態など巻き戻しと関係のない欄は今のまま）。</summary>
    public void Restore(UiState cur)
    {
        cur.HasResult = _hasResult; cur.EngineRan = _engineRan; cur.RanBeforeEdit = _ranBeforeEdit; cur.RelaxedBoard = _relaxedBoard;
        cur.StructureEdited = _structureEdited; cur.RunSummary = _runSummary; cur.StopSummary = _stopSummary; cur.Schedule = _schedule;
        cur.CanUndo = _canUndo; cur.CanRedo = _canRedo; cur.MessageIsError = _messageIsError;
        cur.FixSuggestions = _fixSuggestions; cur.FixTiers = _fixTiers; cur.FixSearched = _fixSearched; cur.FixDoneKey = _fixDoneKey;
        cur.FixFailedKey = _fixFailedKey; cur.StalledHardFamilies = _stalledHardFamilies; cur.Alternatives = _alternatives;
        cur.AlternativeApplied = _alternativeApplied;
    }

    /// <summary>段の控えを <paramref name="undoStack"/>・<paramref name="redoStack"/> へ書き戻す（30 段を超えて落とした最古の段も戻る）。</summary>
    public void RestoreStacks(LinkedList<S> undoStack, LinkedList<S> redoStack)
    {
        undoStack.Clear(); foreach (var s in Undo) undoStack.AddLast(s);
        redoStack.Clear(); foreach (var s in Redo) redoStack.AddLast(s);
    }
}
