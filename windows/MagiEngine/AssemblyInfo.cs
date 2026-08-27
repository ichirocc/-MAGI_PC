using System.Runtime.CompilerServices;

// Phase 3 (parity triangle) needs the test project to directly exercise a handful of
// `internal` members whose Kotlin originals are themselves `internal fun`/scoped for
// verification-only use (DeltaEvaluator.familyRaw/rangeWeighted/rangeRaw, Evaluator.C42PairCount)
// — these exist specifically so tests can cross-check per-family agreement between the checker,
// the full evaluator, and the delta evaluator without those accessors being part of the engine's
// public surface for downstream (WinUI) consumers.
[assembly: InternalsVisibleTo("MagiEngine.Tests")]
