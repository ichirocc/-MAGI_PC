using MagiApp.ViewModels;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace MagiApp.WinUI.Views;

/// <summary>
/// [Android 3.725.0] 開閉の印つきのボタン（Kotlin <c>DisclosureText</c>）。見た目は「{label} ▸／▾」、読み上げは名前と
/// 「開いています／閉じています」だけ（ナレーターに三角の記号を読ませない）。<paramref name="open"/>＝null は行き先の印。
/// </summary>
internal static class Disclosure
{
    public static void Set(ContentControl c, string label, bool? open)
    {
        c.Content = A11y.DisclosureText(label, open);
        AutomationProperties.SetName(c, label);
        AutomationProperties.SetItemStatus(c, open is { } o ? A11y.DisclosureState(o) : "");
    }
}
