using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace MagiApp.WinUI.Views;

/// <summary>
/// [2026-09-11, ItemsView化] <see cref="ScheduleView"/> の勤務表マトリックス1セル分のビューモデル。
/// 旧実装（<c>RenderSchedule</c>が毎回 <see cref="Microsoft.UI.Xaml.Controls.Border"/> を直接
/// 組み立てて<c>Grid.Children</c>へ積む）を、<c>ItemsView</c>のデータバインディングへ置き換えるための型。
///
/// 行・列の位置(<see cref="Row"/>/<see cref="Col"/>)とスケジュール上の論理位置(<see cref="I"/>/<see cref="J"/>、
/// ヘッダーセルは-1)は別＝見た目の座標と業務データの座標を混同しない（旧実装の<c>AddCell(row,col,...)</c>と
/// <c>AddDataCell(row,col,i,j,k)</c>の呼び分けと同じ区別）。
///
/// [インスタンス再利用] <see cref="ScheduleView.RenderSchedule"/>は行・セルのコレクションを再利用し
/// （行数・列数が変わらない限り）、既存インスタンスのプロパティを書き換える。新しい参照を作って
/// <c>ItemsView.ItemsSource</c>を丸ごと差し替えると、<c>ItemsView</c>内蔵の<c>ScrollView</c>が
/// スクロール位置を失う（旧実装は<c>ScheduleGridHost</c>という同一の<see cref="Microsoft.UI.Xaml.Controls.Grid"/>
/// インスタンスの<c>Children</c>だけを差し替えていたため、外側の<c>ScrollViewer</c>の位置は毎回の
/// 再描画で失われなかった＝同じ体験を保つための対応）。
/// </summary>
public sealed partial class ScheduleCellVm : ObservableObject
{
    /// <summary>ヘッダー行・ヘッダー列も含めた見た目上の座標（0=ヘッダー行/列）。生成後は不変。</summary>
    public int Row { get; init; }
    public int Col { get; init; }

    /// <summary>業務データ上の座標。ヘッダーセルは -1。日ヘッダー(row=0,col>0)は I=-1・J=col-1。
    /// 職員名ヘッダー(row>0,col=0)は I=row-1・J=-1。</summary>
    public int I { get; set; } = -1;
    public int J { get; set; } = -1;

    public bool IsHeader { get; set; }
    public bool IsDataCell => !IsHeader && I >= 0 && J >= 0;

    [ObservableProperty] private string text = "";
    [ObservableProperty] private TextAlignment textAlignment = TextAlignment.Center;
    [ObservableProperty] private FontWeight fontWeight = FontWeights.Normal;
    [ObservableProperty] private Brush foreground = new SolidColorBrush(Colors.Black);
    [ObservableProperty] private Brush background = new SolidColorBrush(Colors.Transparent);
    [ObservableProperty] private Brush borderBrush = new SolidColorBrush(Colors.Transparent);
    [ObservableProperty] private Thickness borderThickness = new(0);
    [ObservableProperty] private double opacity = 1.0;
    [ObservableProperty] private double minWidth = 32;
    // [x:Bindの自動bool→Visibility変換に依存しないため、Visibility型そのものを持つ]
    [ObservableProperty] private Visibility wishDotVisibility = Visibility.Collapsed;
    [ObservableProperty] private Brush wishDotColor = new SolidColorBrush(Colors.Transparent);
    [ObservableProperty] private string? tooltip;
}

/// <summary>[2026-09-11, ItemsView化] 勤務表の1行分（<see cref="ScheduleCellVm"/>の横並び）。
/// row=0がヘッダー行（<see cref="ScheduleView"/>のItemsSourceの先頭要素）。</summary>
public sealed class ScheduleRowVm
{
    public ObservableCollection<ScheduleCellVm> Cells { get; } = new();
}
