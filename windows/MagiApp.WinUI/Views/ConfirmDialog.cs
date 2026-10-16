using System.Collections.Generic;
using System.Threading.Tasks;
using MagiApp.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MagiApp.WinUI.Views;

/// <summary>確認＝問いの題＋3 点以内＋動詞の主ボタン 1 つ＋対の否定（Android <c>ConfirmDialog</c>、3.704.0 G-4）。削除の確認のため既定は Close。</summary>
internal static class ConfirmDialog
{
    public static async Task<bool> ShowAsync(XamlRoot root, string title, IEnumerable<string> points, string primary, string close)
    {
        var panel = new StackPanel { Spacing = 8 };
        foreach (var p in CellSheetLogic.ConfirmDialogPoints(points)) panel.Children.Add(new TextBlock { Text = p, TextWrapping = TextWrapping.Wrap });
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = panel,
            PrimaryButtonText = primary,
            CloseButtonText = close,
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
