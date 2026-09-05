using System.ComponentModel;
using System.Linq;
using MagiApp.ViewModels;
using MagiEngine.V6;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MagiApp.WinUI.Views;

/// <summary>[フェーズ9] 「ホーム」タブ。クラスKDoc（HomeView.xaml参照）。</summary>
public sealed partial class HomeView : UserControl
{
    private readonly MagiViewModel _vm;
    private readonly UiSubscription _uiSub;

    public HomeView(MagiViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        // [レビュー指摘 2026-09-04] タブはキャッシュされ再利用されるので、Unloaded で外した購読を Loaded で戻す
        //   （旧: コンストラクタで一度だけ購読＝一度離れたタブは以後の状態変化を受け取らず、表示もボタンの活性も
        //   古いままだった）。再表示時は見えていなかった間の変化をまとめて描く（UiSubscription の KDoc 参照）。
        _uiSub = new UiSubscription(_vm.Ui, OnUiChanged);
        _uiSub.Attach();
        Loaded += (_, _) => { if (_uiSub.Attach()) Render(); };
        Unloaded += (_, _) => _uiSub.Detach();
        Render();
    }

    private void OnUiChanged(object? sender, PropertyChangedEventArgs e) => Render();

    private void Render()
    {
        var ui = _vm.Ui;
        MessageText.Text = ui.Message ?? (ui.Loaded ? "" : "データを読み込んでいます…");
        SummaryText.Text = ui.Loaded
            ? $"満足度 {ui.Satisfaction} ・ 必須違反 {ui.BestHard} ・ 合計 {ui.BestSoft}"
            : "";
        var editable = ui.Loaded && !ui.Running;
        SmartInitialButton.IsEnabled = editable;
        MakeButton.IsEnabled = editable;
        SoftPolishButton.IsEnabled = editable;
        BackgroundButton.IsEnabled = editable;
        StopButton.IsEnabled = ui.Running;

        RenderCoverage(ui, editable);
        RenderAlternatives(ui, editable);
    }

    /// <summary>不足・過剰の各節に出す枠数の上限（Kotlin原本 CoverageDiagnosisCard の take(6) と同じ）。</summary>
    private const int MaxCoverageSlots = 6;

    /// <summary>担当追加の案の表示上限（Kotlin原本の take(4) と同じ）。</summary>
    private const int MaxRelaxations = 4;

    /// <summary>
    /// [phase9 #1] Kotlin原本 <c>CoverageDiagnosisCard</c> の逐語移植（見出しの場合分け=3.344.0、主因=3.406.0）。
    /// 過剰枠の「希望固定N人」は <see cref="CoverageSurplus.PinnedStaff"/> を名指しし、ボタン 1 つで
    /// <see cref="MagiViewModel.RemoveWish"/>（その職員のその日の希望を消す＝データ修正の導線、3.492.0）。
    /// </summary>
    private void RenderCoverage(UiState ui, bool editable)
    {
        var diag = ui.CoverageDiag;
        if (diag is null || (!diag.HasShortage && !diag.HasSurplus))
        {
            CoveragePanel.Visibility = Visibility.Collapsed;
            ShortageListHost.Children.Clear();
            RelaxationListHost.Children.Clear();
            SurplusListHost.Children.Clear();
            return;
        }
        CoveragePanel.Visibility = Visibility.Visible;
        RenderShortage(diag);
        RenderSurplus(ui, diag, editable);
    }

    private void RenderShortage(CoverageDiagnosis diag)
    {
        ShortageListHost.Children.Clear();
        RelaxationListHost.Children.Clear();
        if (!diag.HasShortage)
        {
            ShortagePanel.Visibility = Visibility.Collapsed;
            return;
        }
        ShortagePanel.Visibility = Visibility.Visible;
        ShortageHeadline.Text = diag.AllInfeasible
            ? $"不足 {diag.TotalShortfall} 人は全て充足不可。今のデータでは満たせません（想定内）。"
            : diag.AllBlockedNow
                ? $"不足 {diag.TotalShortfall} 人は、いまの希望・担当のままでは埋められません。希望を1件調整するか、担当を追加してください。"
                : diag.BlockedNowSlots > 0
                    ? $"不足 {diag.TotalShortfall} 人 — うち {diag.BlockedNowSlots} 枠はいまの希望のままでは埋められません（残りは再実行で解消し得ます）。"
                    : diag.InfeasibleSlots == 0
                        ? $"不足 {diag.TotalShortfall} 人は枠が足りています。再実行や設定の見直しで解消し得ます。"
                        : $"不足 {diag.TotalShortfall} 人 — 充足不可 {diag.InfeasibleSlots} 枠 / 充足可能 {diag.FixableSlots} 枠。";
        foreach (var s in diag.Shortfalls.Take(MaxCoverageSlots))
        {
            var infeasible = s.Verdict == CoverageVerdict.Infeasible;
            var body = new StackPanel { Spacing = 4 };
            var head = new Grid { ColumnSpacing = 8 };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = new TextBlock
            {
                Text = $"{s.DayLabel}  {s.ShiftSymbol}  必要{s.Need}/現状{s.Got}（不足{s.Miss}）",
                Style = StyleOf("MagiTitleSmallTextStyle"), TextWrapping = TextWrapping.Wrap,
            };
            head.Children.Add(title);
            var chip = new TextBlock
            {
                Text = infeasible ? "充足不可" : s.BlockedNow ? "今は不能" : "充足可能",
                Style = StyleOf("MagiLabelMediumTextStyle"), VerticalAlignment = VerticalAlignment.Center,
                Foreground = BrushOf(infeasible ? "MagiErrorBrush" : "MagiPrimaryBrush"),
            };
            Grid.SetColumn(chip, 1);
            head.Children.Add(chip);
            body.Children.Add(head);
            body.Children.Add(new TextBlock
            {
                Text = s.Reason, Style = StyleOf("MagiBodySmallTextStyle"), TextWrapping = TextWrapping.Wrap,
            });
            ShortageListHost.Children.Add(SlotCard(body, infeasible ? "MagiErrorContainerBrush" : "MagiSecondaryContainerBrush"));
        }
        var moreShort = diag.Shortfalls.Count - MaxCoverageSlots;
        ShortageMoreText.Visibility = moreShort > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShortageMoreText.Text = moreShort > 0 ? $"ほか {moreShort} 枠（詳細はログ出力を参照）" : "";
        RelaxationPanel.Visibility = diag.Relaxations.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var r in diag.Relaxations.Take(MaxRelaxations))
        {
            RelaxationListHost.Children.Add(new TextBlock
            {
                Text = "・" + r, Style = StyleOf("MagiBodySmallTextStyle"), TextWrapping = TextWrapping.Wrap,
            });
        }
    }

    private void RenderSurplus(UiState ui, CoverageDiagnosis diag, bool editable)
    {
        SurplusListHost.Children.Clear();
        if (!diag.HasSurplus)
        {
            SurplusPanel.Visibility = Visibility.Collapsed;
            return;
        }
        SurplusPanel.Visibility = Visibility.Visible;
        SurplusHeadline.Text = $"過剰 {diag.TotalSurplus} 人 — 在勤者を他シフトへ動かせば消えるはずが、動かない理由を枠ごとに示します。";
        foreach (var s in diag.Surpluses.Take(MaxCoverageSlots))
        {
            var body = new StackPanel { Spacing = 4 };
            body.Children.Add(new TextBlock
            {
                Text = $"{s.DayLabel}  {s.ShiftSymbol}  必要{s.Need}/現状{s.Got}（過剰{s.Excess}）",
                Style = StyleOf("MagiTitleSmallTextStyle"), TextWrapping = TextWrapping.Wrap,
            });
            var famJp = s.BlockedFamily is null ? null
                : AnalysisView.BreakdownLabels.TryGetValue(s.BlockedFamily, out var jp) ? jp : s.BlockedFamily;
            body.Children.Add(new TextBlock
            {
                Text = s.Reason + (famJp is null ? "" : $"（主因: {famJp}）"),
                Style = StyleOf("MagiBodySmallTextStyle"), TextWrapping = TextWrapping.Wrap,
            });
            foreach (var i in s.PinnedStaff ?? System.Array.Empty<int>())
            {
                var name = i >= 0 && i < ui.StaffNames.Count ? ui.StaffNames[i] : $"#{i}";
                var cancel = new Button
                {
                    Content = $"{name} の希望（{s.ShiftSymbol}）を取り消す",
                    HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 48,
                    Foreground = BrushOf("MagiErrorBrush"), IsEnabled = editable,
                };
                var staff = i;
                var day = s.DayIndex;
                cancel.Click += (_, _) => _vm.RemoveWish(staff, day);
                body.Children.Add(cancel);
            }
            SurplusListHost.Children.Add(SlotCard(body, "MagiSecondaryContainerBrush"));
        }
        var moreSurplus = diag.Surpluses.Count - MaxCoverageSlots;
        SurplusMoreText.Visibility = moreSurplus > 0 ? Visibility.Visible : Visibility.Collapsed;
        SurplusMoreText.Text = moreSurplus > 0 ? $"ほか {moreSurplus} 枠（詳細はログ出力を参照）" : "";
    }

    private static Border SlotCard(UIElement child, string brushKey) => new()
    {
        Child = child, Background = BrushOf(brushKey),
        CornerRadius = (CornerRadius)Application.Current.Resources["MagiCornerSM"],
        Padding = new Thickness((double)Application.Current.Resources["MagiSpacingMD"]),
    };

    private static Style StyleOf(string key) => (Style)Application.Current.Resources[key];

    private static Brush BrushOf(string key) => (Brush)Application.Current.Resources[key];

    private void OnMakeClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => _vm.RunV6FullOptimize();

    private void OnBackgroundClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => _vm.RunInBackground();

    private void OnStopClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => _vm.Stop();

    private void OnSmartInitialClick(object sender, RoutedEventArgs e) => _vm.GenerateSmartInitial();

    private void OnSoftPolishClick(object sender, RoutedEventArgs e) => _vm.RunSoftPolish();

    /// <summary>「他の案」の一覧＋適用ボタン。Portfolio実行後だけ <see cref="UiState.Alternatives"/> が
    /// 埋まる（それ以外のアルゴリズムでは常に空）ため、0件のときは節ごと隠す。</summary>
    private void RenderAlternatives(UiState ui, bool editable)
    {
        if (ui.Alternatives.Count == 0)
        {
            AlternativesPanel.Visibility = Visibility.Collapsed;
            AlternativesListHost.Children.Clear();
            return;
        }
        AlternativesPanel.Visibility = Visibility.Visible;
        AlternativesListHost.Children.Clear();
        // [トークン非適用, ファイル共通] FontSize=13(TextBlock)/12(Button)はMagiThemeタイポ
        // スケール(14始まり)より小さい一覧行用の調整値で厳密一致せず、Button.FontSizeはStyle
        // (TargetType=TextBlock)を型的に受け付けられないため、いずれもトークン化の対象外。
        for (var i = 0; i < ui.Alternatives.Count; i++)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new TextBlock
            {
                Text = ui.Alternatives[i], FontSize = 13, VerticalAlignment = VerticalAlignment.Center,
            });
            var apply = new Button { Content = "適用", FontSize = 12, IsEnabled = editable };
            var idx = i;
            apply.Click += (_, _) => _vm.ApplyAlternative(idx);
            row.Children.Add(apply);
            AlternativesListHost.Children.Add(row);
        }
    }
}
