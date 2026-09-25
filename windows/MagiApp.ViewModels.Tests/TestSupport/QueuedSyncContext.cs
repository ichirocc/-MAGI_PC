using System.Collections.Concurrent;

namespace MagiApp.ViewModels.Tests.TestSupport;

/// <summary>
/// 実アプリの UI スレッド（<c>DispatcherQueueSynchronizationContext</c>）の代わり。Post された継続を溜めておき、
/// テストが <see cref="RunUntil"/> で回したときだけテストのスレッドで走らせる。xUnit の <c>AsyncTestSyncContext</c> は
/// 継続をスレッドプールへ投げるだけなので、背景の違反チェック（最小データで約 0.3 ms）の完了がテスト本体の次の行と
/// 並行して走り、「まだ終わっていない」前提のアサーションが順序しだいで落ちる。
/// </summary>
internal sealed class QueuedSyncContext : SynchronizationContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();

    public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state));

    public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException();

    /// <summary>このスレッドの同期コンテキストを差し替えて <paramref name="body"/> を走らせ、終わったら戻す。</summary>
    public void Run(Action body)
    {
        var prev = Current;
        SetSynchronizationContext(this);
        try { body(); }
        finally { SetSynchronizationContext(prev); }
    }

    /// <summary>継続が 1 つ届くまで待つ（走らせはしない）。</summary>
    public void WaitForPosted()
    {
        if (!SpinWait.SpinUntil(() => !_queue.IsEmpty, Timeout)) throw new TimeoutException("no continuation was posted");
    }

    /// <summary><paramref name="task"/> が終わるまで、届いた継続をこのスレッドで順に走らせる。</summary>
    public void RunUntil(Task task)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!task.IsCompleted)
        {
            while (_queue.TryDequeue(out var item)) item.Callback(item.State);
            if (task.IsCompleted) break;
            if (DateTime.UtcNow > deadline) throw new TimeoutException("task did not complete");
            SpinWait.SpinUntil(() => !_queue.IsEmpty || task.IsCompleted, TimeSpan.FromMilliseconds(50));
        }
    }
}
