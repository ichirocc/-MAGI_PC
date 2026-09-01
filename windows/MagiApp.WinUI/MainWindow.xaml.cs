using System.ComponentModel;
using MagiApp.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MagiApp.WinUI;

/// <summary>
/// [フェーズ8, 縦断スライス] 「フィクスチャ読込→違反検査→読取専用グリッド表示」の最小経路。
///
/// [ViewModel との接続方式] <see cref="MagiViewModel.Ui"/>（<see cref="UiState"/>）は
/// <c>ObservableObject</c> なので、<see cref="INotifyPropertyChanged.PropertyChanged"/> を購読して
/// 変化のたびに読取専用の再描画を行う（x:Bind は S×T の可変サイズグリッドを静的テンプレートで
/// 表現しづらいため、フェーズ9でItemsRepeaterベースへ置き換えるまではコードビハインド駆動とする）。
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly MagiViewModel _vm;

    public MainWindow(MagiViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        Title = "MAGI ShiftOptimizer";
        _vm.Ui.PropertyChanged += OnUiChanged;
        Render();
        _ = LoadFixtureAsync();
    }

    private void OnUiChanged(object? sender, PropertyChangedEventArgs e) => Render();

    /// <summary>
    /// フェーズ8の同梱フィクスチャ（Assets/sample_state_v6.json）を読み込む。フェーズ9で
    /// 「データを開く」導線（ファイルピッカー）に置き換わるまでの暫定入口。
    /// </summary>
    private async Task LoadFixtureAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "sample_state_v6.json");
        string json;
        try
        {
            json = await File.ReadAllTextAsync(path);
        }
        catch (Exception e)
        {
            StatusText.Text = $"フィクスチャを読み込めませんでした: {e.GetType().Name}: {e.Message}（{path}）";
            return;
        }
        _vm.LoadAsync(json);
    }

    private void Render()
    {
        var ui = _vm.Ui;
        StatusText.Text = ui.Loaded
            ? $"{ui.Message}（必須={ui.BestHard} 合計={ui.BestSoft}・{ui.Staff}名×{ui.Days}日×{ui.Shifts}シフト）"
            : ui.Message ?? "読込中…";
        RenderSchedule(ui);
    }

    private void RenderSchedule(UiState ui)
    {
        ScheduleGridHost.Children.Clear();
        ScheduleGridHost.RowDefinitions.Clear();
        ScheduleGridHost.ColumnDefinitions.Clear();
        if (!ui.Loaded || ui.Schedule.Count == 0) return;

        var staffCount = ui.Schedule.Count;
        var dayCount = ui.Schedule.Count > 0 ? ui.Schedule[0].Count : 0;
        // +1 列/行 = 職員名ヘッダー列・日番号ヘッダー行。
        for (var r = 0; r <= staffCount; r++) ScheduleGridHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var c = 0; c <= dayCount; c++) ScheduleGridHost.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        void AddCell(int row, int col, string text, bool header)
        {
            var block = new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = header ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                Padding = new Thickness(6, 4, 6, 4),
                MinWidth = header && col == 0 ? 96 : 32,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var border = new Border
            {
                Child = block,
                BorderBrush = new SolidColorBrush(Colors.LightGray),
                BorderThickness = new Thickness(0, 0, 1, 1),
            };
            Grid.SetRow(border, row);
            Grid.SetColumn(border, col);
            ScheduleGridHost.Children.Add(border);
        }

        AddCell(0, 0, "", header: true);
        for (var j = 0; j < dayCount; j++) AddCell(0, j + 1, $"{j + 1}", header: true);

        for (var i = 0; i < staffCount; i++)
        {
            var name = i < ui.StaffNames.Count ? ui.StaffNames[i] : $"#{i}";
            AddCell(i + 1, 0, name, header: true);
            var row = ui.Schedule[i];
            for (var j = 0; j < row.Count; j++)
            {
                var k = row[j];
                var sym = k >= 0 && k < ui.ShiftSymbols.Count ? ui.ShiftSymbols[k] : k.ToString();
                AddCell(i + 1, j + 1, sym, header: false);
            }
        }
    }
}
