using System.ComponentModel;

namespace MagiApp.ViewModels;

/// <summary>
/// [レビュー指摘 2026-09-04] 画面（タブ）の <see cref="INotifyPropertyChanged.PropertyChanged"/> 購読を
/// 多重購読なしに付け外しする。WinUI3 のタブはキャッシュされて再利用されるため、
/// 「コンストラクタで購読・Unloaded で解除」だけだと一度離れたタブは以後の状態変化を受け取らない
/// （最適化の完了・停止・設定変更後も表示とボタンの活性が古いまま）。Loaded で <see cref="Attach"/>、
/// Unloaded で <see cref="Detach"/> を呼ぶ。<see cref="Attach"/> が true を返したときだけ再描画すれば、
/// 見えていなかった間の変化をまとめて描ける。WinUI に依存しないのでここで単体テストできる。
/// </summary>
public sealed class UiSubscription
{
    private readonly INotifyPropertyChanged _source;
    private readonly PropertyChangedEventHandler _handler;

    public UiSubscription(INotifyPropertyChanged source, PropertyChangedEventHandler handler)
    {
        _source = source;
        _handler = handler;
    }

    public bool IsAttached { get; private set; }

    /// <returns>新たに購読したとき true（既に購読中なら false＝多重購読しない）。</returns>
    public bool Attach()
    {
        if (IsAttached) return false;
        _source.PropertyChanged += _handler;
        IsAttached = true;
        return true;
    }

    /// <returns>解除したとき true（購読していなければ false）。</returns>
    public bool Detach()
    {
        if (!IsAttached) return false;
        _source.PropertyChanged -= _handler;
        IsAttached = false;
        return true;
    }
}
