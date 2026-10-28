namespace MagiApp.ViewModels.Tests;

/// <summary>Kotlin <c>RelaxRollbackTest</c> の移植。C# の UiState は可変なので「控えた欄が全部戻る」を欄ごとに比べる。</summary>
public class RelaxRollbackTest
{
    private static UiState Before() => new()
    {
        HasResult = false, EngineRan = true, RanBeforeEdit = false, RelaxedBoard = true, StructureEdited = false,
        Schedule = new List<IReadOnlyList<int>> { new List<int> { 0, 1 } }, CanUndo = false, CanRedo = true,
        StalledHardFamilies = new List<string> { "covU" }, Alternatives = new List<string> { "案1" }, AlternativeApplied = 0, FixDoneKey = "k",
    };

    private static object?[] Fields(UiState u) => new object?[]
    {
        u.HasResult, u.EngineRan, u.RanBeforeEdit, u.RelaxedBoard, u.StructureEdited, u.RunSummary, u.StopSummary, u.Schedule,
        u.CanUndo, u.CanRedo, u.MessageIsError, u.FixSuggestions, u.FixTiers, u.FixSearched, u.FixDoneKey, u.FixFailedKey,
        u.StalledHardFamilies, u.Alternatives, u.AlternativeApplied,
    };

    [Fact]
    public void RestoreReturnsEveryFieldTheApplyChanged()
    {
        var before = Before();
        var expected = Fields(before);
        var rb = new RelaxRollback<string, string, string>(before, new[] { "u1" }, new[] { "r1" }, "res", "alts");
        var applied = before;
        applied.HasResult = true; applied.EngineRan = false; applied.RanBeforeEdit = true; applied.StructureEdited = true;
        applied.RunSummary = null; applied.StopSummary = null; applied.Schedule = new List<IReadOnlyList<int>> { new List<int> { 1, 1 } };
        applied.CanUndo = true; applied.CanRedo = false; applied.StalledHardFamilies = Array.Empty<string>(); applied.Alternatives = Array.Empty<string>();
        applied.AlternativeApplied = -1; applied.FixDoneKey = ""; applied.MessageIsError = true;
        rb.Restore(applied);
        Assert.Equal(expected, Fields(applied));
    }

    [Fact]
    public void RestoreKeepsUnrelatedFields()
    {
        var before = Before();
        var rb = new RelaxRollback<string, string?, int>(before, Array.Empty<string>(), Array.Empty<string>(), null, 0);
        var budget = before.BudgetSec + 5;
        before.BudgetSec = budget;
        rb.Restore(before);
        Assert.Equal(budget, before.BudgetSec);
    }

    [Fact]
    public void RestoreStacksBringsBackDroppedOldestAndClearedRedo()
    {
        var undo = Enumerable.Range(1, 30).Select(i => $"u{i}").ToList();
        var rb = new RelaxRollback<string, string?, int>(Before(), undo, new[] { "r1", "r2" }, null, 0);
        var u = new LinkedList<string>(undo.Skip(1).Append("new"));
        var r = new LinkedList<string>();
        rb.RestoreStacks(u, r);
        Assert.Equal(undo, u.ToList());
        Assert.Equal(new[] { "r1", "r2" }, r.ToList());
    }
}
