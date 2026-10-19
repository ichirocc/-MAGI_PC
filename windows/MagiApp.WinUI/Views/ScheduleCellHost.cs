using System.Collections.Generic;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace MagiApp.WinUI.Views;

/// <summary>
/// 勤務表の 1 セルを包むフォーカス可能な最小の <see cref="Control"/>。<see cref="Border"/> は自動化ピアもフォーカスも
/// 持たないため、ナレーターがセルを個別に読めなかった。<see cref="UserControl"/> はテンプレートを持たず
/// <c>Content</c> を直接描く＝要素が 1 つ増えるだけ（旧 <see cref="Button"/> のテンプレートと視覚状態は戻さない）。
/// </summary>
public sealed partial class ScheduleCellHost : UserControl
{
    protected override AutomationPeer OnCreateAutomationPeer() => new CellPeer(this);

    private sealed partial class CellPeer : FrameworkElementAutomationPeer
    {
        public CellPeer(ScheduleCellHost owner) : base(owner) { }

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.DataItem;

        protected override string GetClassNameCore() => nameof(ScheduleCellHost);

        // 名前（AutomationProperties.Name）が中身をすべて言うので、記号の TextBlock を二重に読ませない。
        protected override IList<AutomationPeer>? GetChildrenCore() => null;
    }
}
