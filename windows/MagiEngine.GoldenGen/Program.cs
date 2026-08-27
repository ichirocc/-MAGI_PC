// MagiEngine.GoldenGen — 使い捨てのオラクル生成ツール（非配布）。
//
// フェーズ0時点ではまだ何もしない。フェーズ3（パリティ三角形）で、
// Kotlin側エンジンを一度だけ走らせて凍結したゴールデンフィクスチャの族別内訳
// （MagiEngine.Tests/Fixtures/golden/ 配下の静的JSON）と、この C# 側エンジンの
// ViolationChecker/Evaluator/DeltaEvaluator の出力を突き合わせるための補助として使う。

Console.WriteLine($"MagiEngine.GoldenGen — ported from Kotlin {MagiEngine.EngineInfo.PortedFromVersion}");
Console.WriteLine("フェーズ0: まだ何もしません。");
