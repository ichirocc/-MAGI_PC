using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using MagiApp.ViewModels;
using Windows.UI;
using MagiEngine;
using MagiEngine.V6;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MagiApp.WinUI.Views;

/// <summary>
/// [フェーズ9] 「編集」タブ。Kotlin原本の入口3分割（月次条件／職員管理／年間マスター）を
/// ComboBox のドア選択＋各ドアの <see cref="StackPanel"/> の表示切替で表す（クラスKDocは EditView.xaml 参照）。
///
/// [双方向反映のガード] <see cref="SettingsView"/> と同じ理由——<see cref="Render"/> がコントロールの値を
/// プログラム的に設定すると、その代入自体が <c>SelectionChanged</c> を発火させて逆流しうる。
/// <see cref="_syncingFromModel"/> を立てている間はイベントハンドラを no-op にする。
///
/// [ドアの選択はモデルではなく画面の状態] どのドアを開いているかは <see cref="UiState"/> に無い画面固有の
/// 状態なので <see cref="_door"/> に持つ。<see cref="Render"/> は毎回の <c>PropertyChanged</c> で走るため、
/// ここから ComboBox へ書き戻す向きに統一して、モデル更新のたびに利用者の選択が飛ばないようにする。
///
/// [実行中の抑止] 構造編集の可否の唯一の根拠は ViewModel 側（<c>OptimizeInFlight()</c>——別アセンブリからは
/// 参照できない <c>internal</c>）で、そちらが実際の拒否を行う。この画面がするのは
/// <see cref="HomeView"/> と同じ<b>表示上の</b>抑止＝<c>Ui.Loaded &amp;&amp; !Ui.Running</c> でボタンを無効化し、
/// 押しても拒否されるだけのボタンを見せないことだけ。
/// </summary>
public sealed partial class EditView : UserControl
{
    private readonly MagiViewModel _vm;
    private readonly UiSubscription _uiSub;
    private bool _syncingFromModel;

    /// <summary>開いているドア。0=月次条件 / 1=職員管理 / 2=年間マスター。</summary>
    private int _door;

    /// <summary>職員/グループ/シフトの ComboBox の中身。差分があるときだけ作り直して選択を保つ。</summary>
    private IReadOnlyList<string> _staffItems = System.Array.Empty<string>();
    private IReadOnlyList<string> _groupItems = System.Array.Empty<string>();
    private IReadOnlyList<string> _wishStaffItems = System.Array.Empty<string>();
    private IReadOnlyList<string> _wishShiftItems = System.Array.Empty<string>();
    /// <summary>[phase9 #14] 適用パネルで選んだシフト（−1＝未選択）と「その他（担当外）」の開閉。</summary>
    private int _wishSelK = -1;
    private bool _wishShowOther;
    private IReadOnlyList<string> _needDayShiftItems = System.Array.Empty<string>();
    private IReadOnlyList<string> _masterGroupItems = System.Array.Empty<string>();
    private IReadOnlyList<string> _masterShiftItems = System.Array.Empty<string>();
    private IReadOnlyList<string> _constraintRowItems = System.Array.Empty<string>();
    private IReadOnlyList<string> _masterSkillGroupItems = System.Array.Empty<string>();
    private IReadOnlyList<string> _staffSkillItems = System.Array.Empty<string>();
    private IReadOnlyList<string> _staffRangeShiftItems = System.Array.Empty<string>();
    private IReadOnlyList<string> _groupRangeGroupItems = System.Array.Empty<string>();
    private IReadOnlyList<string> _groupRangeShiftItems = System.Array.Empty<string>();

    /// <summary>制約編集(<see cref="ConstraintFamilyMetas"/>)のシフト記号欄5枠が共有する
    /// <c>_vm.ShiftKigouList()</c> のキャッシュ（5枠とも同じ中身のため1つで足りる。<see cref="SyncConstraintShiftCombos"/>）。</summary>
    private IReadOnlyList<string> _constraintShiftItems = System.Array.Empty<string>();

    /// <summary>群×シフトの担当可否・適切回数マトリクス（Ws1SetGroupShift/Ws1SetGroupApt）が
    /// 直近に組み立てた行/列の記号。群数・シフト数・記号が変わったときだけ<see cref="BuildGroupShiftMatrix"/>
    /// でグリッドを作り直す（それ以外は既存コントロールの値だけ更新——毎回作り直すと入力中の
    /// 適切回数テキストボックスからフォーカスが飛ぶため）。</summary>
    private IReadOnlyList<string> _matrixGroupKigou = System.Array.Empty<string>();
    private IReadOnlyList<string> _matrixShiftKigou = System.Array.Empty<string>();
    /// <summary>[マトリックス再設計] 担当可否セル＝セル全面がタップ標的の <see cref="Button"/>（✓/—）。
    /// 適切回数は別マトリクス（<see cref="_aptCells"/>）へ分離した。</summary>
    private readonly Dictionary<(int G, int K), Button> _matrixCells = new();
    private readonly Dictionary<(int G, int K), TextBox> _aptCells = new();
    /// <summary>行ヘッダ（群名）/列ヘッダ（シフト名）のボタン。一括操作の有効/無効を実行中に同期する。</summary>
    private readonly List<Button> _matrixHeaderButtons = new();

    /// <summary>入力欄へ最後に取り込んだ職員 index。選択が変わったときだけ名前欄を上書きする
    /// （毎回上書きすると入力途中の名前が消えるため）。</summary>
    private int _syncedStaffIndex = -1;

    /// <summary>希望シフトの月間カレンダー（<see cref="RenderWishCalendar"/>）でタップ選択中の日
    /// （1始まり）。<see cref="WishStaffCombo"/> の選択が変わったら（別人の選択を持ち越さないよう）
    /// リセットする——直近に同期した職員 index は <see cref="_wishCalendarStaffIndex"/> に持つ。</summary>
    private readonly HashSet<int> _wishSelectedDays = new();

    /// <summary>[phase9 #13] 必要人数カレンダーの選択日（1 始まり）と、選択を持ち越すシフト。</summary>
    private readonly HashSet<int> _needSelectedDays = new();
    private int _needCalendarShiftIndex = -1;
    private IReadOnlyList<string> _needCalShiftItems = Array.Empty<string>();
    private int _wishCalendarStaffIndex = -1;

    /// <summary>年間マスターのグループ選択も同じ理由で「選択が変わったときだけ取り込む」。</summary>
    private int _syncedMasterGroupIndex = -1;

    /// <summary>年間マスターのシフト選択も同じ理由で「選択が変わったときだけ取り込む」。</summary>
    private int _syncedMasterShiftIndex = -1;

    /// <summary>年間マスターのスキル区分選択も同じ理由で「選択が変わったときだけ取り込む」。</summary>
    private int _syncedMasterSkillGroupIndex = -1;

    /// <summary>制約の種類(<see cref="ConstraintFamilyCombo"/>)選択も同じ理由。種類が変わったときは
    /// 行選択・入力欄を必ずリセットする（前の種類の行indexを新しい種類へ誤って持ち越さないため）。</summary>
    private string? _syncedConstraintFamilyKey;

    /// <summary>制約の行選択(<see cref="ConstraintRowCombo"/>)も同じ理由で「選択が変わったときだけ取り込む」。</summary>
    private int _syncedConstraintRowIndex = -1;

    /// <summary>希望シフト/日別必要人数の表示上限（件）。超えた分は件数だけ添える
    /// （AnalysisView.MaxIssueRows と同じ理由——最大30名×31日で930件になりうる）。</summary>
    private const int MaxOverrideRows = 60;

    public EditView(MagiViewModel vm)
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
        _syncingFromModel = true;
        try
        {
            var ui = _vm.Ui;
            // 構造編集ができる状態か（表示上の抑止のみ。クラスKDoc参照）。
            var editable = ui.Loaded && !ui.Running;
            RenderDoor();
            RenderMonthly(ui, editable);
            RenderWish(ui, editable);
            RenderNeedDay(ui, editable);
            RenderStaff(ui, editable);
            RenderMaster(ui, editable);
            RenderSkillGroups(ui, editable);
            RenderGroupShiftMatrix(ui, editable);
        }
        finally
        {
            _syncingFromModel = false;
        }
    }

    private void RenderDoor()
    {
        DoorCombo.SelectedIndex = _door;
        DoorHintText.Text = _door switch
        {
            0 => "翌月だけの条件：希望・必要人数・例外（毎月ここから）",
            1 => "入退職・所属・資格スキル・個人の回数（随時変更）",
            _ => "毎月は変えない土台：シフト・ルール・人数（制度変更時のみ）",
        };
        MonthlyPanel.Visibility = _door == 0 ? Visibility.Visible : Visibility.Collapsed;
        StaffPanel.Visibility = _door == 1 ? Visibility.Visible : Visibility.Collapsed;
        MasterPanel.Visibility = _door == 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ===== 月次条件 =====

    private void RenderMonthly(UiState ui, bool editable)
    {
        PeriodText.Text = ui.Loaded
            ? $"対象期間: {FormatPeriod(ui.StartDate, ui.Days)}"
            : "対象期間: （データ読込中）";
        PrevMonthButton.IsEnabled = editable;
        NextStepMonthButton.IsEnabled = editable;
        NextMonthButton.IsEnabled = editable;

        // 件数は GetSetupCounts（入力ガイド用の集計・1回の呼出しで揃う）から取る。
        var c = _vm.GetSetupCounts();
        MonthlyCountsText.Text =
            $"登録済み: 希望 {c.Wishes}件 ・ 日別の必要人数（例外） {c.NeedDay}件 ・ 職員 {c.Staff}名";
        // [phase9 #5] Kotlin原本 SetupGuideCard の「次の一手」（3.482.0 の文言）。件数の行は既にあるので差分だけ足す。
        NextStepPanel.Visibility = ui.Loaded ? Visibility.Visible : Visibility.Collapsed;
        NextStepText.Text = "次の一手: " + (
            c.Staff == 0 || c.Shifts == 0 ? "基本情報（職員／シフト）を整えましょう。"
            : c.Wishes == 0 ? "次に『希望シフト』を登録すると できあがり度 が上がります。"
            : "準備OK。ホームの『勤務表をつくる』で作成できます。");
        RenderChecklist(ui);
    }

    /// <summary>
    /// [phase9 #15] 「今月の作成条件」（Kotlin原本 <c>MonthlyChecklistCard</c>、3.483.0 E-3 の形）。read-only。
    /// 希望の行はタップで下の希望シフト欄へ、入力診断の行はタップでその場に上位 6 件を開く（分析タブへ往復させない）。
    /// 原本の「▶ 勤務表をつくる」は 3.482.0 で撤去済み（フッターに一本化）なので置かない。
    /// </summary>
    private void RenderChecklist(UiState ui)
    {
        ChecklistPanel.Visibility = ui.Loaded ? Visibility.Visible : Visibility.Collapsed;
        if (!ui.Loaded) return;
        var v = _vm.MonthlyChecklist();
        ChecklistHost.Children.Clear();
        ChecklistHost.Children.Add(ChecklistRow("職員", $"{v.StaffN}名", ok: v.StaffN > 0));
        ChecklistHost.Children.Add(ChecklistRow("希望・休暇", $"{v.WishStaff}/{v.StaffN}名 入力済み", ok: v.WishStaff > 0,
            onClick: () => WishSectionTitle.StartBringIntoView()));
        ChecklistHost.Children.Add(ChecklistRow("必要人数",
            (v.NeedStdOk ? "標準あり" : "標準が未設定") + $"・例外{v.NeedExceptions}件", ok: v.NeedStdOk));
        var issues = ui.SettingIssues;
        var issueText = issues.Count == 0 ? "問題なし" : $"見直し {issues.Count}件" + (_checklistIssuesOpen ? " ▾" : " ▸");
        ChecklistHost.Children.Add(ChecklistRow("入力診断", issueText, ok: issues.Count == 0,
            onClick: issues.Count > 0 ? () => { _checklistIssuesOpen = !_checklistIssuesOpen; Render(); } : null));

        ChecklistIssuesHost.Children.Clear();
        var open = _checklistIssuesOpen && issues.Count > 0;
        ChecklistIssuesHost.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        if (!open) return;
        foreach (var iss in issues.Take(ChecklistIssueRows))
        {
            ChecklistIssuesHost.Children.Add(new TextBlock
            {
                Text = $"・{iss.Where}：{iss.Problem}", FontSize = 12, Opacity = 0.8, TextWrapping = TextWrapping.Wrap,
            });
        }
        if (issues.Count > ChecklistIssueRows)
        {
            ChecklistIssuesHost.Children.Add(new TextBlock
            {
                Text = $"ほか{issues.Count - ChecklistIssueRows}件（分析タブの設定見直しに全件）", FontSize = 12, Opacity = 0.8,
            });
        }
    }

    private const int ChecklistIssueRows = 6;
    private bool _checklistIssuesOpen;

    /// <summary>✓/！＋ラベル＋値の 1 行。onClick があるときは行全体がボタン（値の末尾に「›」）。</summary>
    private static FrameworkElement ChecklistRow(string label, string value, bool ok, Action? onClick = null)
    {
        var mark = new TextBlock
        {
            Text = ok ? "✓" : "！", FontWeight = Microsoft.UI.Text.FontWeights.Bold, Width = 16,
            Foreground = (Brush)Application.Current.Resources[ok ? "MagiTertiaryBrush" : "MagiErrorBrush"],
        };
        var lbl = new TextBlock { Text = label, Style = (Style)Application.Current.Resources["MagiBodyMediumTextStyle"] };
        var val = new TextBlock
        {
            Text = value + (onClick is null ? "" : " ›"),
            Style = (Style)Application.Current.Resources["MagiBodyMediumTextStyle"],
            Foreground = onClick is null ? new SolidColorBrush(Colors.Gray) : (Brush)Application.Current.Resources["MagiPrimaryBrush"],
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(mark, 0); Grid.SetColumn(lbl, 1); Grid.SetColumn(val, 2);
        grid.Children.Add(mark); grid.Children.Add(lbl); grid.Children.Add(val);
        if (onClick is null) return grid;
        var btn = new Button
        {
            Content = grid, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0), Padding = new Thickness(0, 4, 0, 4),
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    /// <summary>「2026-07-01」＋31日 → 「2026年7月1日 から 31日間」。読めない開始日はそのまま出す。</summary>
    private static string FormatPeriod(string startDate, int days)
    {
        if (System.DateOnly.TryParseExact(
                startDate, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var d))
        {
            return $"{d.Year}年{d.Month}月{d.Day}日 から {days}日間";
        }
        return startDate.Length == 0 ? $"{days}日間" : $"{startDate} から {days}日間";
    }

    private void OnNextMonthClick(object sender, RoutedEventArgs e)
    {
        if (_syncingFromModel) return;
        _vm.SetNextMonth();
        // 期間が変わると職員の並びは変わらないが、入力欄は取り込み直す（下記 OnAdd/Edit と同じ理由）。
        _syncedStaffIndex = -1;
    }

    /// <summary>
    /// [2026-09-02, 配線] ShiftMonth（フェーズ9で移植・テスト済み）はこれまで呼び出し口が無かった。
    /// 「来月にする」(SetNextMonth) は常に翌月へ一足飛びだが、Kotlin原本の MonthPickerCard は
    /// それに加えて「前の月」「次の月」の1か月ずつの移動も併設する。同じ理由で職員入力欄を取り込み直す。
    /// </summary>
    private void OnPrevMonthClick(object sender, RoutedEventArgs e)
    {
        if (_syncingFromModel) return;
        _vm.ShiftMonth(-1);
        _syncedStaffIndex = -1;
    }

    private void OnNextStepMonthClick(object sender, RoutedEventArgs e)
    {
        if (_syncingFromModel) return;
        _vm.ShiftMonth(1);
        _syncedStaffIndex = -1;
    }

    // ===== 希望シフト（月次条件） =====

    /// <summary>選択肢を差分更新（新規作成しない）。中身が変わらなければ選択位置を保つ。</summary>
    private static void SyncItems(ComboBox combo, IReadOnlyList<string> items, ref IReadOnlyList<string> cache)
    {
        if (cache.SequenceEqual(items)) return;
        cache = items;
        var keep = combo.SelectedIndex;
        combo.ItemsSource = items.ToList();
        combo.SelectedIndex = keep >= 0 && keep < items.Count ? keep : (items.Count > 0 ? 0 : -1);
    }

    private void RenderWish(UiState ui, bool editable)
    {
        SyncItems(WishStaffCombo, ui.StaffNames, ref _wishStaffItems);
        SyncItems(WishShiftCombo, ui.ShiftSymbols, ref _wishShiftItems);
        SetWishButton.IsEnabled = editable;

        RenderWishCalendar(ui, editable);

        WishListHost.Children.Clear();
        var rows = _vm.WishOverrides();
        ApplyWishesButton.IsEnabled = editable && rows.Count > 0;
        ClearAllWishesButton.IsEnabled = editable && rows.Count > 0;
        // [トークン非適用, ファイル共通] このファイルのFontSize値(10/11/12/13)はMagiThemeタイポ
        // スケール(14始まり=BodySmall)より小さい一覧行/密グリッド用の意図的な調整値で、どれとも
        // 厳密一致しない。Button.FontSizeはStyle(TargetType=TextBlock)を型的に受け付けられない
        // （Button.Styleの対象型が違う）ため、そもそもトークン化の対象外。
        foreach (var v in rows.Take(MaxOverrideRows))
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new TextBlock
            {
                Text = $"{v.StaffName} {v.Day}日 → {v.Kigou}", FontSize = 13, VerticalAlignment = VerticalAlignment.Center,
            });
            var remove = new Button { Content = "削除", FontSize = 12, IsEnabled = editable };
            var i = v.I; var j = v.J;
            remove.Click += (_, _) => _vm.RemoveWish(i, j);
            row.Children.Add(remove);
            WishListHost.Children.Add(row);
        }
        if (rows.Count > MaxOverrideRows)
        {
            WishListHost.Children.Add(new TextBlock { Text = $"ほか {rows.Count - MaxOverrideRows}件", FontSize = 13, Opacity = 0.8 });
        }
    }

    /// <summary>
    /// [2026-09-02, 配線] SetWishesForDays/ClearWishesForDays（フェーズ9で移植・テスト済み）はこれまで
    /// 呼び出し口が無かった——既存の希望登録は<see cref="OnSetWishClick"/>の1日ずつ(SetWish)のみで、
    /// Kotlin原本の WishMonthGrid＋WishApplyPanel（複数日をまとめて選び、まとめて反映/クリア）に
    /// 相当する導線が無かった。<see cref="WishStaffCombo"/> で選択中の職員の月間カレンダー
    /// （日曜始まり）を毎回まるごと作り直し（<see cref="ScheduleView"/> のグリッドと同じ方針。
    /// フォーカスを保つ入力欄が無いセルのみのため差分更新は不要）、タップで
    /// <see cref="_wishSelectedDays"/>（1始まりの日）へ出し入れする。1件以上選ぶと下の適用パネル
    /// （シフト選択＋「適用（N日）」/「選択した日を未設定に戻す」）を表示する。職員選択が変わったら
    /// （<see cref="_wishCalendarStaffIndex"/> で検知）選択日をリセットする（別人の選択を持ち越さない）。
    /// </summary>
    private void RenderWishCalendar(UiState ui, bool editable)
    {
        var staffIdx = WishStaffCombo.SelectedIndex;
        if (staffIdx != _wishCalendarStaffIndex)
        {
            _wishCalendarStaffIndex = staffIdx;
            _wishSelectedDays.Clear();
        }

        WishCalendarHost.Children.Clear();
        WishCalendarHost.RowDefinitions.Clear();
        WishCalendarHost.ColumnDefinitions.Clear();
        if (!ui.Loaded || ui.Days <= 0)
        {
            WishApplyPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var dow0 = 0;
        if (DateOnly.TryParseExact(
                ui.StartDate, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var start))
        {
            dow0 = (int)start.DayOfWeek; // .NET の DayOfWeek は日曜=0
        }

        var days = ui.Days;
        var rowCount = (dow0 + days + 6) / 7;
        for (var c = 0; c < 7; c++) WishCalendarHost.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var r = 0; r <= rowCount; r++) WishCalendarHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        string[] weekdayLabels = { "日", "月", "火", "水", "木", "金", "土" };
        for (var c = 0; c < 7; c++)
        {
            var color = c == 0 ? Colors.Red : (c == 6 ? Colors.RoyalBlue : Colors.Black);
            var head = new TextBlock
            {
                Text = weekdayLabels[c], FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(color), HorizontalAlignment = HorizontalAlignment.Center,
                // 2px は7段階スケール(XS=4が最小)に無い意図的な微調整値のため据え置き（トークン化するとサイズが変わる）。
                Padding = new Thickness(2),
            };
            Grid.SetRow(head, 0);
            Grid.SetColumn(head, c);
            WishCalendarHost.Children.Add(head);
        }

        // このスタッフが既に持っている希望: 日(1始まり) -> シフト記号（未反映/反映の別はここでは問わない）。
        var marked = new Dictionary<int, string>();
        if (staffIdx >= 0)
        {
            foreach (var v in _vm.WishOverrides())
                if (v.I == staffIdx) marked[v.Day] = v.Kigou;
        }

        for (var d = 1; d <= days; d++)
        {
            var col = (dow0 + d - 1) % 7;
            var row = 1 + (dow0 + d - 1) / 7;
            var selected = _wishSelectedDays.Contains(d);

            var content = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center };
            content.Children.Add(new TextBlock
            {
                Text = selected ? $"{d} ✓" : d.ToString(), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center,
                FontWeight = selected ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
            });
            // 登録済みの希望はシフト色のチップで（Kotlin原本と同じ＝色＋記号の二重符号化）。
            if (marked.TryGetValue(d, out var kigou))
            {
                // IReadOnlyList には IndexOf が無いので手で探す（LINQ 版は無い）。
                var kk = -1;
                for (var q = 0; q < ui.ShiftSymbols.Count; q++) if (ui.ShiftSymbols[q] == kigou) { kk = q; break; }
                var chipBg = kk >= 0 && kk < ui.ShiftColorHex.Count ? ColorHex.Parse(ui.ShiftColorHex[kk], Colors.LightGray) : Colors.LightGray;
                var chipFg = kk >= 0 && kk < ui.ShiftTextHex.Count ? ColorHex.Parse(ui.ShiftTextHex[kk], Colors.Black) : Colors.Black;
                content.Children.Add(new Border
                {
                    Background = new SolidColorBrush(chipBg), CornerRadius = new CornerRadius(4), Padding = new Thickness(4, 1, 4, 1),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Child = new TextBlock { Text = kigou, FontSize = 10, Foreground = new SolidColorBrush(chipFg) },
                });
            }

            var cellButton = new Button
            {
                Content = content,
                Padding = new Thickness((double)Application.Current.Resources["MagiSpacingXS"]),
                MinWidth = 44,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Background = new SolidColorBrush(selected ? Color.FromArgb(60, 0x00, 0x50, 0x4A) : Colors.Transparent),
                BorderThickness = new Thickness(selected ? 2 : 1),
                BorderBrush = selected ? (Brush)Application.Current.Resources["MagiPrimaryBrush"] : new SolidColorBrush(Colors.LightGray),
                IsEnabled = editable && staffIdx >= 0,
            };
            var day = d;
            cellButton.Click += (_, _) =>
            {
                if (_syncingFromModel) return;
                if (!_wishSelectedDays.Add(day)) _wishSelectedDays.Remove(day);
                _syncingFromModel = true;
                try
                {
                    RenderWishCalendar(_vm.Ui, editable);
                }
                finally
                {
                    _syncingFromModel = false;
                }
            };
            Grid.SetRow(cellButton, row);
            Grid.SetColumn(cellButton, col);
            WishCalendarHost.Children.Add(cellButton);
        }

        var hasSelection = staffIdx >= 0 && _wishSelectedDays.Count > 0;
        WishApplyPanel.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
        if (!hasSelection) return;
        var sorted = _wishSelectedDays.OrderBy(x => x).ToList();
        var labels = sorted.Count <= 4 ? string.Join("、", sorted.Select(DayChipLabel))
            : string.Join("、", sorted.Take(3).Select(DayChipLabel)) + $"、ほか{sorted.Count - 3}日";
        WishApplySummaryText.Text = $"{sorted.Count}日選択中: {labels}";
        RenderWishShiftButtons(ui, staffIdx, editable);
    }

    /// <summary>[phase9 #14] 適用先シフトの大ボタン（Kotlin原本 <c>ShiftButtonGrid</c>）: 担当可能を主、担当外は「その他」の下に ⚠ つきで。</summary>
    private void RenderWishShiftButtons(UiState ui, int staffIdx, bool editable)
    {
        var allowed = new HashSet<int>(_vm.AllowedShiftsFor(staffIdx));
        var all = Enumerable.Range(0, ui.ShiftSymbols.Count).ToList();
        var primary = all.Where(allowed.Contains).ToList();
        var others = all.Where(k => !allowed.Contains(k)).ToList();
        if (_wishSelK < 0 || _wishSelK >= all.Count) _wishSelK = primary.Count > 0 ? primary[0] : 0;
        void Fill(StackPanel host, List<int> idxs, bool warn)
        {
            host.Children.Clear();
            for (var start = 0; start < idxs.Count; start += 4)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                foreach (var k in idxs.Skip(start).Take(4))
                {
                    var sel = _wishSelK == k;
                    var bg = k < ui.ShiftColorHex.Count ? ColorHex.Parse(ui.ShiftColorHex[k], Colors.LightGray) : Colors.LightGray;
                    var fg = k < ui.ShiftTextHex.Count ? ColorHex.Parse(ui.ShiftTextHex[k], Colors.Black) : Colors.Black;
                    var b = new Button
                    {
                        Content = new TextBlock { Text = (sel ? "✓ " : "") + ui.ShiftSymbols[k] + (warn ? " ⚠" : ""), Foreground = new SolidColorBrush(fg), FontWeight = sel ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal },
                        Background = new SolidColorBrush(bg), MinWidth = 72, MinHeight = 52,
                        BorderThickness = new Thickness(sel ? 3 : 2),
                        BorderBrush = warn ? (Brush)Application.Current.Resources["MagiErrorBrush"] : sel ? (Brush)Application.Current.Resources["MagiPrimaryBrush"] : new SolidColorBrush(Colors.Gray),
                        IsEnabled = editable,
                    };
                    var kk = k;
                    b.Click += (_, _) => { _wishSelK = kk; RenderWishShiftButtons(_vm.Ui, staffIdx, editable); };
                    row.Children.Add(b);
                }
                host.Children.Add(row);
            }
        }
        Fill(WishShiftButtonsHost, primary, warn: false);
        Fill(WishOtherButtonsHost, others, warn: true);
        WishOtherToggle.Visibility = others.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        WishOtherToggle.Content = _wishShowOther ? "その他を閉じる" : "その他（担当外シフト）";
        WishOtherButtonsHost.Visibility = _wishShowOther && others.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var outOfScope = !allowed.Contains(_wishSelK);
        WishShiftWarnText.Visibility = outOfScope ? Visibility.Visible : Visibility.Collapsed;
        if (outOfScope) WishShiftWarnText.Text = $"⚠「{ui.ShiftSymbols[_wishSelK]}」はこの職員の担当外です。希望は登録できますが、配置すると違反になります。";
        ApplyWishesForDaysButton.Content = $"{_wishSelectedDays.Count}日に適用";
        ApplyWishesForDaysButton.IsEnabled = editable;
        ClearWishesForDaysButton.IsEnabled = editable;
    }

    private void OnWishOtherToggleClick(object sender, RoutedEventArgs e)
    {
        _wishShowOther = !_wishShowOther;
        var i = WishStaffCombo.SelectedIndex;
        if (i >= 0) RenderWishShiftButtons(_vm.Ui, i, _vm.Ui.Loaded && !_vm.Ui.Running);
    }

    private void OnCancelWishSelectionClick(object sender, RoutedEventArgs e)
    {
        _wishSelectedDays.Clear();
        var ui = _vm.Ui;
        RenderWishCalendar(ui, ui.Loaded && !ui.Running);
    }

    /// <summary>職員選択が変わったらカレンダーを作り直す（選択日リセット・「今持っている希望」チップの
    /// 更新）。<see cref="Render"/> は <c>_vm.Ui</c> の PropertyChanged でしか走らないため、ComboBoxの
    /// 選択変更そのものはここで拾う必要がある（<see cref="OnStaffSelectionChanged"/> と同じ理由）。</summary>
    private void OnWishStaffSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingFromModel) return;
        _syncingFromModel = true;
        try
        {
            var ui = _vm.Ui;
            RenderWishCalendar(ui, ui.Loaded && !ui.Running);
        }
        finally
        {
            _syncingFromModel = false;
        }
    }

    private void OnApplyWishesForDaysClick(object sender, RoutedEventArgs e)
    {
        if (_syncingFromModel) return;
        var i = WishStaffCombo.SelectedIndex;
        var k = _wishSelK;
        if (i < 0) { WishHintText.Text = "対象の職員を選んでください。"; return; }
        if (k < 0 || k >= _vm.Ui.ShiftSymbols.Count) { WishHintText.Text = "シフトを選んでください。"; return; }
        if (_wishSelectedDays.Count == 0) { WishHintText.Text = "日を選んでください。"; return; }
        var days = _wishSelectedDays.Select(d => d - 1).ToList();
        _vm.SetWishesForDays(i, days, k);
        _wishSelectedDays.Clear();
        WishHintText.Text = "";
    }

    private void OnClearWishesForDaysClick(object sender, RoutedEventArgs e)
    {
        if (_syncingFromModel) return;
        var i = WishStaffCombo.SelectedIndex;
        if (i < 0) { WishHintText.Text = "対象の職員を選んでください。"; return; }
        if (_wishSelectedDays.Count == 0) { WishHintText.Text = "日を選んでください。"; return; }
        var days = _wishSelectedDays.Select(d => d - 1).ToList();
        _vm.ClearWishesForDays(i, days);
        _wishSelectedDays.Clear();
        WishHintText.Text = "";
    }

    private void OnSetWishClick(object sender, RoutedEventArgs e)
    {
        if (_syncingFromModel) return;
        var i = WishStaffCombo.SelectedIndex;
        var k = WishShiftCombo.SelectedIndex;
        if (i < 0) { WishHintText.Text = "対象の職員を選んでください。"; return; }
        if (k < 0) { WishHintText.Text = "シフトを選んでください。"; return; }
        if (!int.TryParse(WishDayBox.Text.Trim(), out var day1) || day1 < 1 || day1 > _vm.Ui.Days)
        {
            WishHintText.Text = $"日は 1〜{_vm.Ui.Days} で入れてください。";
            return;
        }
        _vm.SetWish(i, day1 - 1, k);
        WishHintText.Text = "";
    }

    /// <summary>
    /// [2026-09-02, 配線] ApplyWishes/WishOutOfScopeCount（フェーズ9で移植・テスト済み）はこれまで
    /// 呼び出し口が無かった——登録した希望を勤務表へ反映する手段が、1件ずつの手作業(SetCell)しか
    /// 存在しなかった。担当外(そのスタッフのグループで担当できない)希望が混じっている場合だけ
    /// 確認ダイアログで「含めて反映/除いて反映/キャンセル」を選ばせる（Kotlin原本の「希望で上書き」
    /// 相当・ConfirmAsync とは別の3択のため専用ダイアログ）。
    /// </summary>
    private async void OnApplyWishesClick(object sender, RoutedEventArgs e)
    {
        if (_syncingFromModel) return;
        var oos = _vm.WishOutOfScopeCount();
        if (oos > 0)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "希望を勤務表へ反映しますか？",
                Content = new TextBlock
                {
                    Text = $"担当外（所属グループで担当できない）希望が{oos}件あります。含めて反映しますか？",
                    TextWrapping = TextWrapping.Wrap,
                },
                PrimaryButtonText = "担当外も含めて反映",
                SecondaryButtonText = "担当外を除いて反映",
                CloseButtonText = "キャンセル",
                DefaultButton = ContentDialogButton.Secondary,
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary) _vm.ApplyWishes(true);
            else if (result == ContentDialogResult.Secondary) _vm.ApplyWishes(false);
            return;
        }
        _vm.ApplyWishes(false);
    }

    private async void OnClearAllWishesClick(object sender, RoutedEventArgs e)
    {
        if (_syncingFromModel) return;
        var ok = await ConfirmAsync("希望シフトをすべて削除しますか？",
            "登録済みの希望シフトがすべて削除されます（勤務表自体は変わりません。「元に戻す」で戻せます）。");
        if (!ok) return;
        _vm.ClearAllWishes();
    }

    // ===== 日別の必要人数（例外, 月次条件） =====

    /// <summary>[phase9 #13] 必要人数カレンダー（Kotlin原本 <c>NeedMonthGrid</c>）。<see cref="RenderWishCalendar"/> と同じ作り。
    /// 各日は「必要人数の範囲」（—＝未設定／n／a–b）を出し、個別設定の日は太字＋●。複数日選択→下の適用パネル。</summary>
    private void RenderNeedCalendar(UiState ui, bool editable)
    {
        SyncItems(NeedCalShiftCombo, ui.ShiftSymbols, ref _needCalShiftItems);
        var k = NeedCalShiftCombo.SelectedIndex;
        if (k != _needCalendarShiftIndex)
        {
            _needCalendarShiftIndex = k;
            _needSelectedDays.Clear();
        }
        NeedCalendarHost.Children.Clear();
        NeedCalendarHost.RowDefinitions.Clear();
        NeedCalendarHost.ColumnDefinitions.Clear();
        if (!ui.Loaded || ui.Days <= 0)
        {
            NeedApplyPanel.Visibility = Visibility.Collapsed;
            return;
        }
        var dow0 = 0;
        if (DateOnly.TryParseExact(ui.StartDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var start))
        {
            dow0 = (int)start.DayOfWeek;
        }
        var days = ui.Days;
        var rowCount = (dow0 + days + 6) / 7;
        for (var c = 0; c < 7; c++) NeedCalendarHost.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var r = 0; r <= rowCount; r++) NeedCalendarHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        string[] weekdayLabels = { "日", "月", "火", "水", "木", "金", "土" };
        for (var c = 0; c < 7; c++)
        {
            var head = new TextBlock
            {
                Text = weekdayLabels[c], FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(c == 0 ? Colors.Red : c == 6 ? Colors.RoyalBlue : Colors.Black),
                HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(2),
            };
            Grid.SetRow(head, 0); Grid.SetColumn(head, c);
            NeedCalendarHost.Children.Add(head);
        }
        var individual = new HashSet<int>();
        if (k >= 0) foreach (var v in _vm.NeedDayOverrides()) if (v.K == k) individual.Add(v.J);
        for (var d = 1; d <= days; d++)
        {
            var col = (dow0 + d - 1) % 7;
            var row = 1 + (dow0 + d - 1) / 7;
            var selected = _needSelectedDays.Contains(d);
            var range = k >= 0 ? _vm.NeedCellLimits(k, d - 1) : null;
            var isIndividual = range is not null && individual.Contains(d - 1);
            var rangeLabel = range is null ? "—" : range.Value.Lo == range.Value.Hi ? range.Value.Lo.ToString() : $"{range.Value.Lo}–{range.Value.Hi}";
            var content = new StackPanel { Spacing = 0, HorizontalAlignment = HorizontalAlignment.Center };
            content.Children.Add(new TextBlock
            {
                Text = selected ? $"{d} ✓" : isIndividual ? $"{d} ●" : d.ToString(), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center,
                FontWeight = selected ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
            });
            content.Children.Add(new TextBlock
            {
                Text = rangeLabel, FontSize = 10, Opacity = range is null ? 0.5 : 0.9, HorizontalAlignment = HorizontalAlignment.Center,
                FontWeight = isIndividual ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
            });
            var cellButton = new Button
            {
                Content = content, Padding = new Thickness((double)Application.Current.Resources["MagiSpacingXS"]), MinWidth = 44,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Background = new SolidColorBrush(selected ? Colors.DodgerBlue : Colors.Transparent),
                BorderThickness = new Thickness(selected ? 2 : 1), BorderBrush = new SolidColorBrush(selected ? Colors.DodgerBlue : Colors.LightGray),
                IsEnabled = editable && k >= 0,
            };
            var day = d;
            cellButton.Click += (_, _) =>
            {
                if (_syncingFromModel) return;
                if (!_needSelectedDays.Add(day)) _needSelectedDays.Remove(day);
                _syncingFromModel = true;
                try { RenderNeedCalendar(_vm.Ui, editable); }
                finally { _syncingFromModel = false; }
            };
            Grid.SetRow(cellButton, row); Grid.SetColumn(cellButton, col);
            NeedCalendarHost.Children.Add(cellButton);
        }
        var hasSelection = k >= 0 && _needSelectedDays.Count > 0;
        NeedApplyPanel.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
        if (!hasSelection) return;
        var sorted = _needSelectedDays.OrderBy(x => x).ToList();
        var labels = sorted.Count <= 4 ? string.Join("、", sorted.Select(DayChipLabel))
            : string.Join("、", sorted.Take(3).Select(DayChipLabel)) + $"、ほか{sorted.Count - 3}日";
        NeedApplySummaryText.Text = $"{sorted.Count}日選択中: {labels}";
        UpdateNeedApplyButtons(editable);
    }

    /// <summary>「M/D(曜)」（Kotlin原本 <c>dayChipLabel</c>）。開始日が読めなければ「N日」。</summary>
    private string DayChipLabel(int day1)
    {
        if (!DateOnly.TryParseExact(_vm.Ui.StartDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var start)) return $"{day1}日";
        var d = start.AddDays(day1 - 1);
        string[] wk = { "日", "月", "火", "水", "木", "金", "土" };
        return $"{d.Month}/{d.Day}({wk[(int)d.DayOfWeek]})";
    }

    private void UpdateNeedApplyButtons(bool editable)
    {
        var p1 = NeedApplyP1Box.Text.Trim();
        var p2 = NeedApplyP2Box.Text.Trim();
        var invalid = V6SanityPort.RangeOrderConflict(p1, p2) is not null;
        NeedApplyHintText.Text = "上限人数は最低人数以上にしてください。";
        NeedApplyHintText.Visibility = invalid ? Visibility.Visible : Visibility.Collapsed;
        ApplyNeedForDaysButton.Content = $"{_needSelectedDays.Count}日に適用";
        ApplyNeedForDaysButton.IsEnabled = editable && !invalid && (p1.Length > 0 || p2.Length > 0);
        ClearNeedForDaysButton.IsEnabled = editable;
    }

    private void OnNeedCalShiftSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingFromModel) return;
        Render();
    }

    private void OnNeedApplyTextChanged(object sender, TextChangedEventArgs e)
    {
        if (NeedApplyPanel.Visibility == Visibility.Visible) UpdateNeedApplyButtons(_vm.Ui.Loaded && !_vm.Ui.Running);
    }

    private void OnApplyNeedForDaysClick(object sender, RoutedEventArgs e)
    {
        var k = NeedCalShiftCombo.SelectedIndex;
        if (k < 0 || _needSelectedDays.Count == 0) return;
        _vm.SetNeedDaysForDays(k, _needSelectedDays.Select(d => d - 1).OrderBy(x => x).ToList(), NeedApplyP1Box.Text.Trim(), NeedApplyP2Box.Text.Trim());
        _needSelectedDays.Clear();
        Render();
    }

    private void OnClearNeedForDaysClick(object sender, RoutedEventArgs e)
    {
        var k = NeedCalShiftCombo.SelectedIndex;
        if (k < 0 || _needSelectedDays.Count == 0) return;
        _vm.ClearNeedDaysForDays(k, _needSelectedDays.Select(d => d - 1).OrderBy(x => x).ToList());
        _needSelectedDays.Clear();
        Render();
    }

    private void OnCancelNeedSelectionClick(object sender, RoutedEventArgs e)
    {
        _needSelectedDays.Clear();
        Render();
    }

    private void RenderNeedDay(UiState ui, bool editable)
    {
        RenderNeedCalendar(ui, editable);
        SyncItems(NeedDayShiftCombo, ui.ShiftSymbols, ref _needDayShiftItems);
        SetNeedDayButton.IsEnabled = editable;

        NeedDayListHost.Children.Clear();
        var rows = _vm.NeedDayOverrides();
        foreach (var v in rows.Take(MaxOverrideRows))
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new TextBlock
            {
                Text = $"{v.Kigou} {v.J + 1}日 → 最低{DashIfBlank(v.P1)}/上限{DashIfBlank(v.P2)}",
                FontSize = 13, VerticalAlignment = VerticalAlignment.Center,
            });
            var remove = new Button { Content = "削除", FontSize = 12, IsEnabled = editable };
            var k = v.K; var j = v.J;
            remove.Click += (_, _) => _vm.RemoveNeedDay(k, j);
            row.Children.Add(remove);
            NeedDayListHost.Children.Add(row);
        }
        if (rows.Count > MaxOverrideRows)
        {
            NeedDayListHost.Children.Add(new TextBlock { Text = $"ほか {rows.Count - MaxOverrideRows}件", FontSize = 13, Opacity = 0.8 });
        }
    }

    private static string DashIfBlank(string s) => string.IsNullOrWhiteSpace(s) ? "-" : s;

    private void OnSetNeedDayClick(object sender, RoutedEventArgs e)
    {
        if (_syncingFromModel) return;
        var k = NeedDayShiftCombo.SelectedIndex;
        if (k < 0) { NeedDayHintText.Text = "シフトを選んでください。"; return; }
        if (!int.TryParse(NeedDayDayBox.Text.Trim(), out var day1) || day1 < 1 || day1 > _vm.Ui.Days)
        {
            NeedDayHintText.Text = $"日は 1〜{_vm.Ui.Days} で入れてください。";
            return;
        }
        _vm.SetNeedDay(k, day1 - 1, NeedDayP1Box.Text.Trim(), NeedDayP2Box.Text.Trim());
        NeedDayHintText.Text = "";
    }

    // ===== 確認ダイアログ（削除の共通入口） =====

    /// <summary>削除前の確認。Kotlin原本は削除前に確認ダイアログ（影響件数つき）を出す——
    /// この移植ではこれまで押す前の警告文＋Undoで代用していたが、ここで本来の確認ダイアログへ揃える。</summary>
    private async Task<bool> ConfirmAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "削除",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    // ===== 職員管理 =====

    private void RenderStaff(UiState ui, bool editable)
    {
        var names = ui.StaffNames;
        var symbols = ui.StaffGroupSymbols;

        StaffSummaryText.Text = ui.Loaded ? $"職員 {names.Count}名" : "（データ読込中）";

        // 一覧（読取専用）。ScheduleView と同じくコードビハインドで子要素を積む。
        StaffListHost.Children.Clear();
        for (var i = 0; i < names.Count; i++)
        {
            var sym = i < symbols.Count ? symbols[i] : "";
            StaffListHost.Children.Add(new TextBlock
            {
                Text = sym.Length > 0 ? $"{i + 1}. {names[i]}（{sym}）" : $"{i + 1}. {names[i]}",
                FontSize = 13,
            });
        }

        // 職員セレクタ。中身が変わったときだけ作り直して、選択中の行を保つ。
        var staffItems = names
            .Select((n, i) => (i < symbols.Count && symbols[i].Length > 0) ? $"{n}（{symbols[i]}）" : n)
            .ToList();
        if (!_staffItems.SequenceEqual(staffItems))
        {
            _staffItems = staffItems;
            var keep = StaffCombo.SelectedIndex;
            StaffCombo.ItemsSource = staffItems;
            StaffCombo.SelectedIndex = keep >= 0 && keep < staffItems.Count
                ? keep
                : (staffItems.Count > 0 ? 0 : -1);
            _syncedStaffIndex = -1; // 並びが変わった＝入力欄を取り込み直す
        }

        var groupItems = _vm.GroupLabels();
        if (!_groupItems.SequenceEqual(groupItems))
        {
            _groupItems = groupItems;
            var keep = GroupCombo.SelectedIndex;
            GroupCombo.ItemsSource = groupItems.ToList();
            GroupCombo.SelectedIndex = keep >= 0 && keep < groupItems.Count
                ? keep
                : (groupItems.Count > 0 ? 0 : -1);
        }

        // スキル区分選択肢。先頭は「(なし)」= SkillIdx -1（年間マスターのスキル区分CRUDと共有）。
        SyncItems(StaffSkillCombo, SkillComboItems(), ref _staffSkillItems);

        SyncStaffFields();

        var hasStaff = StaffCombo.SelectedIndex >= 0;
        var hasGroup = GroupCombo.SelectedIndex >= 0;
        AddStaffButton.IsEnabled = editable && hasGroup;
        EditStaffButton.IsEnabled = editable && hasStaff && hasGroup;
        RemoveStaffButton.IsEnabled = editable && hasStaff;
        StaffHintText.Text = editable
            ? "削除すると、その人の勤務・希望・個人の回数も消えます（「元に戻す」で戻せます）。"
            : (ui.Loaded ? "計算の実行中は職員を変更できません。終わってからにしてください。" : "");

        RenderStaffRange(editable);
        RenderStaffShiftMatrix(ui, editable);
    }

    /// <summary>
    /// [2026-09-02, 配線] SetStaffRange/RemoveStaffRange（個人別の回数=下限/上限、フェーズ9で移植・
    /// テスト済み）はこれまで呼び出し口が無かった——分析タブの「回数の固定で止まった手」は
    /// この値を読むが、そもそも値を設定する手段がここまで無かった（RelaxStaffRangePinの±1調整は
    /// 既存値の微調整のみで、新規設定はできない）。対象職員は上の「対象の職員」(StaffCombo)を共有し、
    /// 一覧は StaffCountRules（個人別レンジ+適切回数(apt)の実効目標を統合したビュー）から
    /// HasRange=true（個人別上下限が設定されている）行だけを列挙する。
    /// </summary>
    private void RenderStaffRange(bool editable)
    {
        SyncItems(StaffRangeShiftCombo, _vm.Ui.ShiftSymbols, ref _staffRangeShiftItems);
        SetStaffRangeButton.IsEnabled = editable && StaffCombo.SelectedIndex >= 0;

        StaffRangeListHost.Children.Clear();
        var rows = _vm.StaffCountRules().Where(v => v.HasRange).ToList();
        foreach (var v in rows.Take(MaxOverrideRows))
        {
            var target = v.AptEff >= 0 ? $"・目標{v.AptEff}" : "";
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new TextBlock
            {
                Text = $"{v.StaffName} {v.Kigou}: 下限{DashIfBlank(v.Lo)}〜上限{DashIfBlank(v.Hi)}{target}",
                FontSize = 13, VerticalAlignment = VerticalAlignment.Center,
            });
            var remove = new Button { Content = "削除", FontSize = 12, IsEnabled = editable };
            var i = v.I;
            var k = v.K;
            remove.Click += (_, _) => _vm.RemoveStaffRange(i, k);
            row.Children.Add(remove);
            StaffRangeListHost.Children.Add(row);
        }
        if (rows.Count > MaxOverrideRows)
        {
            StaffRangeListHost.Children.Add(new TextBlock { Text = $"ほか {rows.Count - MaxOverrideRows}件", FontSize = 13, Opacity = 0.8 });
        }
    }

    private void OnSetStaffRangeClick(object sender, RoutedEventArgs e)
    {
        if (_syncingFromModel) return;
        var i = StaffCombo.SelectedIndex;
        var k = StaffRangeShiftCombo.SelectedIndex;
        if (i < 0) { StaffRangeHintText.Text = "対象の職員を選んでください（上の「対象の職員」）。"; return; }
        if (k < 0) { StaffRangeHintText.Text = "シフトを選んでください。"; return; }
        _vm.SetStaffRange(i, k, StaffRangeLoBox.Text.Trim(), StaffRangeHiBox.Text.Trim());
        StaffRangeHintText.Text = "";
    }

    /// <summary>
    /// [2026-09-02, 配線] GroupRangeSummary/SetGroupRange/ClearGroupRange（フェーズ9で移植・
    /// テスト済み）はこれまで呼び出し口が無かった。個人別(<see cref="RenderStaffRange"/>)は
    /// 1人ずつしか設定できないのに対し、こちらはグループ全員へ一括で下限/上限を書く
    /// （既に個人別で設定済みの職員はスキップ・保持）＋下限=上限のときは同じシフトの適切回数(apt)も
    /// 同時に設定する——Kotlin原本コメントの言う「Excelのws1 C→ws5展開を1操作で再現」。
    /// </summary>
    private void RenderGroupRange(bool editable)
    {
        SyncItems(GroupRangeGroupCombo, _vm.GroupLabels(), ref _groupRangeGroupItems);
        SyncItems(GroupRangeShiftCombo, _vm.Ui.ShiftSymbols, ref _groupRangeShiftItems);
        SetGroupRangeButton.IsEnabled = editable && GroupRangeGroupCombo.SelectedIndex >= 0;

        GroupRangeListHost.Children.Clear();
        var rows = _vm.GroupRangeSummary();
        foreach (var v in rows.Take(MaxOverrideRows))
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new TextBlock
            {
                Text = $"{v.GroupName} {v.Kigou}: 下限{DashIfBlank(v.Lo)}〜上限{DashIfBlank(v.Hi)}（{v.Shared}/{v.Members}名が共有）",
                FontSize = 13, VerticalAlignment = VerticalAlignment.Center,
            });
            var clear = new Button { Content = "解除", FontSize = 12, IsEnabled = editable };
            var g = v.G; var k = v.K; var lo = v.Lo; var hi = v.Hi;
            clear.Click += (_, _) => _vm.ClearGroupRange(g, k, lo, hi);
            row.Children.Add(clear);
            GroupRangeListHost.Children.Add(row);
        }
        if (rows.Count > MaxOverrideRows)
        {
            GroupRangeListHost.Children.Add(new TextBlock { Text = $"ほか {rows.Count - MaxOverrideRows}件", FontSize = 13, Opacity = 0.8 });
        }
    }

    private void OnSetGroupRangeClick(object sender, RoutedEventArgs e)
    {
        if (_syncingFromModel) return;
        var g = GroupRangeGroupCombo.SelectedIndex;
        var k = GroupRangeShiftCombo.SelectedIndex;
        if (g < 0) { GroupRangeHintText.Text = "対象のグループを選んでください。"; return; }
        if (k < 0) { GroupRangeHintText.Text = "シフトを選んでください。"; return; }
        if (GroupRangeLoBox.Text.Trim().Length == 0 && GroupRangeHiBox.Text.Trim().Length == 0)
        {
            // [3.506.0 同期] 両方空欄＝グループ全員ぶんの個人上下限を解除（解除対象が無ければ案内のみ）。
            var n = _vm.GroupRangeMemberCount(g, k);
            if (n == 0) { GroupRangeHintText.Text = "下限・上限のどちらかは入れてください（解除する個人上下限もありません）。"; return; }
            _vm.ClearGroupRangeAll(g, k);
            GroupRangeHintText.Text = "";
            return;
        }
        _vm.SetGroupRange(g, k, GroupRangeLoBox.Text.Trim(), GroupRangeHiBox.Text.Trim());
        GroupRangeHintText.Text = "";
    }

    /// <summary>
    /// [2026-09-02, 配線] AddReviewMemo/RemoveReviewMemo（フェーズ9で移植・テスト済み、セッション内のみ・
    /// state非保存の軽量メモ）はこれまで呼び出し口が無かった。追加口は2つ:
    /// ①ここの手動追加欄 ②勤務表タブのセル編集フライアウト（違反セルのみ「見直し候補にする」項目、
    /// <see cref="ScheduleView.ShowCellEditor"/> 参照）。年間マスターに置くのは Kotlin原本と同じ理由
    /// （「見直し候補」＝土台の設定を見直すべき箇所という位置づけ）。
    /// </summary>
    private bool _constraintHelpOpen;

    /// <summary>
    /// [phase9 #17] 制約10族の「ⓘ 詳しい説明」（Kotlin原本 <c>ConstraintHelpExpander</c>、3.409.14）。既定で閉じる。
    /// 本文は <see cref="ConstraintHelp"/>（族キーとの過不足はテストが固定）。
    /// </summary>
    private void RenderConstraintHelp(IReadOnlyList<MagiViewModel.ConstraintFamilyView> families)
    {
        ConstraintHelpToggle.Content = _constraintHelpOpen ? "ⓘ 詳しい説明を閉じる" : "ⓘ 詳しい説明（それぞれの条件の意味）";
        ConstraintHelpHost.Visibility = _constraintHelpOpen ? Visibility.Visible : Visibility.Collapsed;
        ConstraintHelpHost.Children.Clear();
        if (!_constraintHelpOpen) return;
        foreach (var f in families)
        {
            if (!ConstraintHelp.Bodies.TryGetValue(f.Key, out var body)) continue;
            var block = new StackPanel { Spacing = 2 };
            block.Children.Add(new TextBlock { Text = f.Title, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            block.Children.Add(new TextBlock { Text = body, FontSize = 12, Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
            ConstraintHelpHost.Children.Add(block);
        }
        ConstraintHelpHost.Children.Add(new TextBlock { Text = ConstraintHelp.Footer, FontSize = 12, Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
    }

    private void OnConstraintHelpToggleClick(object sender, RoutedEventArgs e)
    {
        _constraintHelpOpen = !_constraintHelpOpen;
        Render();
    }

    /// <summary>
    /// [phase9 #16] 「この体制で回るか」（Kotlin原本 <c>StaffingRealityCard</c>）。年間マスターの先頭、見直し候補メモの直後。
    /// 「15人いるから大丈夫」ではなくシフトごとの担当可能人数で見る。数値は VM（<see cref="MagiViewModel.StaffingReality"/>）が
    /// チェッカーと同じ実効需要から出す。read-only。
    /// </summary>
    private void RenderStaffingReality(UiState ui)
    {
        var rows = ui.Loaded ? _vm.StaffingReality() : System.Array.Empty<MagiViewModel.StaffingRealityRow>();
        StaffingRealityPanel.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        StaffingRealityHost.Children.Clear();
        foreach (var r in rows)
        {
            var slack = r.Q - r.MaxNeed;
            var tenths = r.Q > 0 ? (r.D * 10 + r.Q / 2) / r.Q : 0;
            var (mark, brushKey) = slack < 0 ? ("⚠", "MagiErrorBrush")
                : slack == 0 ? ("！", "MagiOnWarnContainerBrush")
                : ("✓", "MagiTertiaryBrush");
            var detail = slack < 0 ? $"日最大{r.MaxNeed}人 → 担当不足"
                : slack == 0 ? $"日最大{r.MaxNeed}人 → 欠勤余裕なし"
                : $"欠勤余裕{slack}人";
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new TextBlock
            {
                Text = mark, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Width = 16,
                Foreground = (Brush)Application.Current.Resources[brushKey],
            });
            row.Children.Add(new TextBlock { Text = r.Kigou, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Width = 44 });
            row.Children.Add(new TextBlock
            {
                Text = $"担当{r.Q}人・月{r.D}人日（1人あたり{tenths / 10}.{tenths % 10}回）・{detail}",
                FontSize = 12, Opacity = 0.8, TextWrapping = TextWrapping.Wrap,
            });
            StaffingRealityHost.Children.Add(row);
        }
    }

    private void RenderReviewMemos(UiState ui)
    {
        ReviewMemoListHost.Children.Clear();
        var memos = ui.ReviewMemos;
        for (var idx = 0; idx < memos.Count; idx++)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new TextBlock
            {
                Text = memos[idx], FontSize = 13, TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center, MaxWidth = 380,
            });
            var remove = new Button { Content = "削除", FontSize = 12 };
            var i = idx;
            remove.Click += (_, _) => _vm.RemoveReviewMemo(i);
            row.Children.Add(remove);
            ReviewMemoListHost.Children.Add(row);
        }
    }

    private void OnAddReviewMemoClick(object sender, RoutedEventArgs e)
    {
        if (_syncingFromModel) return;
        var text = ReviewMemoBox.Text.Trim();
        if (text.Length == 0) return;
        _vm.AddReviewMemo(text);
        ReviewMemoBox.Text = "";
    }

    /// <summary>選択中の職員の名前・所属を入力欄へ取り込む（選択が変わったときだけ）。</summary>
    private void SyncStaffFields()
    {
        var i = StaffCombo.SelectedIndex;
        if (i < 0 || i == _syncedStaffIndex) return;
        _syncedStaffIndex = i;
        var ui = _vm.Ui;
        StaffNameBox.Text = i < ui.StaffNames.Count ? ui.StaffNames[i] : "";
        // 所属グループの index は UiState に無い（記号しか持たない）ので ws1 のスナップショットから引く。
        var ws1 = _vm.Ws1();
        if (ws1 is not null && i < ws1.Staff.Count)
        {
            var g = ws1.Staff[i].GroupIdx;
            if (g >= 0 && g < _groupItems.Count) GroupCombo.SelectedIndex = g;
            // スキル区分。-1(なし)は index0、区分[s]は index(s+1)（SkillComboItems の並びと対応）。
            var skillIdx = ws1.Staff[i].SkillIdx;
            var comboIdx = skillIdx + 1;
            if (comboIdx >= 0 && comboIdx < _staffSkillItems.Count) StaffSkillCombo.SelectedIndex = comboIdx;
        }
    }

    /// <summary>「(なし)」＋スキル区分一覧。index=0が「(なし)」(SkillIdx=-1)、index=g+1がSkillIdx=g。
    /// 職員管理(スキル割当)・年間マスター(スキル区分CRUD)双方の元データを共有する唯一の並び。</summary>
    private IReadOnlyList<string> SkillComboItems() =>
        new[] { "(なし)" }.Concat(_vm.SkillGroups().Select(g => $"{g.Name}（{g.Kigou}）")).ToList();

    private void OnStaffSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingFromModel) return;
        _syncingFromModel = true;
        try
        {
            SyncStaffFields();
        }
        finally
        {
            _syncingFromModel = false;
        }
    }

    private void OnAddStaffClick(object sender, RoutedEventArgs e)
    {
        if (_syncingFromModel) return;
        var name = StaffNameBox.Text.Trim();
        var g = GroupCombo.SelectedIndex;
        if (name.Length == 0) { StaffHintText.Text = "名前を入れてください。"; return; }
        if (g < 0) { StaffHintText.Text = "所属グループを選んでください。"; return; }
        // [スキル区分の同時設定] Ws1AddStaff(name,groupIdx) は SkillIdx を受け取らない
        // （既定0=未所属で追加される）ため、追加直後にその新しい職員(=追加前の人数=末尾index)へ
        // SetStaffSkill で選択中のスキル区分を書く。追加自体が失敗する経路（name/g空欄）は
        // 既に上のガードで弾いているため、ここに来た時点で追加は必ず成功する。
        var newIndex = _vm.Ui.StaffNames.Count;
        _vm.Ws1AddStaff(name, g);
        var skillCombo = StaffSkillCombo.SelectedIndex;
        if (skillCombo >= 0) _vm.SetStaffSkill(newIndex, skillCombo - 1);
        // 追加後は末尾に増えるだけで選択 index は変わらない＝入力欄の取り込みを促す。
        _syncedStaffIndex = -1;
    }

    private void OnEditStaffClick(object sender, RoutedEventArgs e)
    {
        if (_syncingFromModel) return;
        var i = StaffCombo.SelectedIndex;
        var name = StaffNameBox.Text.Trim();
        var g = GroupCombo.SelectedIndex;
        if (i < 0) { StaffHintText.Text = "対象の職員を選んでください。"; return; }
        if (name.Length == 0) { StaffHintText.Text = "名前を入れてください。"; return; }
        if (g < 0) { StaffHintText.Text = "所属グループを選んでください。"; return; }
        // Ws1EditStaff は名前と所属をまとめて書く（Kotlin原本と同じ単位）＝ボタンも「改名・所属変更」。
        // SetStaffSkill は独立API（Ws1EditStaffがSkillIdxを受け取らない）なので、続けて別途書く。
        _vm.Ws1EditStaff(i, name, g);
        var skillCombo = StaffSkillCombo.SelectedIndex;
        if (skillCombo >= 0) _vm.SetStaffSkill(i, skillCombo - 1);
        _syncedStaffIndex = -1;
    }

    private async void OnRemoveStaffClick(object sender, RoutedEventArgs e)
    {
        if (_syncingFromModel) return;
        var i = StaffCombo.SelectedIndex;
        if (i < 0) { StaffHintText.Text = "対象の職員を選んでください。"; return; }
        var name = i < _vm.Ui.StaffNames.Count ? _vm.Ui.StaffNames[i] : "";
        var ok = await ConfirmAsync("職員を削除しますか？",
            $"「{name}」を削除します。勤務・希望・個人の回数も削除されます（「元に戻す」で戻せます）。");
        if (!ok) return;
        _vm.Ws1RemoveStaff(i);
        _syncedStaffIndex = -1;
    }

    // ===== 年間マスター =====

    private void RenderMaster(UiState ui, bool editable)
    {
        RenderReviewMemos(ui);
        RenderStaffingReality(ui);

        // [2026-09-02, 配線] Ws1ResizeDays（フェーズ9で移植・テスト済み）はこれまで呼び出し口が無かった。
        // 期間の日数は「対象月を選ぶ」(SetMonth/ShiftMonth/SetNextMonth＝常に暦通りの月)でしか変えられず、
        // 15日等の任意の日数（暦月と一致しない期間）を直接指定する手段が無かった。Kotlin原本(Ws1Card)の
        // PERIOD節と同じく日数入力＋「変更」ボタンを ARSENAL(シフト)節の前に置く。入力中は上書きしない
        // （群×シフトの適切回数欄と同じ理由・SyncMasterShiftFields系のフォーカスガードと同型）。
        if (MasterDaysBox.FocusState == FocusState.Unfocused)
        {
            MasterDaysBox.Text = ui.Loaded && ui.Days > 0 ? ui.Days.ToString() : "";
        }
        EditMasterDaysButton.IsEnabled = editable;
        MasterDaysHintText.Text = editable
            ? ""
            : (ui.Loaded ? "計算の実行中は期間を変更できません。終わってからにしてください。" : "");

        ShiftListText.Text = ui.ShiftSymbols.Count > 0
            ? string.Join(" ・ ", ui.ShiftSymbols)
            : "（未設定）";

        Use2Toggle.IsOn = ui.Use2;
        Use2Toggle.IsEnabled = editable;
        ResetAptButton.IsEnabled = editable;

        var shifts = _vm.Ws1()?.Shifts;
        var shiftItems = shifts?.Select(s => s.Kigou.Length > 0 && s.Kigou != s.Name ? $"{s.Name}·{s.Kigou}" : s.Name).ToList()
            ?? new List<string>();
        SyncItems(MasterShiftCombo, shiftItems, ref _masterShiftItems);
        SyncMasterShiftFields();
        var hasShift = MasterShiftCombo.SelectedIndex >= 0;
        AddMasterShiftButton.IsEnabled = editable;
        EditMasterShiftButton.IsEnabled = editable && hasShift;
        RemoveMasterShiftButton.IsEnabled = editable && hasShift;
        if (editable && hasShift)
        {
            var refs = _vm.Ws1ShiftRefCount(MasterShiftCombo.SelectedIndex);
            MasterShiftHintText.Text = refs > 0
                ? $"削除すると、該当マスは「休」（無ければ先頭シフト）へ置き換わります。このシフトを参照する制約が{refs}件あります。"
                : "削除すると、該当マスは「休」（無ければ先頭シフト）へ置き換わります。";
        }
        else
        {
            MasterShiftHintText.Text = editable ? "" : (ui.Loaded ? "計算の実行中はシフトを変更できません。終わってからにしてください。" : "");
        }

        var groups = _vm.GroupLabels();
        GroupListText.Text = groups.Count > 0 ? string.Join(" ・ ", groups) : "（未設定）";

        SyncItems(MasterGroupCombo, groups, ref _masterGroupItems);
        SyncMasterGroupFields();
        var hasGroup = MasterGroupCombo.SelectedIndex >= 0;
        AddMasterGroupButton.IsEnabled = editable;
        EditMasterGroupButton.IsEnabled = editable && hasGroup;
        RemoveMasterGroupButton.IsEnabled = editable && hasGroup && _vm.Ws1CanRemoveGroup(MasterGroupCombo.SelectedIndex);
        // [2026-09-02, 配線] GroupKigouList（フェーズ9で移植・テスト済み）はこれまで呼び出し口が
        // 無かった。追加/改名で記号が衝突すると SymbolTaken が事後にエラーで断るため、既存の記号を
        // 先に見せて衝突を未然に避けられるようにする。
        MasterGroupHintText.Text = editable
            ? "削除すると、所属者は先頭グループへ移動します（担当できるシフトが変わります）。" +
              $" 使用中の記号: {string.Join("・", _vm.GroupKigouList())}"
            : (ui.Loaded ? "計算の実行中はグループを変更できません。終わってからにしてください。" : "");

        RenderGroupRange(editable);

        // ルールの件数は族ごとに出す（GetSetupCounts().Constraints はスキル群の2族を含まない合計のため、
        // 表示は ConstraintFamilies/SkillConstraintFamilies の実 Rows 数を正とする）。
        ConstraintListHost.Children.Clear();
        var families = _vm.ConstraintFamilies().Concat(_vm.SkillConstraintFamilies()).ToList();
        RenderConstraintHelp(families);
        var total = 0;
        foreach (var f in families)
        {
            total += f.Rows.Count;
            ConstraintListHost.Children.Add(new TextBlock
            {
                Text = $"{f.Title}: {f.Rows.Count}件",
                FontSize = 13,
                Opacity = f.Rows.Count == 0 ? 0.5 : 1.0,
            });
        }
        ConstraintListHost.Children.Add(new TextBlock
        {
            Text = $"合計 {total}件",
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });

        SyncConstraintFields();
        var hasConstraintFamily = ConstraintFamilyCombo.SelectedIndex >= 0;
        var hasConstraintRow = ConstraintRowCombo.SelectedIndex >= 0;
        AddConstraintButton.IsEnabled = editable && hasConstraintFamily;
        UpdateConstraintButton.IsEnabled = editable && hasConstraintFamily && hasConstraintRow;
        RemoveConstraintButton.IsEnabled = editable && hasConstraintFamily && hasConstraintRow;
        if (!editable)
            ConstraintHintText.Text = ui.Loaded ? "計算の実行中はルールを変更できません。終わってからにしてください。" : "";
    }

    /// <summary>
    /// [制約(ルール)10族の追加/変更/削除] 種類ごとの入力欄構成。<c>FieldLabels</c> の並びは
    /// <see cref="MagiViewModel.ConstraintRowValues"/>/<see cref="MagiViewModel.UpdateConstraint"/> の
    /// 値順と一致させる（cons3系は最大5・可変長=空欄で打ち切り、それ以外は固定長）。
    /// <c>Add*</c> 系メソッドへの引数の並びだけ cons42/cons42s で異なる
    /// （<see cref="TryAddConstraint"/> 参照・<c>MagiViewModel.Editing.cs</c> の
    /// <see cref="MagiViewModel.ConstraintRowValues"/> のKDocに理由あり）。
    /// <paramref name="ShiftFieldIndexes"/>=この族で「シフト記号」を入れる欄の index（0始まり）。
    /// 該当欄は<see cref="SyncConstraintFields"/>が既存シフトから選べる編集可能ComboBoxへ切り替える
    /// （Kotlin原本 ConstraintEditor.kt の Picker("シフト", shifts, sk) 相当・
    /// <see cref="MagiViewModel.ShiftKigouList"/> 配線）。未指定の欄は従来どおりTextBoxのまま。
    /// </summary>
    private sealed record ConstraintFamilyMeta(string Key, string[] FieldLabels, int[]? ShiftFieldIndexes = null)
    {
        public bool IsShiftField(int i) => ShiftFieldIndexes is not null && System.Array.IndexOf(ShiftFieldIndexes, i) >= 0;
    }

    private static readonly ConstraintFamilyMeta[] ConstraintFamilyMetas =
    {
        new("cons1", new[] { "窓の日数", "シフト記号", "最低回数" }, new[] { 1 }),
        new("cons2", new[] { "シフト記号", "合計回数" }, new[] { 0 }),
        new("cons3", new[] { "1日目", "2日目", "3日目", "4日目", "5日目" }),
        new("cons3n", new[] { "1日目", "2日目", "3日目", "4日目", "5日目" }),
        new("cons3m", new[] { "1日目", "2日目", "3日目", "4日目", "5日目" }),
        new("cons3mn", new[] { "1日目", "2日目", "3日目", "4日目", "5日目" }),
        new("cons41", new[] { "群記号", "シフト記号", "下限", "上限" }, new[] { 1 }),
        new("cons42", new[] { "群1記号", "シフト1記号", "群2記号", "シフト2記号" }, new[] { 1, 3 }),
        new("cons41s", new[] { "スキル群記号", "シフト記号", "下限", "上限" }, new[] { 1 }),
        new("cons42s", new[] { "スキル群1記号", "シフト1記号", "スキル群2記号", "シフト2記号" }, new[] { 1, 3 }),
    };

    private string? ConstraintFamilyKey() => (ConstraintFamilyCombo.SelectedItem as ComboBoxItem)?.Tag as string;

    private IReadOnlyList<string> RowLabelsFor(string key)
    {
        var f = _vm.ConstraintFamilies().Concat(_vm.SkillConstraintFamilies()).FirstOrDefault(x => x.Key == key);
        if (f is null) return System.Array.Empty<string>();
        return f.Rows.Select((r, i) => $"{i + 1}: {r}").ToList();
    }

    private TextBox[] ConstraintFieldBoxes() =>
        new[] { ConstraintField1Box, ConstraintField2Box, ConstraintField3Box, ConstraintField4Box, ConstraintField5Box };

    private ComboBox[] ConstraintFieldShiftCombos() => new[]
    {
        ConstraintField1ShiftCombo, ConstraintField2ShiftCombo, ConstraintField3ShiftCombo,
        ConstraintField4ShiftCombo, ConstraintField5ShiftCombo,
    };

    /// <summary>シフト記号欄5枠(<see cref="ConstraintFieldShiftCombos"/>)は中身が全て同じ
    /// <c>_vm.ShiftKigouList()</c> のため、<see cref="SyncItems"/>(単一キャッシュ=単一コンボ前提)は使わず
    /// ここで一括同期する（表示中でない枠も含めて更新——種類切替の直後でも即座に正しい選択肢を持つ）。</summary>
    private void SyncConstraintShiftCombos()
    {
        var items = _vm.ShiftKigouList();
        if (_constraintShiftItems.SequenceEqual(items)) return;
        _constraintShiftItems = items;
        var list = items.ToList();
        foreach (var combo in ConstraintFieldShiftCombos()) combo.ItemsSource = list;
    }

    /// <summary>種類の選択・行一覧・入力欄の表示/ラベルを同期する。種類が変わったときだけ行選択と
    /// 入力欄を強制的に空へ戻す（<see cref="_syncedConstraintFamilyKey"/> 参照）。シフト記号欄
    /// （<see cref="ConstraintFamilyMeta.IsShiftField"/>）はTextBoxでなくComboBoxを表示する。</summary>
    private void SyncConstraintFields()
    {
        var key = ConstraintFamilyKey();
        var meta = key is null ? null : ConstraintFamilyMetas.FirstOrDefault(m => m.Key == key);
        var familyChanged = key != _syncedConstraintFamilyKey;
        _syncedConstraintFamilyKey = key;

        SyncConstraintShiftCombos();

        var boxes = ConstraintFieldBoxes();
        var combos = ConstraintFieldShiftCombos();
        var labels = new[] { ConstraintField1Label, ConstraintField2Label, ConstraintField3Label, ConstraintField4Label, ConstraintField5Label };
        for (var i = 0; i < boxes.Length; i++)
        {
            var show = meta is not null && i < meta.FieldLabels.Length;
            var isShift = show && meta!.IsShiftField(i);
            boxes[i].Visibility = show && !isShift ? Visibility.Visible : Visibility.Collapsed;
            combos[i].Visibility = isShift ? Visibility.Visible : Visibility.Collapsed;
            labels[i].Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (show) labels[i].Text = meta!.FieldLabels[i];
            if (familyChanged)
            {
                boxes[i].Text = "";
                combos[i].Text = "";
            }
        }

        var rows = key is null ? System.Array.Empty<string>() : RowLabelsFor(key);
        if (familyChanged)
        {
            // [SyncItemsのキャッシュ差分検知に頼らない理由] 種類切替の直前直後で行ラベルがたまたま
            //   一致すると(通常起きないが)差分無しと誤判定され、前の種類の行一覧が残ってしまう。
            //   種類が変わったときは常に明示的に作り直す。
            _syncedConstraintRowIndex = -1;
            _constraintRowItems = rows;
            ConstraintRowCombo.ItemsSource = rows.ToList();
            ConstraintRowCombo.SelectedIndex = -1;
        }
        else
        {
            SyncItems(ConstraintRowCombo, rows, ref _constraintRowItems);
        }
    }

    private void OnConstraintFamilyChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingFromModel) return;
        _syncingFromModel = true;
        try
        {
            SyncConstraintFields();
            ConstraintHintText.Text = "";
        }
        finally
        {
            _syncingFromModel = false;
        }
    }

    private void OnConstraintRowSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingFromModel) return;
        var idx = ConstraintRowCombo.SelectedIndex;
        if (idx < 0 || idx == _syncedConstraintRowIndex) return;
        _syncedConstraintRowIndex = idx;
        var key = ConstraintFamilyKey();
        var meta = key is null ? null : ConstraintFamilyMetas.FirstOrDefault(m => m.Key == key);
        var values = key is null ? null : _vm.ConstraintRowValues(key, idx);
        if (values is null || meta is null) return;
        var boxes = ConstraintFieldBoxes();
        var combos = ConstraintFieldShiftCombos();
        for (var i = 0; i < boxes.Length; i++)
        {
            var v = i < values.Count ? values[i] : "";
            if (meta.IsShiftField(i)) combos[i].Text = v; else boxes[i].Text = v;
        }
    }

    /// <summary>シフト記号欄(<see cref="ConstraintFamilyMeta.IsShiftField"/>)はComboBox.Text から、
    /// それ以外はTextBox.Text から集める（<see cref="SyncConstraintFields"/>で表示している側が実データ）。</summary>
    private IReadOnlyList<string> CollectConstraintFieldValues(ConstraintFamilyMeta meta)
    {
        var boxes = ConstraintFieldBoxes();
        var combos = ConstraintFieldShiftCombos();
        var result = new List<string>();
        for (var i = 0; i < meta.FieldLabels.Length; i++)
            result.Add((meta.IsShiftField(i) ? combos[i].Text : boxes[i].Text).Trim());
        return result;
    }

    /// <summary>[追加専用のフィールド並び替え] <c>AddCons42</c>/<c>AddCons42s</c> の引数順(g1,g2,s1,s2)は、
    /// 編集の値順(<see cref="MagiViewModel.ConstraintRowValues"/>=g1,s1,g2,s2、群と対応シフトを並べて
    /// 見せるUI都合)と異なるため、ここでだけ明示的に並べ替える。それ以外の族は両者が一致するため
    /// 並べ替え不要（<see cref="ConstraintFamilyMetas"/> クラスKDoc参照）。</summary>
    private bool TryAddConstraint(string key, IReadOnlyList<string> v, out string error)
    {
        error = "";
        string G(int i) => i < v.Count ? v[i] : "";
        // [レビュー指摘] 数値欄の非数・下限>上限・存在しない記号は登録できたように見えて最適化に効かない。VM の検証を先に通す。
        var invalid = _vm.ConstraintInputError(key, v);
        if (invalid is not null) { error = invalid; return false; }
        switch (key)
        {
            case "cons1":
                if (G(0).Length == 0 || G(1).Length == 0 || G(2).Length == 0) { error = "すべての項目を入れてください。"; return false; }
                _vm.AddCons1(G(0), G(1), G(2));
                return true;
            case "cons2":
                if (G(0).Length == 0 || G(1).Length == 0) { error = "すべての項目を入れてください。"; return false; }
                _vm.AddCons2(G(0), G(1));
                return true;
            case "cons3": case "cons3n": case "cons3m": case "cons3mn":
                if (v.All(x => x.Length == 0)) { error = "少なくとも1日目を入れてください。"; return false; }
                _vm.AddCons3(key, v);
                return true;
            case "cons41":
                if (G(0).Length == 0 || G(1).Length == 0) { error = "群記号とシフト記号を入れてください。"; return false; }
                _vm.AddCons41(G(0), G(1), G(2), G(3));
                return true;
            case "cons41s":
                if (G(0).Length == 0 || G(1).Length == 0) { error = "スキル群記号とシフト記号を入れてください。"; return false; }
                _vm.AddCons41s(G(0), G(1), G(2), G(3));
                return true;
            case "cons42":
                if (G(0).Length == 0 || G(1).Length == 0 || G(2).Length == 0 || G(3).Length == 0) { error = "すべての項目を入れてください。"; return false; }
                _vm.AddCons42(G(0), G(2), G(1), G(3)); // 入力欄=[群1,シフト1,群2,シフト2] -> AddCons42(g1,g2,s1,s2)
                return true;
            case "cons42s":
                if (G(0).Length == 0 || G(1).Length == 0 || G(2).Length == 0 || G(3).Length == 0) { error = "すべての項目を入れてください。"; return false; }
                _vm.AddCons42s(G(0), G(2), G(1), G(3));
                return true;
            default:
                error = "種類を選んでください。";
                return false;
        }
    }

    private void OnAddConstraintClick(object sender, RoutedEventArgs e)
    {
        if (_syncingFromModel) return;
        var key = ConstraintFamilyKey();
        var meta = key is null ? null : ConstraintFamilyMetas.FirstOrDefault(m => m.Key == key);
        if (key is null || meta is null) { ConstraintHintText.Text = "種類を選んでください。"; return; }
        if (!TryAddConstraint(key, CollectConstraintFieldValues(meta), out var error)) { ConstraintHintText.Text = error; return; }
        ConstraintHintText.Text = "";
        _syncedConstraintRowIndex = -1;
    }

    private void OnUpdateConstraintClick(object sender, RoutedEventArgs e)
    {
        if (_syncingFromModel) return;
        var key = ConstraintFamilyKey();
        var meta = key is null ? null : ConstraintFamilyMetas.FirstOrDefault(m => m.Key == key);
        var idx = ConstraintRowCombo.SelectedIndex;
        if (key is null || meta is null) { ConstraintHintText.Text = "種類を選んでください。"; return; }
        if (idx < 0) { ConstraintHintText.Text = "変更する行を選んでください。"; return; }
        var values = CollectConstraintFieldValues(meta);
        if (values.All(x => x.Length == 0)) { ConstraintHintText.Text = "内容を入れてください。"; return; }
        if (_vm.ConstraintInputError(key, values) is { } invalid) { ConstraintHintText.Text = invalid; return; }
        _vm.UpdateConstraint(key, idx, values);
        ConstraintHintText.Text = "";
        _syncedConstraintRowIndex = -1;
    }

    private void OnRemoveConstraintClick(object sender, RoutedEventArgs e)
    {
        if (_syncingFromModel) return;
        var key = ConstraintFamilyKey();
        var idx = ConstraintRowCombo.SelectedIndex;
        if (key is null) { ConstraintHintText.Text = "種類を選んでください。"; return; }
        if (idx < 0) { ConstraintHintText.Text = "削除する行を選んでください。"; return; }
        _vm.RemoveConstraint(key, idx);
        ConstraintHintText.Text = "";
        _syncedConstraintRowIndex = -1;
    }

    /// <summary>選択中のグループの名前・記号を入力欄へ取り込む（選択が変わったときだけ、職員管理と同じ理由）。</summary>
    private void SyncMasterGroupFields()
    {
        var g = MasterGroupCombo.SelectedIndex;
        if (g < 0 || g == _syncedMasterGroupIndex) return;
        _syncedMasterGroupIndex = g;
        var groups = _vm.Ws1()?.Groups;
        if (groups is not null && g < groups.Count)
        {
            MasterGroupNameBox.Text = groups[g].Name;
            MasterGroupKigouBox.Text = groups[g].Kigou;
        }
    }

    private void OnMasterGroupSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingFromModel) return;
        _syncingFromModel = true;
        try
        {
            SyncMasterGroupFields();
        }
        finally
        {
            _syncingFromModel = false;
        }
    }

    private void OnAddMasterGroupClick(object sender, RoutedEventArgs e)
    {
        if (_syncingFromModel) return;
        var name = MasterGroupNameBox.Text.Trim();
        var kigou = MasterGroupKigouBox.Text.Trim();
        if (name.Length == 0 || kigou.Length == 0) { MasterGroupHintText.Text = "名前と記号を入れてください。"; return; }
        _vm.Ws1AddGroup(name, kigou);
        _syncedMasterGroupIndex = -1;
    }

    private void OnEditMasterGroupClick(object sender, RoutedEventArgs e)
    {
        if (_syncingFromModel) return;
        var g = MasterGroupCombo.SelectedIndex;
        var name = MasterGroupNameBox.Text.Trim();
        var kigou = MasterGroupKigouBox.Text.Trim();
        if (g < 0) { MasterGroupHintText.Text = "対象のグループを選んでください。"; return; }
        if (name.Length == 0 || kigou.Length == 0) { MasterGroupHintText.Text = "名前と記号を入れてください。"; return; }
        _vm.Ws1EditGroup(g, name, kigou);
        _syncedMasterGroupIndex = -1;
    }

    private async void OnRemoveMasterGroupClick(object sender, RoutedEventArgs e)
    {
        if (_syncingFromModel) return;
        var g = MasterGroupCombo.SelectedIndex;
        if (g < 0) { MasterGroupHintText.Text = "対象のグループを選んでください。"; return; }
        if (!_vm.Ws1CanRemoveGroup(g)) { MasterGroupHintText.Text = "グループは最低1つ必要なため削除できません。"; return; }
        var members = _vm.Ws1GroupMemberCount(g);
        var refs = _vm.Ws1GroupRefCount(g) + _vm.Ws1SkillGroupRefCount(g);
        var name = g < _masterGroupItems.Count ? _masterGroupItems[g] : "";
        var msg = $"「{name}」を削除します。" +
            (members > 0 ? $" 所属{members}名は先頭グループへ移動し、担当できるシフトが変わります。" : "") +
            (refs > 0 ? $" このグループを参照する制約が{refs}件あります。" : "");
        var ok = await ConfirmAsync("グループを削除しますか？", msg);
        if (!ok) return;
        _vm.Ws1RemoveGroup(g);
        _syncedMasterGroupIndex = -1;
    }

    /// <summary>選択中のシフトの名前・記号・最低/上限人数を入力欄へ取り込む（選択が変わったときだけ）。</summary>
    private void SyncMasterShiftFields()
    {
        var k = MasterShiftCombo.SelectedIndex;
        if (k < 0 || k == _syncedMasterShiftIndex) return;
        _syncedMasterShiftIndex = k;
        var shifts = _vm.Ws1()?.Shifts;
        if (shifts is not null && k < shifts.Count)
        {
            MasterShiftNameBox.Text = shifts[k].Name;
            MasterShiftKigouBox.Text = shifts[k].Kigou;
            MasterShiftNeed1Box.Text = shifts[k].Need1;
            MasterShiftNeed2Box.Text = shifts[k].Need2;
        }
    }

    private void OnMasterShiftSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingFromModel) return;
        _syncingFromModel = true;
        try
        {
            SyncMasterShiftFields();
        }
        finally
        {
            _syncingFromModel = false;
        }
    }

    private void OnAddMasterShiftClick(object sender, RoutedEventArgs e)
    {
        if (_syncingFromModel) return;
        var name = MasterShiftNameBox.Text.Trim();
        var kigou = MasterShiftKigouBox.Text.Trim();
        if (name.Length == 0 || kigou.Length == 0) { MasterShiftHintText.Text = "名前と記号を入れてください。"; return; }
        _vm.Ws1AddShift(name, kigou, MasterShiftNeed1Box.Text.Trim(), MasterShiftNeed2Box.Text.Trim());
        _syncedMasterShiftIndex = -1;
    }

    private void OnEditMasterShiftClick(object sender, RoutedEventArgs e)
    {
        if (_syncingFromModel) return;
        var k = MasterShiftCombo.SelectedIndex;
        var name = MasterShiftNameBox.Text.Trim();
        var kigou = MasterShiftKigouBox.Text.Trim();
        if (k < 0) { MasterShiftHintText.Text = "対象のシフトを選んでください。"; return; }
        if (name.Length == 0 || kigou.Length == 0) { MasterShiftHintText.Text = "名前と記号を入れてください。"; return; }
        _vm.Ws1EditShift(k, name, kigou, MasterShiftNeed1Box.Text.Trim(), MasterShiftNeed2Box.Text.Trim());
        _syncedMasterShiftIndex = -1;
    }

    private void OnRemoveMasterShiftClick(object sender, RoutedEventArgs e)
    {
        if (_syncingFromModel) return;
        var k = MasterShiftCombo.SelectedIndex;
        if (k < 0) { MasterShiftHintText.Text = "対象のシフトを選んでください。"; return; }
        // [Kotlin原本の挙動をそのまま保存] 削除は「休」（無ければ先頭シフト）への吸収で大量破壊的では
        //   ないため、職員/グループのような確認ダイアログは課さない（クラスKDoc参照）。
        _vm.Ws1RemoveShift(k);
        _syncedMasterShiftIndex = -1;
    }

    private void OnEditMasterDaysClick(object sender, RoutedEventArgs e)
    {
        if (_syncingFromModel) return;
        if (!int.TryParse(MasterDaysBox.Text.Trim(), out var n) || n < 1 || n > 31)
        {
            MasterDaysHintText.Text = "日数は 1〜31 で入れてください。";
            return;
        }
        _vm.Ws1ResizeDays(n);
    }

    // ===== スキル区分（年間マスター・新C41s/C42s 専用） =====

    /// <summary>
    /// [2026-09-02, 配線] SkillGroups/AddSkillGroup/EditSkillGroup/RemoveSkillGroup（フェーズ9で移植・
    /// テスト済み）が、cons41s/cons42s の追加/変更/削除UIは既にあるのに、その対象であるスキル区分
    /// 自体を作る/消す手段が無い「配線されていない箱」だった（スキル群制約を新規に使い始めることが
    /// できなかった）。グループCRUD（<see cref="RenderMaster"/> 内）と同じパターンで実装する。
    /// </summary>
    private void RenderSkillGroups(UiState ui, bool editable)
    {
        var skillGroups = _vm.SkillGroups();
        var labels = skillGroups.Select(g => $"{g.Name}（{g.Kigou}）").ToList();
        SkillGroupListText.Text = labels.Count > 0 ? string.Join(" ・ ", labels) : "（未設定）";

        SyncItems(MasterSkillGroupCombo, labels, ref _masterSkillGroupItems);
        SyncMasterSkillGroupFields();
        var hasSkillGroup = MasterSkillGroupCombo.SelectedIndex >= 0;
        AddMasterSkillGroupButton.IsEnabled = editable;
        EditMasterSkillGroupButton.IsEnabled = editable && hasSkillGroup;
        RemoveMasterSkillGroupButton.IsEnabled = editable && hasSkillGroup;
        // [2026-09-02, 配線] SkillGroupKigouList（フェーズ9で移植・テスト済み）も同じ理由で追加。
        MasterSkillGroupHintText.Text = editable
            ? "削除すると、割り当てていた職員は「(なし)」に戻ります（cons41s/cons42sの対象から外れます）。" +
              (_vm.SkillGroupKigouList().Count > 0 ? $" 使用中の記号: {string.Join("・", _vm.SkillGroupKigouList())}" : "")
            : (ui.Loaded ? "計算の実行中はスキル区分を変更できません。終わってからにしてください。" : "");
    }

    /// <summary>選択中のスキル区分の名前・記号を入力欄へ取り込む（選択が変わったときだけ）。</summary>
    private void SyncMasterSkillGroupFields()
    {
        var g = MasterSkillGroupCombo.SelectedIndex;
        if (g < 0 || g == _syncedMasterSkillGroupIndex) return;
        _syncedMasterSkillGroupIndex = g;
        var groups = _vm.SkillGroups();
        if (g < groups.Count)
        {
            MasterSkillGroupNameBox.Text = groups[g].Name;
            MasterSkillGroupKigouBox.Text = groups[g].Kigou;
        }
    }

    private void OnMasterSkillGroupSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingFromModel) return;
        _syncingFromModel = true;
        try
        {
            SyncMasterSkillGroupFields();
        }
        finally
        {
            _syncingFromModel = false;
        }
    }

    private void OnAddMasterSkillGroupClick(object sender, RoutedEventArgs e)
    {
        if (_syncingFromModel) return;
        var name = MasterSkillGroupNameBox.Text.Trim();
        var kigou = MasterSkillGroupKigouBox.Text.Trim();
        if (name.Length == 0 || kigou.Length == 0) { MasterSkillGroupHintText.Text = "名前と記号を入れてください。"; return; }
        _vm.AddSkillGroup(name, kigou);
        _syncedMasterSkillGroupIndex = -1;
    }

    private void OnEditMasterSkillGroupClick(object sender, RoutedEventArgs e)
    {
        if (_syncingFromModel) return;
        var g = MasterSkillGroupCombo.SelectedIndex;
        var name = MasterSkillGroupNameBox.Text.Trim();
        var kigou = MasterSkillGroupKigouBox.Text.Trim();
        if (g < 0) { MasterSkillGroupHintText.Text = "対象のスキル区分を選んでください。"; return; }
        if (name.Length == 0 || kigou.Length == 0) { MasterSkillGroupHintText.Text = "名前と記号を入れてください。"; return; }
        _vm.EditSkillGroup(g, name, kigou);
        _syncedMasterSkillGroupIndex = -1;
    }

    private async void OnRemoveMasterSkillGroupClick(object sender, RoutedEventArgs e)
    {
        if (_syncingFromModel) return;
        var g = MasterSkillGroupCombo.SelectedIndex;
        if (g < 0) { MasterSkillGroupHintText.Text = "対象のスキル区分を選んでください。"; return; }
        var ws1 = _vm.Ws1();
        var members = ws1?.Staff.Count(s => s.SkillIdx == g) ?? 0;
        var refs = _vm.Ws1SkillGroupRefCount(g);
        var name = g < _masterSkillGroupItems.Count ? _masterSkillGroupItems[g] : "";
        var msg = $"「{name}」を削除します。" +
            (members > 0 ? $" 割り当てていた{members}名は「(なし)」に戻ります。" : "") +
            (refs > 0 ? $" このスキル区分を参照する制約が{refs}件あります。" : "");
        var ok = await ConfirmAsync("スキル区分を削除しますか？", msg);
        if (!ok) return;
        _vm.RemoveSkillGroup(g);
        _syncedMasterSkillGroupIndex = -1;
    }

    // ===== 群×シフト 担当可否・適切回数マトリクス =====

    /// <summary>
    /// [2026-09-02, 配線] Ws1SetGroupShift/Ws1SetGroupApt/Ws1ResetGroupApt/Ws1SetUse2（フェーズ9で
    /// 移植・テスト済み）が、この移植で一度も呼び出し口を持たなかった最大のギャップ。群がどのシフトを
    /// 担当できるか(canDo)を設定する手段がここにしか無く、これが無いと新規データでは全シフト担当不可の
    /// まま何も割り当てられない。既存の群/シフト数が可変な一覧は静的XAMLで組めないため
    /// <see cref="ScheduleView"/> と同じ Grid のコードビハインド組立て方式を使う。
    ///
    /// [フォーカス保持] 群/シフト数が変わらない限り毎回作り直さず、既存 <see cref="CheckBox"/>/
    /// <see cref="TextBox"/> の値だけ更新する（適切回数入力中にフォーカスが飛ぶのを防ぐ。テキストは
    /// フォーカスが無いセルだけ上書き）。
    /// </summary>
    private void RenderGroupShiftMatrix(UiState ui, bool editable)
    {
        var ws1 = _vm.Ws1();
        if (ws1 is null)
        {
            GroupShiftMatrixHost.Children.Clear();
            GroupShiftNameColumn.Children.Clear();
            GroupAptMatrixHost.Children.Clear();
            _matrixCells.Clear();
            _aptCells.Clear();
            _matrixHeaderButtons.Clear();
            _matrixGroupKigou = System.Array.Empty<string>();
            _matrixShiftKigou = System.Array.Empty<string>();
            RenderAptOverload();
            return;
        }

        var groupKigou = ws1.Groups.Select(g => g.Kigou).ToList();
        var shiftKigou = ws1.Shifts.Select(s => s.Kigou).ToList();
        if (!_matrixGroupKigou.SequenceEqual(groupKigou) || !_matrixShiftKigou.SequenceEqual(shiftKigou))
        {
            BuildGroupShiftMatrix(ws1);
            _matrixGroupKigou = groupKigou;
            _matrixShiftKigou = shiftKigou;
        }

        var onBg = (Brush)Application.Current.Resources["MagiPrimaryBrush"];
        var onFg = (Brush)Application.Current.Resources["MagiOnPrimaryBrush"];
        var offBg = (Brush)Application.Current.Resources["MagiSurfaceVariantBrush"];
        var offFg = (Brush)Application.Current.Resources["MagiOnBackgroundBrush"];
        foreach (var hb in _matrixHeaderButtons) hb.IsEnabled = editable;
        for (var g = 0; g < ws1.Groups.Count; g++)
        {
            for (var k = 0; k < ws1.Shifts.Count; k++)
            {
                var allowed = k < ws1.GroupShift[g].Count && ws1.GroupShift[g][k] != 0;
                if (_matrixCells.TryGetValue((g, k), out var cell))
                {
                    // ON=濃い主色地＋白✓ / OFF=薄い地＋「—」（色だけに依存しない手がかり）。
                    cell.Background = allowed ? onBg : offBg;
                    cell.Foreground = allowed ? onFg : offFg;
                    cell.Content = allowed ? "✓" : "—";
                    cell.IsEnabled = editable;
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(cell,
                        $"{ws1.Groups[g].Name} × {ws1.Shifts[k].Kigou}: {(allowed ? "担当できる" : "担当しない")}");
                }
                if (_aptCells.TryGetValue((g, k), out var apt))
                {
                    apt.IsEnabled = editable && allowed;
                    if (apt.FocusState == FocusState.Unfocused)
                    {
                        // [レビュー指摘 2026-09-04] Validate は GroupShiftApt の行数不足（空配列・旧形式）を
                        //   許容するので、行の存在も確認する（旧: 列だけ確認＝IndexOutOfRange で画面ごと落ちた）。
                        apt.Text = g < ws1.GroupShiftApt.Count && k < ws1.GroupShiftApt[g].Count ? ws1.GroupShiftApt[g][k] : "";
                    }
                }
            }
        }

        RenderAptOverload();
    }

    /// <summary>
    /// [2026-09-02, 配線] AptBalances（フェーズ7で移植・テスト済み、Kotlin原本 Ws1Editor.kt の
    /// 「この目標は達成できません」インライン警告）はこれまで呼び出し口が無かった——同じ診断は
    /// 分析タブの設定ミス一覧(SettingIssues)にも出るが、タブを跨がず**適切回数を入力しているその場**で
    /// 即座に見えるのが Kotlin 原本の狙い。マトリクス直下に、超過しているシフトだけ列挙する。
    /// </summary>
    private void RenderAptOverload()
    {
        var overloaded = _vm.AptBalances().Where(b => b.Overloaded).ToList();
        AptOverloadPanel.Visibility = overloaded.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AptOverloadList.Children.Clear();
        foreach (var b in overloaded)
        {
            var reason = b.IsRest
                ? $"「{b.Kigou}」目標の合計 {b.AptSum}回 に対し、他シフトの上限を差し引いた最大可能日数の合計は {b.Capacity}回。{b.Shortfall}回ぶんは必ず届きません。"
                : $"「{b.Kigou}」目標の合計 {b.AptSum}回 に対し、必要人数の合計は {b.Capacity}回。{b.Shortfall}回ぶんは必ず届きません。";
            AptOverloadList.Children.Add(new TextBlock { Text = reason, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        }
    }

    /// <summary>
    /// [マトリックス再設計（ユーザー提示案）] 行=群・列=シフトの2次元マトリクス。
    ///  - 左列（群名）は <c>GroupShiftNameColumn</c> に置いて横スクロールの外＝固定。右側（シフト名ヘッダ＋
    ///    セル）だけ <c>GroupShiftMatrixHost</c> で横スクロール。両側とも行高を <see cref="MatrixRowH"/> に
    ///    固定してヘッダ/セルの横ずれを防ぐ。
    ///  - セルは<b>全面がタップ標的</b>の Button（44×44、ON=主色地＋✓ / OFF=薄い地＋—）。チェックボックスと
    ///    数字欄を同じセルに重ねていた旧レイアウトは、適切回数を別マトリクス（<c>GroupAptMatrixHost</c>）へ分離。
    ///  - 行ヘッダ（群名）タップ＝その群の全シフトを一括（1つでもOFFがあれば全ON、全ONなら全OFF＝休は残る）。
    ///    列ヘッダ（シフト名）タップ＝そのシフトを全群へ一括（同じ規則。休の列はOFFにできない＝VMが案内）。
    /// </summary>
    /// <summary>[phase9 #12] 回数マトリクス（Kotlin原本 <c>StaffShiftMatrixCard</c>）。表示専用の再配置＝判定は
    /// <see cref="UiState.CountViolations"/>・<see cref="MagiViewModel.StaffCellLimits"/>（チェッカー側が正）だけを読む。
    /// 色は既存の 2 色言語（不足=赤系／超過=橙系）。目標(apt)のズレは薄く、個人上下限の逸脱は濃く。</summary>
    private void RenderStaffShiftMatrix(UiState ui, bool editable)
    {
        StaffShiftMatrixHost.Children.Clear();
        StaffShiftMatrixHost.RowDefinitions.Clear();
        StaffShiftMatrixHost.ColumnDefinitions.Clear();
        var ws1 = _vm.Ws1();
        if (ws1 is null || ws1.Shifts.Count == 0 || ws1.Staff.Count == 0)
        {
            AptOverloadBanner.Visibility = Visibility.Collapsed;
            return;
        }
        var worst = _vm.AptBalances().Where(b => b.Overloaded).OrderByDescending(b => b.Shortfall).FirstOrDefault();
        AptOverloadBanner.Visibility = worst is null ? Visibility.Collapsed : Visibility.Visible;
        if (worst is not null)
        {
            AptOverloadText.Text = $"⚠ {KigouFormat.ToHankakuKigou(worst.Kigou)}：目標の合計{worst.AptSum}回 ＞ " +
                (worst.IsRest ? $"休める日数の上限{worst.Capacity}日" : $"必要人数の合計{worst.Capacity}回") + $"（{worst.Shortfall}回ぶんは必ず届きません）";
        }

        var K = ws1.Shifts.Count;
        var S = ws1.Staff.Count;
        var restIdx = _vm.RestShiftIndex();
        var shortC = ColorHex.Parse(ui.ViolationColorHex, ColorHex.Parse(ColorHex.DefaultHardVioHex, Colors.Crimson));
        var overC = ColorHex.Parse(ui.ViolationSoftColorHex, ColorHex.Parse(ColorHex.DefaultSoftVioHex, Colors.Orange));
        for (var r = 0; r <= S; r++) StaffShiftMatrixHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var c = 0; c <= K; c++) StaffShiftMatrixHost.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        void Header(int row, int col, string text, bool left)
        {
            var b = new Border
            {
                Padding = new Thickness(6, 4, 6, 4), MinWidth = left ? 128 : 68, MinHeight = 52,
                Background = (Brush)Application.Current.Resources["MagiSurfaceVariantBrush"],
                Child = new TextBlock
                {
                    Text = text, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Center,
                },
            };
            Grid.SetRow(b, row); Grid.SetColumn(b, col);
            StaffShiftMatrixHost.Children.Add(b);
        }
        Header(0, 0, "職員 (群)", left: true);
        for (var k = 0; k < K; k++) Header(0, k + 1, KigouFormat.ToHankakuKigou(ws1.Shifts[k].Kigou), left: false);

        for (var i = 0; i < S; i++)
        {
            var staff = ws1.Staff[i];
            var g = staff.GroupIdx;
            var gk = g >= 0 && g < ws1.Groups.Count ? ws1.Groups[g].Kigou : "?";
            Header(i + 1, 0, $"{staff.Name} ({gk})", left: true);
            var counts = new int[K];
            if (i < ui.Schedule.Count) foreach (var v in ui.Schedule[i]) if (v >= 0 && v < K) counts[v]++;
            for (var k = 0; k < K; k++)
            {
                var allowed = g >= 0 && g < ws1.GroupShift.Count && k < ws1.GroupShift[g].Count && ws1.GroupShift[g][k] == 1;
                ui.CountViolations.TryGetValue($"{i},{k}", out var vio);
                var (text, sub, bg, bold, bordered) = MatrixCell(allowed, counts[k], _vm.StaffCellLimits(i, k), vio, k == restIdx, shortC, overC);
                var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                content.Children.Add(new TextBlock { Text = text, FontSize = 12, FontWeight = bold ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal, HorizontalAlignment = HorizontalAlignment.Center });
                if (sub.Length > 0) content.Children.Add(new TextBlock { Text = sub, FontSize = 10, Opacity = 0.8, HorizontalAlignment = HorizontalAlignment.Center });
                var cell = new Button
                {
                    Content = content, MinWidth = 68, MinHeight = 52, Padding = new Thickness(4, 2, 4, 2), CornerRadius = new CornerRadius(4),
                    Background = bg is { } c ? new SolidColorBrush(c) : new SolidColorBrush(Colors.Transparent),
                    BorderThickness = new Thickness(bordered ? 1 : 0), IsEnabled = editable && allowed,
                };
                var (si, sk) = (i, k);
                cell.Click += async (_, _) => await ShowStaffShiftCellDialogAsync(si, sk);
                Grid.SetRow(cell, i + 1); Grid.SetColumn(cell, k + 1);
                StaffShiftMatrixHost.Children.Add(cell);
            }
        }
    }

    /// <summary>セルの見た目（Kotlin原本 <c>matrixCell</c>）。透明地・薄色（目標のズレ）・濃色（上下限の逸脱）の 3 段。</summary>
    private static (string Text, string Sub, Color? Bg, bool Bold, bool Bordered) MatrixCell(
        bool allowed, int count, (int? Lo, int? Hi, int? Apt) limits, string? vio, bool isRest, Color shortC, Color overC)
    {
        var (lo, hi, apt) = limits;
        if (!allowed) return ("—", "", Color.FromArgb(90, 0xE3, 0xED, 0xEA), false, false);
        string Range() => (lo, hi) switch
        {
            ({ } l, { } h) when l == h => $"={l}",
            ({ } l, { } h) => $"{l}〜{h}",
            ({ } l, null) => $"{l}〜",
            (null, { } h) => $"〜{h}",
            _ => "",
        };
        static Color A(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
        var orange = ColorHex.Parse(MagiAccent.Orange, Colors.Orange);
        return vio switch
        {
            "vio-low" => ($"▼{count}", Range(), A(shortC, 115), true, false),
            "vio-high" => ($"▲{count}", Range(), A(overC, 128), true, false),
            "vio-aptLow" => ($"▼{count}", $"目標{apt ?? count}", isRest ? A(orange, 71) : A(shortC, 56), false, false),
            "vio-aptHigh" => ($"▲{count}", $"目標{apt ?? count}", isRest ? A(orange, 71) : A(overC, 56), false, false),
            null => lo is not null || hi is not null ? ($"{count}", Range(), null, false, true)
                : apt is not null ? ($"{count}", $"目標{apt}", null, false, false)
                : ($"{count}", "", null, false, false),
            _ => ($"△{count}", "", A(overC, 46), false, false),
        };
    }

    /// <summary>セルタップの編集ダイアログ（Kotlin原本 <c>StaffShiftCellSheet</c>）: ①群の目標（同じ群の全員に影響）②個人の上下限 の 2 系統だけ。</summary>
    private async Task ShowStaffShiftCellDialogAsync(int i, int k)
    {
        var ui = _vm.Ui;
        var v = _vm.Ws1();
        if (v is null || i >= v.Staff.Count || k >= v.Shifts.Count) return;
        var name = v.Staff[i].Name;
        var g = v.Staff[i].GroupIdx;
        var groupName = g >= 0 && g < v.Groups.Count ? v.Groups[g].Name : "?";
        var kigou = KigouFormat.ToHankakuKigou(v.Shifts[k].Kigou);
        var count = i < ui.Schedule.Count ? ui.Schedule[i].Count(x => x == k) : 0;
        var (lo0, hi0, apt) = _vm.StaffCellLimits(i, k);
        ui.CountViolations.TryGetValue($"{i},{k}", out var vio);
        var hasRange = lo0 is not null || hi0 is not null;

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = $"現在 {count}回", FontWeight = Microsoft.UI.Text.FontWeights.Bold });
        var status = vio switch
        {
            "vio-low" when lo0 is { } l => $"下限{l}回に対し現在{count}回（{Math.Max(0, l - count)}回不足）",
            "vio-high" when hi0 is { } h => $"上限{h}回に対し現在{count}回（{Math.Max(0, count - h)}回超過）",
            "vio-aptLow" when apt is { } a => $"目標{a}回に対し現在{count}回（{Math.Max(0, a - count)}回未達）",
            "vio-aptHigh" when apt is { } a => $"目標{a}回に対し現在{count}回（{Math.Max(0, count - a)}回超過）",
            _ => apt is { } a2 ? $"目標{a2}回どおりです" : "",
        };
        if (status.Length > 0) panel.Children.Add(new TextBlock { Text = status, TextWrapping = TextWrapping.Wrap });

        var raw = g >= 0 && g < v.GroupShiftApt.Count && k < v.GroupShiftApt[g].Count ? v.GroupShiftApt[g][k] : "";
        panel.Children.Add(new TextBlock { Text = $"群の目標（{groupName} 全員に適用）", FontWeight = Microsoft.UI.Text.FontWeights.Bold });
        var aptRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var aptValue = new TextBlock { Text = string.IsNullOrWhiteSpace(raw) ? "なし" : raw.Trim(), MinWidth = 40, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var minus = new Button { Content = "−", MinWidth = 44 };
        var plus = new Button { Content = "＋", MinWidth = 44 };
        void SetApt(string value) { _vm.Ws1SetGroupApt(g, k, value); raw = value; aptValue.Text = string.IsNullOrWhiteSpace(value) ? "なし" : value; }
        minus.Click += (_, _) => { var c = int.TryParse(raw.Trim(), out var n) ? n : (int?)null; SetApt(c is null ? "0" : c <= 0 ? "" : (c.Value - 1).ToString()); };
        plus.Click += (_, _) => { var c = int.TryParse(raw.Trim(), out var n) ? n : -1; SetApt(Math.Max(0, c + 1).ToString()); };
        aptRow.Children.Add(new TextBlock { Text = kigou, MinWidth = 48, VerticalAlignment = VerticalAlignment.Center });
        aptRow.Children.Add(minus); aptRow.Children.Add(aptValue); aptRow.Children.Add(plus);
        panel.Children.Add(aptRow);
        if (apt is { } aEff && (!int.TryParse(raw.Trim(), out var rawN) || rawN != aEff))
            panel.Children.Add(new TextBlock { Text = $"個人の上下限で {(string.IsNullOrWhiteSpace(raw) ? "0" : raw.Trim())}→{aEff} に調整されています", FontSize = 12, Opacity = 0.8 });

        panel.Children.Add(new TextBlock { Text = "個人の下限・上限（このシフトだけ）", FontWeight = Microsoft.UI.Text.FontWeights.Bold });
        var loBox = new TextBox { Header = "下限", PlaceholderText = "なし", Text = lo0?.ToString() ?? "", Width = 120 };
        var hiBox = new TextBox { Header = "上限", PlaceholderText = "なし", Text = hi0?.ToString() ?? "", Width = 120 };
        var rangeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        rangeRow.Children.Add(loBox); rangeRow.Children.Add(hiBox);
        panel.Children.Add(rangeRow);
        var conflict = new TextBlock { Text = "上限は下限以上にしてください。", FontSize = 12, Foreground = (Brush)Application.Current.Resources["MagiErrorBrush"], Visibility = Visibility.Collapsed };
        panel.Children.Add(conflict);
        if (vio == "vio-high" || vio == "vio-low")
        {
            var quick = new Button { Content = vio == "vio-high" ? $"上限を{count}に引き上げて解決" : $"下限を{count}に下げて解決", HorizontalAlignment = HorizontalAlignment.Stretch };
            panel.Children.Add(quick);
            quick.Click += (_, _) =>
            {
                if (vio == "vio-high") _vm.SetStaffRange(i, k, lo0?.ToString() ?? "", count.ToString());
                else _vm.SetStaffRange(i, k, count.ToString(), hi0?.ToString() ?? "");
            };
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = $"{name} ・ {kigou}", Content = new ScrollViewer { Content = panel, MaxHeight = 520 },
            PrimaryButtonText = "この上下限を適用", SecondaryButtonText = hasRange ? "上下限を解除" : null, CloseButtonText = "閉じる",
            DefaultButton = ContentDialogButton.Close,
        };
        void Validate()
        {
            var bad = V6SanityPort.RangeOrderConflict(loBox.Text, hiBox.Text) is not null;
            conflict.Visibility = bad ? Visibility.Visible : Visibility.Collapsed;
            dialog.IsPrimaryButtonEnabled = !bad && (loBox.Text.Trim().Length > 0 || hiBox.Text.Trim().Length > 0 || hasRange);
        }
        loBox.TextChanged += (_, _) => Validate();
        hiBox.TextChanged += (_, _) => Validate();
        Validate();
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary) _vm.SetStaffRange(i, k, loBox.Text.Trim(), hiBox.Text.Trim());
        else if (result == ContentDialogResult.Secondary) _vm.RemoveStaffRange(i, k);
    }

    private const double MatrixRowH = 44;
    private const double MatrixCellW = 44;

    private void BuildGroupShiftMatrix(MagiViewModel.Ws1View ws1)
    {
        GroupShiftMatrixHost.Children.Clear();
        GroupShiftMatrixHost.RowDefinitions.Clear();
        GroupShiftMatrixHost.ColumnDefinitions.Clear();
        GroupShiftNameColumn.Children.Clear();
        GroupShiftNameColumn.RowDefinitions.Clear();
        GroupAptMatrixHost.Children.Clear();
        GroupAptMatrixHost.RowDefinitions.Clear();
        GroupAptMatrixHost.ColumnDefinitions.Clear();
        _matrixCells.Clear();
        _aptCells.Clear();
        _matrixHeaderButtons.Clear();

        var groupCount = ws1.Groups.Count;
        var shiftCount = ws1.Shifts.Count;
        var xs = (double)Application.Current.Resources["MagiSpacingXS"];
        for (var r = 0; r <= groupCount; r++)
        {
            GroupShiftMatrixHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(MatrixRowH) });
            GroupShiftNameColumn.RowDefinitions.Add(new RowDefinition { Height = new GridLength(MatrixRowH) });
            GroupAptMatrixHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        for (var c = 0; c < shiftCount; c++) GroupShiftMatrixHost.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var c = 0; c <= shiftCount; c++) GroupAptMatrixHost.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Button HeaderButton(string text, string automationName)
        {
            var b = new Button
            {
                Content = text,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                MinHeight = MatrixRowH,
                MinWidth = MatrixCellW,
                // [Token] 密マトリクス用の意図的な値（ScheduleView のセルと同じ理由で据え置き）。
                Padding = new Thickness(xs, 0, xs, 0),
                CornerRadius = new CornerRadius(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, automationName);
            _matrixHeaderButtons.Add(b);
            return b;
        }

        // 左上の角（固定列側）＝空。行ヘッダ＝群名（タップで行一括）。
        var corner = new TextBlock { Text = "群 ＼ シフト", FontSize = 11, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(xs, 0, xs, 0) };
        Grid.SetRow(corner, 0);
        GroupShiftNameColumn.Children.Add(corner);
        for (var g = 0; g < groupCount; g++)
        {
            var gg = g;
            var rowBtn = HeaderButton($"{ws1.Groups[g].Kigou} {ws1.Groups[g].Name}", $"グループ {ws1.Groups[g].Name}: タップで全シフトを一括切替");
            rowBtn.HorizontalAlignment = HorizontalAlignment.Stretch;
            rowBtn.HorizontalContentAlignment = HorizontalAlignment.Left;
            rowBtn.Click += (_, _) =>
            {
                if (_syncingFromModel) return;
                var cur = _vm.Ws1();
                if (cur is null || gg >= cur.GroupShift.Count) return;
                var anyOff = cur.GroupShift[gg].Any(v => v == 0);
                _vm.Ws1SetGroupShiftRow(gg, anyOff);
            };
            Grid.SetRow(rowBtn, g + 1);
            GroupShiftNameColumn.Children.Add(rowBtn);
        }

        // 列ヘッダ＝シフト名（タップで列一括）。
        for (var k = 0; k < shiftCount; k++)
        {
            var kk = k;
            var colBtn = HeaderButton(ws1.Shifts[k].Kigou, $"シフト {ws1.Shifts[k].Kigou}: タップで全グループへ一括切替");
            colBtn.Click += (_, _) =>
            {
                if (_syncingFromModel) return;
                var cur = _vm.Ws1();
                if (cur is null) return;
                var anyOff = cur.GroupShift.Any(row => kk >= row.Count || row[kk] == 0);
                _vm.Ws1SetGroupShiftColumn(kk, anyOff);
            };
            Grid.SetRow(colBtn, 0);
            Grid.SetColumn(colBtn, k);
            GroupShiftMatrixHost.Children.Add(colBtn);
        }

        // セル＝全面タップの Button（見た目は RenderGroupShiftMatrix が同期する）。
        for (var g = 0; g < groupCount; g++)
        {
            for (var k = 0; k < shiftCount; k++)
            {
                var gg = g;
                var kk = k;
                var cell = new Button
                {
                    MinWidth = MatrixCellW,
                    MinHeight = MatrixRowH,
                    Width = MatrixCellW,
                    Height = MatrixRowH,
                    Padding = new Thickness(0),
                    CornerRadius = new CornerRadius(0),
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    FontSize = 16,
                };
                cell.Click += (_, _) =>
                {
                    if (_syncingFromModel) return;
                    var cur = _vm.Ws1();
                    if (cur is null || gg >= cur.GroupShift.Count || kk >= cur.GroupShift[gg].Count) return;
                    _vm.Ws1SetGroupShift(gg, kk, cur.GroupShift[gg][kk] == 0);
                };
                Grid.SetRow(cell, g + 1);
                Grid.SetColumn(cell, k);
                GroupShiftMatrixHost.Children.Add(cell);
                _matrixCells[(g, k)] = cell;
            }
        }

        // 適切回数マトリクス（別表）。ヘッダは TextBlock、セルは数字欄。
        void AddAptHeader(int row, int col, string text)
        {
            var block = new TextBlock
            {
                Text = text, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Padding = new Thickness(xs), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetRow(block, row);
            Grid.SetColumn(block, col);
            GroupAptMatrixHost.Children.Add(block);
        }
        AddAptHeader(0, 0, "");
        for (var k = 0; k < shiftCount; k++) AddAptHeader(0, k + 1, ws1.Shifts[k].Kigou);
        for (var g = 0; g < groupCount; g++) AddAptHeader(g + 1, 0, ws1.Groups[g].Name);
        for (var g = 0; g < groupCount; g++)
        {
            for (var k = 0; k < shiftCount; k++)
            {
                var gg = g;
                var kk = k;
                var apt = new TextBox { Width = 44, FontSize = 12, Margin = new Thickness(xs) };
                apt.LostFocus += (_, _) => { if (!_syncingFromModel) _vm.Ws1SetGroupApt(gg, kk, apt.Text); };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(apt, $"{ws1.Groups[g].Name} × {ws1.Shifts[k].Kigou} の適切回数");
                Grid.SetRow(apt, g + 1);
                Grid.SetColumn(apt, k + 1);
                GroupAptMatrixHost.Children.Add(apt);
                _aptCells[(g, k)] = apt;
            }
        }
    }

    private void OnUse2Toggled(object sender, RoutedEventArgs e)
    {
        if (_syncingFromModel) return;
        _vm.Ws1SetUse2(Use2Toggle.IsOn);
    }

    private void OnResetAptClick(object sender, RoutedEventArgs e)
    {
        if (_syncingFromModel) return;
        _vm.Ws1ResetGroupApt();
    }

    // ===== ドア切替 =====

    private void OnDoorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingFromModel) return;
        var i = DoorCombo.SelectedIndex;
        if (i < 0) return;
        _door = i;
        Render();
    }
}
