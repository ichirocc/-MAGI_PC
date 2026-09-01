using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using MagiApp.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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
    private bool _syncingFromModel;

    /// <summary>開いているドア。0=月次条件 / 1=職員管理 / 2=年間マスター。</summary>
    private int _door;

    /// <summary>職員/グループ/シフトの ComboBox の中身。差分があるときだけ作り直して選択を保つ。</summary>
    private IReadOnlyList<string> _staffItems = System.Array.Empty<string>();
    private IReadOnlyList<string> _groupItems = System.Array.Empty<string>();
    private IReadOnlyList<string> _wishStaffItems = System.Array.Empty<string>();
    private IReadOnlyList<string> _wishShiftItems = System.Array.Empty<string>();
    private IReadOnlyList<string> _needDayShiftItems = System.Array.Empty<string>();
    private IReadOnlyList<string> _masterGroupItems = System.Array.Empty<string>();
    private IReadOnlyList<string> _masterShiftItems = System.Array.Empty<string>();
    private IReadOnlyList<string> _constraintRowItems = System.Array.Empty<string>();

    /// <summary>入力欄へ最後に取り込んだ職員 index。選択が変わったときだけ名前欄を上書きする
    /// （毎回上書きすると入力途中の名前が消えるため）。</summary>
    private int _syncedStaffIndex = -1;

    /// <summary>年間マスターのグループ選択も同じ理由で「選択が変わったときだけ取り込む」。</summary>
    private int _syncedMasterGroupIndex = -1;

    /// <summary>年間マスターのシフト選択も同じ理由で「選択が変わったときだけ取り込む」。</summary>
    private int _syncedMasterShiftIndex = -1;

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
        _vm.Ui.PropertyChanged += OnUiChanged;
        Unloaded += (_, _) => _vm.Ui.PropertyChanged -= OnUiChanged;
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
        NextMonthButton.IsEnabled = editable;

        // 件数は GetSetupCounts（入力ガイド用の集計・1回の呼出しで揃う）から取る。
        var c = _vm.GetSetupCounts();
        MonthlyCountsText.Text =
            $"登録済み: 希望 {c.Wishes}件 ・ 日別の必要人数（例外） {c.NeedDay}件 ・ 職員 {c.Staff}名";
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

        WishListHost.Children.Clear();
        var rows = _vm.WishOverrides();
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

    // ===== 日別の必要人数（例外, 月次条件） =====

    private void RenderNeedDay(UiState ui, bool editable)
    {
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

        SyncStaffFields();

        var hasStaff = StaffCombo.SelectedIndex >= 0;
        var hasGroup = GroupCombo.SelectedIndex >= 0;
        AddStaffButton.IsEnabled = editable && hasGroup;
        EditStaffButton.IsEnabled = editable && hasStaff && hasGroup;
        RemoveStaffButton.IsEnabled = editable && hasStaff;
        StaffHintText.Text = editable
            ? "削除すると、その人の勤務・希望・個人の回数も消えます（「元に戻す」で戻せます）。"
            : (ui.Loaded ? "計算の実行中は職員を変更できません。終わってからにしてください。" : "");
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
        }
    }

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
        _vm.Ws1AddStaff(name, g);
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
        _vm.Ws1EditStaff(i, name, g);
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
        ShiftListText.Text = ui.ShiftSymbols.Count > 0
            ? string.Join(" ・ ", ui.ShiftSymbols)
            : "（未設定）";

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
        MasterGroupHintText.Text = editable
            ? "削除すると、所属者は先頭グループへ移動します（担当できるシフトが変わります）。"
            : (ui.Loaded ? "計算の実行中はグループを変更できません。終わってからにしてください。" : "");

        // ルールの件数は族ごとに出す（GetSetupCounts().Constraints はスキル群の2族を含まない合計のため、
        // 表示は ConstraintFamilies/SkillConstraintFamilies の実 Rows 数を正とする）。
        ConstraintListHost.Children.Clear();
        var families = _vm.ConstraintFamilies().Concat(_vm.SkillConstraintFamilies()).ToList();
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
    /// </summary>
    private sealed record ConstraintFamilyMeta(string Key, string[] FieldLabels);

    private static readonly ConstraintFamilyMeta[] ConstraintFamilyMetas =
    {
        new("cons1", new[] { "窓の日数", "シフト記号", "最低回数" }),
        new("cons2", new[] { "シフト記号", "合計回数" }),
        new("cons3", new[] { "1日目", "2日目", "3日目", "4日目", "5日目" }),
        new("cons3n", new[] { "1日目", "2日目", "3日目", "4日目", "5日目" }),
        new("cons3m", new[] { "1日目", "2日目", "3日目", "4日目", "5日目" }),
        new("cons3mn", new[] { "1日目", "2日目", "3日目", "4日目", "5日目" }),
        new("cons41", new[] { "群記号", "シフト記号", "下限", "上限" }),
        new("cons42", new[] { "群1記号", "シフト1記号", "群2記号", "シフト2記号" }),
        new("cons41s", new[] { "スキル群記号", "シフト記号", "下限", "上限" }),
        new("cons42s", new[] { "スキル群1記号", "シフト1記号", "スキル群2記号", "シフト2記号" }),
    };

    private string? ConstraintFamilyKey() => (ConstraintFamilyCombo.SelectedItem as ComboBoxItem)?.Tag as string;

    private IReadOnlyList<string> RowLabelsFor(string key)
    {
        var f = _vm.ConstraintFamilies().Concat(_vm.SkillConstraintFamilies()).FirstOrDefault(x => x.Key == key);
        if (f is null) return System.Array.Empty<string>();
        return f.Rows.Select((r, i) => $"{i + 1}: {r}").ToList();
    }

    /// <summary>種類の選択・行一覧・入力欄の表示/ラベルを同期する。種類が変わったときだけ行選択と
    /// 入力欄を強制的に空へ戻す（<see cref="_syncedConstraintFamilyKey"/> 参照）。</summary>
    private void SyncConstraintFields()
    {
        var key = ConstraintFamilyKey();
        var meta = key is null ? null : ConstraintFamilyMetas.FirstOrDefault(m => m.Key == key);
        var familyChanged = key != _syncedConstraintFamilyKey;
        _syncedConstraintFamilyKey = key;

        var boxes = new[] { ConstraintField1Box, ConstraintField2Box, ConstraintField3Box, ConstraintField4Box, ConstraintField5Box };
        var labels = new[] { ConstraintField1Label, ConstraintField2Label, ConstraintField3Label, ConstraintField4Label, ConstraintField5Label };
        for (var i = 0; i < boxes.Length; i++)
        {
            var show = meta is not null && i < meta.FieldLabels.Length;
            boxes[i].Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            labels[i].Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (show) labels[i].Text = meta!.FieldLabels[i];
            if (familyChanged) boxes[i].Text = "";
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
        var values = key is null ? null : _vm.ConstraintRowValues(key, idx);
        if (values is null) return;
        var boxes = new[] { ConstraintField1Box, ConstraintField2Box, ConstraintField3Box, ConstraintField4Box, ConstraintField5Box };
        for (var i = 0; i < boxes.Length; i++) boxes[i].Text = i < values.Count ? values[i] : "";
    }

    private IReadOnlyList<string> CollectConstraintFieldValues(ConstraintFamilyMeta meta)
    {
        var boxes = new[] { ConstraintField1Box, ConstraintField2Box, ConstraintField3Box, ConstraintField4Box, ConstraintField5Box };
        return boxes.Take(meta.FieldLabels.Length).Select(b => b.Text.Trim()).ToList();
    }

    /// <summary>[追加専用のフィールド並び替え] <c>AddCons42</c>/<c>AddCons42s</c> の引数順(g1,g2,s1,s2)は、
    /// 編集の値順(<see cref="MagiViewModel.ConstraintRowValues"/>=g1,s1,g2,s2、群と対応シフトを並べて
    /// 見せるUI都合)と異なるため、ここでだけ明示的に並べ替える。それ以外の族は両者が一致するため
    /// 並べ替え不要（<see cref="ConstraintFamilyMetas"/> クラスKDoc参照）。</summary>
    private bool TryAddConstraint(string key, IReadOnlyList<string> v, out string error)
    {
        error = "";
        string G(int i) => i < v.Count ? v[i] : "";
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
