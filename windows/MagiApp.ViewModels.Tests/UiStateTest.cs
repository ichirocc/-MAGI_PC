using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>
/// [フェーズ9] <c>UiState</c>（<c>MagiUiState.kt</c> の <c>data class UiState</c> の移植）の検証。
/// Kotlin原本には専用テストが無い（package com.magi.app.ui はUI層でAndroidに依存しホストJVMで
/// コンパイル・実行できないため）。この移植ではプラットフォーム非依存クラスライブラリへ切り出した
/// ことで初めてテスト可能になった＝新規に書き下ろす。
/// </summary>
public class UiStateTest
{
    [Fact]
    public void DefaultsMatchTheKotlinDataClassDeclaration()
    {
        var s = new UiState();

        Assert.False(s.Loaded);
        Assert.False(s.CanUndo);
        Assert.False(s.CanRedo);
        Assert.Equal(0, s.Staff);
        Assert.Equal(0, s.Days);
        Assert.False(s.Use2);
        Assert.Equal(0L, s.InitHard);
        Assert.False(s.Running);
        Assert.False(s.HasResult);

        // budgetSec=300 / nativeAccel=true / nativeParity=true / softPolish=true は
        // Kotlin原本の非既定値そのままの明示デフォルト。
        Assert.Equal(300, s.BudgetSec);
        Assert.True(s.NativeAccel);
        Assert.True(s.NativeParity);
        Assert.False(s.BlockSwapC3nFilter);
        Assert.False(s.WideC3nBreak);
        Assert.True(s.SoftPolish);
        Assert.Equal(V6Algorithm.Auto, s.V6Algorithm);

        // workers = Runtime.getRuntime().availableProcessors().coerceIn(1, 8) の等価物。
        // 実機のコア数に依らず [1,8] の範囲に収まることだけを検証する（決定的）。
        Assert.InRange(s.Workers, 1, 8);

        Assert.Equal("", s.FixFocusName);
        Assert.Equal("", s.ViolationColorHex);
        Assert.Equal("", s.ViolationSoftColorHex);
        Assert.Equal("", s.StartDate);
        Assert.Null(s.Message);
        Assert.False(s.MessageIsError);
        Assert.Null(s.CopilotHint);

        Assert.Null(s.V6);
        Assert.Null(s.CoverageDiag);
        Assert.Null(s.ForbiddenDiag);
        Assert.Null(s.C1Plateau);

        Assert.Empty(s.Logs);
        Assert.Empty(s.StaffNames);
        Assert.Empty(s.Schedule);
        Assert.Empty(s.LiveSchedule);
        Assert.Empty(s.FixSuggestions);
        Assert.Empty(s.PinTargets);
        Assert.Empty(s.SettingIssues);
        Assert.Empty(s.Wishes);
        Assert.Empty(s.ViolationCells);
        Assert.Empty(s.ViolationFamilyColorHex);
    }

    /// <summary>
    /// Kotlin: <c>internal val emptyBreakdown: Map&lt;String, Int&gt; = MirrorKeys.all.associateWith { 0 }</c>
    /// — 空マップではなく19族すべてを0で埋めたマップが breakdown の既定値である、という原本の
    /// 意図を維持していることを固定する。
    /// </summary>
    [Fact]
    public void BreakdownDefaultsToAllNineteenFamiliesZeroedNotAnEmptyMap()
    {
        var s = new UiState();

        Assert.Equal(MirrorKeys.All.Count, s.Breakdown.Count);
        foreach (var key in MirrorKeys.All)
        {
            Assert.True(s.Breakdown.TryGetValue(key, out var v), $"missing key: {key}");
            Assert.Equal(0, v);
        }
    }

    [Theory]
    [InlineData(nameof(UiState.Loaded))]
    [InlineData(nameof(UiState.Staff))]
    [InlineData(nameof(UiState.InitHard))]
    [InlineData(nameof(UiState.WeightedScore))]
    [InlineData(nameof(UiState.Message))]
    [InlineData(nameof(UiState.Breakdown))]
    [InlineData(nameof(UiState.V6Algorithm))]
    [InlineData(nameof(UiState.PinTargets))]
    public void SettingEachRepresentativePropertyRaisesPropertyChanged(string propertyName)
    {
        var s = new UiState();
        var raised = new List<string>();
        s.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

        switch (propertyName)
        {
            case nameof(UiState.Loaded): s.Loaded = true; break;
            case nameof(UiState.Staff): s.Staff = 4; break;
            case nameof(UiState.InitHard): s.InitHard = 9; break;
            case nameof(UiState.WeightedScore): s.WeightedScore = 12.5; break;
            case nameof(UiState.Message): s.Message = "hi"; break;
            case nameof(UiState.Breakdown): s.Breakdown = new Dictionary<string, int> { ["c1"] = 3 }; break;
            case nameof(UiState.V6Algorithm): s.V6Algorithm = V6Algorithm.Portfolio; break;
            case nameof(UiState.PinTargets):
                s.PinTargets = new[] { new PinTargetView(0, 1, "職員A", "休", 10, 3) };
                break;
        }

        Assert.Contains(propertyName, raised);
    }

    [Fact]
    public void PinTargetViewHasStructuralEquality()
    {
        var a = new PinTargetView(0, 1, "職員A", "休", 10, 3);
        var b = new PinTargetView(0, 1, "職員A", "休", 10, 3);
        var c = a with { Attempts = 4 };

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Equal(0, a.Staff);
        Assert.Equal(1, a.Shift);
        Assert.Equal("職員A", a.StaffName);
        Assert.Equal("休", a.ShiftKigou);
        Assert.Equal(10, a.PinnedCount);
        Assert.Equal(3, a.Attempts);
    }
}
