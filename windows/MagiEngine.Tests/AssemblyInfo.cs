// [テスト分離] V6NativeOptimizer は「いちばん新しい実行が勝つ」プロセス全体の static ミラー
// （LastAlternatives/LastFusionElites/LiveBest/LiveBestContentionCount/LastInfeasibleFamilies/
// TuningTelemetry。いずれも V6NativeOptimizer.RunSlot.cs 参照）を Kotlin 原本どおり意図的に持つ。
// これらは「実行が重ならない」ことを前提にした設計（3.335.0 の Kotlin コメント参照）で、xUnit の
// 既定（テストクラス間の並列実行）だと無関係な2つのテストクラスが同時に Optimize()/RunAdaptivePortfolio
// 等を呼ぶだけでこの前提が破れ、Assert.Same(V6NativeOptimizer.LastAlternatives, result.Alternatives)
// のような「自分が最新の実行のはず」というアサーションが間欠的に落ちる（実機で再現・観測済み）。
//
// 根本の static 設計自体は Kotlin 原本の忠実な移植であり変更しない（ライブ表示用の意図的な挙動）。
// 対処はテスト側の並列度を落とすことで「実行が重ならない」という前提を満たす——ホストJVMの
// 既存の検証手法（カスタム RunAllTests ランナーで全テストを逐次実行する。hosttest.sh 参照）と
// 同じ実行モデルへ揃える形でもある。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
