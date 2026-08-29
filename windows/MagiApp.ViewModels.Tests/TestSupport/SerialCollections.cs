namespace MagiApp.ViewModels.Tests.TestSupport;

/// <summary>
/// [フェーズ9] <see cref="Work.OptimizationRepository"/> はプロセス全体で共有される static 状態
/// （Kotlin原本の <c>object</c> をそのまま反映した、プロセス内 pub/sub ブリッジ）。このコレクションに
/// 属するテストクラスどうしは xUnit のクラス間並列実行から除外され、常に直列に走る（同一クラス内の
/// 各 [Fact] は xUnit の既定動作で元々直列）。これにより、異なるテストクラスが同時に
/// Running/Progress/Result を書き換えて競合することを防ぐ。
///
/// このコレクションに入っていない他のテストクラス（<c>UiStateTest</c> 等、この static 状態に触れない
/// もの）は従来どおり並列に走ってよい——このコレクションは全体を直列化するのではなく、
/// 「共有 static 状態に触れるテストクラスどうし」だけを直列化する。
/// </summary>
[CollectionDefinition("OptimizationRepositoryState", DisableParallelization = true)]
public class OptimizationRepositoryStateCollection
{
}
