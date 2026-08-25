// Host-buildable parity + benchmark harness for magi_native.cpp.
//
//   g++ -O3 -std=c++17 -DMAGI_HOST_TEST -I app/src/main/cpp \
//       tools/native/host_parity_bench.cpp -o /tmp/magi_host && /tmp/magi_host [state.flat ...]
//
// Parity: builds synthetic MagiProblem fixtures — and, when given flat problem files
//   (tools/native/state_to_flat.py で実 state JSON から生成) — then over millions of random
//   single-cell moves checks that the incrementally maintained SaChunk.score
//   (delta) exactly equals a fresh scalar fullEvalCombined (the Kotlin oracle's
//   C++ mirror). This is the runtime self-consistency check, run offline and
//   exhaustively. Mismatch count MUST be 0.
//
// [実データ形状] 実機で SaChunk 自己整合NG(status=1)が発生した回帰を受け、合成フィクスチャに
//   実データで実際に使われる形状を追加: 休(rest)シフトへの range/apt/c1/c2・実現不可能な希望
//   (非canDoへのwish)・盤面中の -1(未割当=normalizeSchedule の正規化結果)と非canDo値。
//   さらに flat ファイル引数で実 state（golden_state.json 等）の問題をそのまま照合できる。
// Bench: times raw deltaApply throughput (apply+revert) so bit-op changes can be
//   measured A/B via SaChunk.useBits.
//
// Exit code contract: 0 = all checks passed. 1 = the check(s) actually ran and found a
//   real mismatch (Δ!=full, cross-language divergence, or shared-handle divergence) —
//   this is the "the code is wrong" signal a CI/script consumer should act on.
//   2 = the run could not meaningfully execute at all (bad/missing flat file, missing
//   required arguments, --expect count not matching flat-file count) — a setup/usage
//   problem, not a finding about the code under test. The printed message (not a further
//   split of exit codes) disambiguates within the "2" bucket; nothing in this repo greps
//   for a specific sub-code today, so a single "could not run" code is intentional.
//
// Shared-handle concurrency check (runSharedHandleConcurrency, below) is two-tier by
//   design: a plain (`-O3`, no sanitizer) build runs it once per invocation as a cheap
//   smoke check that can still catch an outright shared-mutable-state bug if it happens
//   to produce an observably different value serial-vs-parallel. The *race detector*
//   proper is `-fsanitize=thread` — wired as a dedicated, fast (~10s total) CI job
//   (native-parity.yml) that runs `--shared-only`, so the TSAN protection these comments
//   describe is continuously enforced, not just a manual step a developer has to remember.
#ifndef MAGI_HOST_TEST
#define MAGI_HOST_TEST
#endif
#include "magi_native.cpp"

#include <cstdio>
#include <chrono>
#include <random>
#include <algorithm>
#include <cstring>
#include <fstream>
#include <string>
#include <thread>

// Fill the derived tables the JNI decode normally builds (members / bucketHas /
// staffForShift), so a hand-built MagiProblem behaves like a decoded one.
static void finalizeProblem(MagiProblem& p) {
    p.members.assign((size_t)p.G, {});
    for (int i = 0; i < p.S; i++) { int g = p.sgrp[i]; if (g >= 0 && g < p.G) p.members[g].push_back(i); }
    p.bucketHas.assign((size_t)p.G * p.K, 0);
    for (int g = 0; g < p.G; g++) for (int k : p.bucket[g]) if (k >= 0 && k < p.K) p.bucketHas[(size_t)g * p.K + k] = 1;
    p.staffForShift.assign((size_t)p.K, {});
    for (int i = 0; i < p.S; i++) { int g = p.sgrp[i]; if (g < 0 || g >= p.G) continue;
        for (int k : p.bucket[g]) if (k >= 0 && k < p.K) p.staffForShift[k].push_back(i); }
}

static C3r mkC3(std::initializer_list<int> seq) {
    C3r r; for (int x : seq) r.seq.push_back(x);
    r.singleRun = !r.seq.empty();
    for (size_t l = 1; l < r.seq.size(); l++) if (r.seq[l] != r.seq[0]) { r.singleRun = false; break; }
    return r;
}

// ---- 実データ問題ローダ: tools/native/state_to_flat.py の MAGIFLAT1 形式 ----
static bool loadFlat(const char* path, MagiProblem& p, std::vector<int>& board) {
    std::ifstream f(path);
    if (!f) { printf("loadFlat: cannot open %s\n", path); return false; }
    std::string magic; f >> magic;
    if (magic != "MAGIFLAT1") { printf("loadFlat: bad magic in %s\n", path); return false; }
    auto readArr = [&](std::vector<int>& v) -> bool {
        long long n; if (!(f >> n) || n < 0) return false;
        v.resize((size_t)n);
        for (long long i = 0; i < n; i++) if (!(f >> v[i])) return false;
        return true;
    };
    std::vector<int> meta, staffArr, canDo, wish, needs, ranges, cons, c3, bucketBlob;
    if (!readArr(meta) || meta.size() != 7 || !readArr(staffArr) || !readArr(canDo) ||
        !readArr(wish) || !readArr(needs) || !readArr(ranges) || !readArr(cons) ||
        !readArr(c3) || !readArr(bucketBlob) || !readArr(board)) {
        printf("loadFlat: truncated %s\n", path); return false;
    }
    p.S = meta[0]; p.T = meta[1]; p.K = meta[2]; p.G = meta[3];
    p.restIdx = meta[4]; p.dow0 = meta[5]; p.use2 = meta[6] != 0;
    const int S = p.S, T = p.T, K = p.K, G = p.G;
    p.sgrp.assign(staffArr.begin(), staffArr.begin() + S);
    p.ssk.assign(staffArr.begin() + S, staffArr.begin() + 2 * S);
    p.canDo.assign(canDo.begin(), canDo.end());
    p.wish = wish;
    p.need1.assign(needs.begin(), needs.begin() + (size_t)K * T);
    p.need2.assign(needs.begin() + (size_t)K * T, needs.end());
    p.rangeLo.assign(ranges.begin(), ranges.begin() + (size_t)S * K);
    p.rangeHi.assign(ranges.begin() + (size_t)S * K, ranges.begin() + 2 * (size_t)S * K);
    p.apt.assign(ranges.begin() + 2 * (size_t)S * K, ranges.end());
    size_t ix = 0;
    auto rd = [&]() { return cons[ix++]; };
    int n = rd(); for (int c = 0; c < n; c++) { int d1 = rd(), si = rd(), d2 = rd(); p.cons1.push_back({d1, si, d2}); }
    n = rd(); for (int c = 0; c < n; c++) { int si = rd(), ct = rd(); p.cons2.push_back({si, ct}); }
    n = rd(); for (int c = 0; c < n; c++) { int g = rd(), s = rd(), l = rd(), u = rd(); p.cons41.push_back({g, s, l, u}); }
    n = rd(); for (int c = 0; c < n; c++) { int g1 = rd(), s1 = rd(), g2 = rd(), s2 = rd(); p.cons42.push_back({g1, s1, g2, s2}); }
    n = rd(); for (int c = 0; c < n; c++) { int g = rd(), s = rd(), l = rd(), u = rd(); p.cons41s.push_back({g, s, l, u}); }
    n = rd(); for (int c = 0; c < n; c++) { int g1 = rd(), s1 = rd(), g2 = rd(), s2 = rd(); p.cons42s.push_back({g1, s1, g2, s2}); }
    size_t cx = 0;
    auto rdc = [&]() { return c3[cx++]; };
    for (std::vector<C3r>* fam : {&p.cons3, &p.cons3n, &p.cons3m, &p.cons3mn}) {
        int cnt = rdc();
        for (int r = 0; r < cnt; r++) {
            C3r row; int len = rdc();
            for (int l = 0; l < len; l++) row.seq.push_back(rdc());
            row.singleRun = !row.seq.empty();
            for (size_t l = 1; l < row.seq.size(); l++) if (row.seq[l] != row.seq[0]) { row.singleRun = false; break; }
            fam->push_back(row);
        }
    }
    size_t bx = 0;
    p.bucket.assign((size_t)G, {});
    for (int g = 0; g < G; g++) { int len = bucketBlob[bx++]; for (int l = 0; l < len; l++) p.bucket[g].push_back(bucketBlob[bx++]); }
    finalizeProblem(p);
    return true;
}

// Build a varied but self-consistent problem. rest = shift 0.
static MagiProblem buildProblem(int S, int T, int K, int G, uint64_t seed, bool use2) {
    std::mt19937_64 rng(seed);
    auto ri = [&](int lo, int hi) { return lo + (int)(rng() % (uint64_t)(hi - lo + 1)); };
    MagiProblem p;
    p.S = S; p.T = T; p.K = K; p.G = G; p.restIdx = 0; p.dow0 = ri(0, 6); p.use2 = use2;
    p.sgrp.resize(S); p.ssk.resize(S);
    int Gs = std::max(1, G - 1);
    for (int i = 0; i < S; i++) { p.sgrp[i] = ri(0, G - 1); p.ssk[i] = ri(0, Gs - 1); }
    // bucket[g] = rest + a couple of random work shifts (1..K-1)
    p.bucket.assign((size_t)G, {});
    for (int g = 0; g < G; g++) {
        p.bucket[g].push_back(0);
        int nShift = std::min(K - 1, ri(1, 3));
        for (int s = 0; s < nShift; s++) { int k = ri(1, K - 1); if (std::find(p.bucket[g].begin(), p.bucket[g].end(), k) == p.bucket[g].end()) p.bucket[g].push_back(k); }
    }
    // canDo(i,k) = k in bucket[sgrp[i]]
    p.canDo.assign((size_t)S * K, 0);
    for (int i = 0; i < S; i++) for (int k : p.bucket[p.sgrp[i]]) p.canDo[(size_t)i * K + k] = 1;
    // wish: ~10% of cells wished. [実データ形状] うち1/4は任意シフト＝非canDoの実現不可能な希望を含む
    //   （実データに常在。pref は canDo ガードで数えない＝Δ/フル双方の対称性をここで照合する）。
    p.wish.assign((size_t)S * T, -1);
    for (int i = 0; i < S; i++) for (int j = 0; j < T; j++) if (ri(0, 9) == 0) {
        if (ri(0, 3) == 0) { p.wish[(size_t)i * T + j] = ri(0, K - 1); }
        else { const auto& b = p.bucket[p.sgrp[i]]; p.wish[(size_t)i * T + j] = b[ri(0, (int)b.size() - 1)]; }
    }
    // needs: a few critical shifts get P1 (and P2 if use2)
    p.need1.assign((size_t)K * T, -1); p.need2.assign((size_t)K * T, -1);
    for (int k = 1; k < K; k++) if (ri(0, 1) == 0) for (int j = 0; j < T; j++) {
        p.need1[(size_t)k * T + j] = ri(1, 2);
        if (use2 && ri(0, 1) == 0) p.need2[(size_t)k * T + j] = ri(1, 2);
    }
    // ranges + apt: sparse. [実データ形状] k=0(休) も対象（実データは休の上下限・apt目標が最も濃い:
    //   例 golden_state 休 lo=10/hi=10・apt=10。旧フィクスチャは k>=1 のみで未照合だった）。
    p.rangeLo.assign((size_t)S * K, INT32_MIN); p.rangeHi.assign((size_t)S * K, INT32_MAX); p.apt.assign((size_t)S * K, -1);
    for (int i = 0; i < S; i++) for (int k = 0; k < K; k++) if (p.canDo[(size_t)i * K + k]) {
        if (ri(0, 2) == 0) p.rangeLo[(size_t)i * K + k] = ri(1, 4 + (k == 0 ? 8 : 0));
        if (ri(0, 2) == 0) p.rangeHi[(size_t)i * K + k] = ri(3, 8 + (k == 0 ? 8 : 0));
        if (ri(0, 2) == 0) p.apt[(size_t)i * K + k] = ri(0, 6 + (k == 0 ? 6 : 0));
    }
    // cons1 windows. [実データ形状] rest(0) 窓も混ぜる（例 golden: 14日窓 休>=5）。
    int nC1 = ri(2, 5);
    for (int c = 0; c < nC1; c++) { int d1 = ri(2, std::min(T, 14)); p.cons1.push_back({d1, ri(0, K - 1), ri(1, std::max(1, d1 - 1))}); }
    // [A5/3.273.0 実データ形状] 同一シフトに複数窓ルール（golden の「休 5日窓>=1 かつ 15日窓>=4」型）を
    //   明示的に追加し、重複スライド窓での c1 累積（scalar/bit 両path）を照合対象に含める。rest(0) と
    //   もう1つ非rest シフトへ、短窓＋長窓の2規則を重ねる。
    {
        int longW = std::min(T, 15);
        p.cons1.push_back({5, 0, 1});            // 休 5日窓>=1
        p.cons1.push_back({longW, 0, std::max(1, longW / 4)});   // 休 15日窓>=~4
        int wk = (K > 1) ? 1 : 0;
        p.cons1.push_back({std::min(T, 14), wk, 2});  // 勤務シフト 14日窓>=2（golden の Dﾃ型）
    }
    int nC2 = ri(1, 4); for (int c = 0; c < nC2; c++) p.cons2.push_back({ri(0, K - 1), ri(1, T / 3 + 1)});
    int nC41 = ri(2, 5); for (int c = 0; c < nC41; c++) { int l = ri(0, 2); p.cons41.push_back({ri(0, G - 1), ri(1, K - 1), l, l + ri(0, 2)}); }
    int nC42 = ri(1, 4); for (int c = 0; c < nC42; c++) p.cons42.push_back({ri(0, G - 1), ri(1, K - 1), ri(0, G - 1), ri(1, K - 1)});
    int nC41s = ri(1, 3); for (int c = 0; c < nC41s; c++) { int l = ri(0, 2); p.cons41s.push_back({ri(0, Gs - 1), ri(1, K - 1), l, l + ri(0, 2)}); }
    int nC42s = ri(1, 3); for (int c = 0; c < nC42s; c++) p.cons42s.push_back({ri(0, Gs - 1), ri(1, K - 1), ri(0, Gs - 1), ri(1, K - 1)});
    // c3 families: mix of single-run and multi-shift sequences
    for (int c = 0; c < ri(1, 3); c++) p.cons3.push_back(mkC3({ri(1, K - 1), ri(1, K - 1)}));
    for (int c = 0; c < ri(1, 3); c++) { int s = ri(1, K - 1); p.cons3n.push_back(mkC3({s, s, s})); }   // forbidden triple-run
    for (int c = 0; c < ri(1, 2); c++) { int s = ri(1, K - 1); p.cons3m.push_back(mkC3({s, s})); }        // single-run want
    for (int c = 0; c < ri(1, 2); c++) p.cons3mn.push_back(mkC3({ri(1, K - 1), 0}));
    // [c3 窓マッチのビット化(3.174.0)を明示的に踏む] 多シフト D>=3 の非forbidden/forbidden を追加。
    //   これらは singleRun=false の窓マッチ経路＝新しい popcount パスの主対象。
    if (K >= 4) {
        p.cons3.push_back(mkC3({ri(1, K - 1), ri(1, K - 1), ri(1, K - 1)}));                       // 非forbidden 多シフト D=3
        p.cons3mn.push_back(mkC3({ri(1, K - 1), ri(1, K - 1), ri(1, K - 1), ri(1, K - 1)}));       // forbidden 多シフト D=4
    }
    finalizeProblem(p);
    return p;
}

static std::vector<int> randomBoard(const MagiProblem& p, std::mt19937_64& rng) {
    std::vector<int> a((size_t)p.S * p.T);
    for (int i = 0; i < p.S; i++) { const auto& b = p.bucket[p.sgrp[i]];
        for (int j = 0; j < p.T; j++) a[(size_t)i * p.T + j] = b[rng() % b.size()]; }
    return a;
}

// [実データ形状] 盤面に -1(未割当) と 非canDo 値を混ぜる。normalizeSchedule は範囲外セルを -1 に
// 写像し（MirrorCore.normalizeSchedule）、範囲内なら非canDo値もそのまま残す。どちらも実ランタイムで
// SaChunk へ正当に到達する（undo バッファは旧値=-1/非canDo をそのまま復元する）。
static void injectRealisticNoise(const MagiProblem& p, std::vector<int>& a, std::mt19937_64& rng) {
    const size_t n = a.size();
    for (size_t c = 0; c < n; c++) {
        uint64_t r = rng() % 100;
        if (r < 3) a[c] = -1;                        // ~3% 未割当
        else if (r < 5) a[c] = (int)(rng() % (uint64_t)p.K);   // ~2% 任意（非canDoを含む）
    }
}

// 共有パリティループ: 乱択1セル手（bucket からの割当）＋25% リバート（旧値へ戻す＝-1/非canDo の
// 復元も踏む）。delta==full を1手ごとに照合。
static void runParityLoop(const MagiProblem& p, const std::vector<int>& board, const char* name,
                          uint64_t seed, bool bits, int moves,
                          long long& totalMoves, long long& mismatches) {
    std::mt19937_64 rng(seed * 104729ULL + 1);
    SaChunk st(p, board.data(), seed * 2654435761ULL);
    st.useBits = st.useBits && bits;
    if (st.score != fullEvalCombined(p, st.a.data())) {
        mismatches++;
        printf("INIT MISMATCH %s seed=%llu\n", name, (unsigned long long)seed);
    }
    for (int m = 0; m < moves; m++) {
        int i = (int)(rng() % (uint64_t)p.S), j = (int)(rng() % (uint64_t)p.T);
        const auto& b = p.bucket[p.sgrp[i]];
        if (b.empty()) continue;
        int nw = b[rng() % b.size()];
        int old = st.a[(size_t)i * p.T + j];
        st.deltaApply(i, j, nw);
        totalMoves++;
        long long full = fullEvalCombined(p, st.a.data());
        if (st.score != full) {
            mismatches++;
            if (mismatches <= 8) printf("MISMATCH %s seed=%llu move=%d (i=%d j=%d %d->%d) delta=%lld full=%lld\n",
                                        name, (unsigned long long)seed, m, i, j, old, nw, st.score, full);
        }
        // 25% chance revert (exercise reversibility incl. restoring -1 / non-canDo)
        if ((rng() & 3) == 0) { st.deltaApply(i, j, old); totalMoves++;
            long long f2 = fullEvalCombined(p, st.a.data());
            if (st.score != f2) { mismatches++; if (mismatches <= 8) printf("REVERT MISMATCH %s (restore %d)\n", name, old); }
        }
    }
}

// [3.357.0/言語跨ぎパリティ] Kotlin の Evaluator.fullEval が出した値（app/src/test/resources/
//   golden_eval_expected.txt・Kotlin 側は NativeParityFixtureTest が同じファイルを検証する）と
//   C++ の fullEvalParts を突き合わせる。
//
//   これまでの CI は **C++ scalar vs C++ bit-op** しか照合しておらず、「Kotlin だけ／C++ だけを
//   直した」意味的乖離（3.345.0 の weekly 定義変更がまさにその形）は**どちらの経路でも検出できなかった**。
//   実機の番兵は捕まえるが、そのときネイティブは黙って無効化される＝速度が落ちるだけで気づけない。
//   両側を1つの数字に固定することで、片側だけの変更が必ず CI で落ちる。
static bool checkCrossLanguage(const MagiProblem& p, const std::vector<int>& board, const char* expectPath) {
    std::ifstream f(expectPath);
    if (!f) { printf("CROSS: cannot open %s\n", expectPath); return false; }
    long long expHard = -1, expSoft = -1;
    std::string line;
    while (std::getline(f, line)) {
        if (line.rfind("hard=", 0) == 0) expHard = atoll(line.c_str() + 5);
        else if (line.rfind("soft=", 0) == 0) expSoft = atoll(line.c_str() + 5);
    }
    if (expHard < 0 || expSoft < 0) { printf("CROSS: bad expectation file %s\n", expectPath); return false; }
    long long out[2];
    fullEvalParts(p, board.data(), out);
    const bool ok = (out[0] == expHard && out[1] == expSoft);
    printf("CROSS Kotlin-vs-C++ (%s): C++ hard=%lld soft=%lld / Kotlin hard=%lld soft=%lld -> %s\n",
           expectPath, out[0], out[1], expHard, expSoft, ok ? "MATCH" : "MISMATCH");
    return ok;
}

// [x8ygvy 923bf07 由来] **共有ネイティブハンドルの安全性を、読んで確認するのでなく実行で示す。**
//
// `SaOptimizer.run` は `nativeCreateProblem` のハンドルを1本だけ作り、最大8本の並列ワーカーが
// 同時に `nativeSaChunk` を呼ぶ。コメントは「read-only なのでスレッド安全」と主張しているが、
// コメントと実装が食い違うのはこのリポジトリで何度も起きている（HF77）。実際に N スレッドから
// 同一 MagiProblem を叩き、**逐次実行とビット単位で同じ結果**になることを確かめる。
// ThreadSanitizer 付きでビルドすれば、書き込み競合はここで検出される。
// 呼出は1箇所・常に kSharedHandleThreads 本（SaOptimizer.Params.workers の実運用上限と同じ）。
// パラメータ化を保つのは「TSAN専用ジョブが必要なら別本数で呼べる」将来の柔軟性のためで、
// 現状は名前付き定数を1箇所で参照する（呼出側で生の 8 を読む手間を無くすだけ）。
static constexpr int kSharedHandleThreads = 8;

static bool runSharedHandleConcurrency(const MagiProblem& p, const std::vector<int>& board,
                                       int threads, long long& mismatches) {
    struct Res { std::vector<int> cur, best; long long out[6]; };
    auto oneChunk = [&p, &board](uint64_t seed, Res& r) {
        r.cur = board; r.best = board;
        long long bestScore = fullEvalCombined(p, r.best.data());
        // ラダーは軽くてよい。ここで確かめたいのは「同じ p を並列に読んで壊れないか」であって
        // 探索の深さではない（深さはパリティループが担当）。ThreadSanitizer は重いラダーだと
        // 遅くなるが、この軽さなら計測上 TSAN ビルドでも通常ビルドと同程度（~50ms）で終わる。
        runSaChunk(p, r.cur.data(), r.best.data(), bestScore, seed, 1.0, 0.5, 0.5, 1, r.out);
    };
    // 逐次で基準を取る
    std::vector<Res> serial((size_t)threads);
    for (int t = 0; t < threads; t++) oneChunk((uint64_t)(t + 1) * 9176ULL, serial[(size_t)t]);
    // 同じ seed を並列で。p は共有＝ここで競合があれば結果がずれるか TSAN が鳴る。
    std::vector<Res> par((size_t)threads);
    std::vector<std::thread> ts;
    ts.reserve((size_t)threads);
    // スレッド生成を全部終えてから join する構造のため、途中で emplace_back が例外を投げると
    // 既に起動済みの std::thread が joinable のまま巻き戻りで破棄され std::terminate() する。
    // 生成側で捕捉し、既に起動済みの分は join してから再送出する（クラッシュでなく例外で終わる）。
    try {
        for (int t = 0; t < threads; t++)
            ts.emplace_back([&par, &oneChunk, t] { oneChunk((uint64_t)(t + 1) * 9176ULL, par[(size_t)t]); });
    } catch (...) {
        for (auto& th : ts) if (th.joinable()) th.join();
        throw;
    }
    for (auto& th : ts) th.join();
    // out[0]=status（deltaApply の自己整合番兵）。serial/parallel が偶然同じ status!=0 に
    // なっても、cur/best は書き込まれていない（呼出元は破棄する契約）ため「一致」とは呼べない
    // ＝この検査本来の対象（自己整合性そのもの）を見逃さないよう status を明示的に含めて比較する。
    bool ok = true;
    for (int t = 0; t < threads && ok; t++) {
        const Res& s = serial[(size_t)t];
        const Res& q = par[(size_t)t];
        if (s.out[0] != 0 || q.out[0] != 0) {
            printf("SHARED-HANDLE x%d threads: SELF-CONSISTENCY FAIL (status serial=%lld par=%lld, thread %d)\n",
                   threads, s.out[0], q.out[0], t);
            ok = false;
            break;
        }
        ok = s.cur == q.cur && s.best == q.best && std::equal(std::begin(s.out), std::end(s.out), std::begin(q.out));
    }
    printf("SHARED-HANDLE x%d threads: %s\n", threads, ok ? "identical to serial" : "MISMATCH");
    if (!ok) mismatches++;
    return ok;
}

// [3.409.22] ネイティブ修復器が **need2 単独定義の被覆需要** を扱えるかの直接検証。
//   旧実装は `need1<=0 → continue`（destroyRepairDayAtN）/ `need1<0 → continue`（findCovOFixN）で
//   need1 未設定のセルを丸ごと素通りしており、**評価器は covU/covO を計上するのに修復器はその枠を
//   狙う候補を作れない**という乖離になっていた（Kotlin 側は 3.379.0/3.369.0 で covUCell/covOCell へ
//   委譲済みで、この C++ ミラーだけ取り残されていた）。パリティ番兵はスコアの一致しか見ないため
//   この乖離を検出できない＝ここで直接叩く。
static int runNeed2OnlyRepairTest() {
    int failures = 0;
    // S=4, T=3, K=2（0=休, 1=A）, G=1。全員 A を担当可能。
    MagiProblem p;
    p.S = 4; p.T = 3; p.K = 2; p.G = 1; p.restIdx = 0; p.dow0 = 0; p.use2 = true;
    p.sgrp.assign(p.S, 0); p.ssk.assign(p.S, -1);
    p.canDo.assign((size_t)p.S * p.K, 1);
    p.wish.assign((size_t)p.S * p.T, -1);
    p.need1.assign((size_t)p.K * p.T, -1);      // ← need1 は全セル未設定
    p.need2.assign((size_t)p.K * p.T, -1);
    p.need2[(size_t)1 * p.T + 0] = 2;           // ← day0 の A だけ need2=2（P2 単独定義）
    p.rangeLo.assign((size_t)p.S * p.K, INT32_MIN);
    p.rangeHi.assign((size_t)p.S * p.K, INT32_MAX);
    p.apt.assign((size_t)p.S * p.K, -1);
    p.bucket.assign((size_t)p.G, {0, 1});
    finalizeProblem(p);

    // 前提の裏取り: 公式ヘルパは「誰も居ない day0 の A は 2 不足」と判定する。
    if (p.covUCell(1, 0, 0) != 2) { printf("REPAIR-TEST FAIL: covUCell 前提が崩れています\n"); failures++; }

    // (1) destroyRepairDayAtN: 全員休の盤面で day0 を修復 → A が need2 ぶん埋まること。
    std::vector<int> a((size_t)p.S * p.T, 0);   // 全セル休
    std::mt19937_64 rng(20260821);
    destroyRepairDayAtN(p, a.data(), 0, rng);
    int got = 0;
    for (int i = 0; i < p.S; i++) if (a[(size_t)i * p.T + 0] == 1) got++;
    if (got < 2) {
        printf("REPAIR-TEST FAIL: destroyRepairDayAtN が need2 単独定義の不足を埋めない (got=%d, 期待>=2)\n", got);
        failures++;
    }

    // (2) findCovOFixN: need2=2 の枠に3人＝公式 covOCell は過剰1。修復器がその日を見つけること。
    //     day を引くのは乱数なので、T=3 の全日について「過剰のある day0 を引いたら必ず検出する」を確認する。
    std::vector<int> b((size_t)p.S * p.T, 0);
    for (int i = 0; i < 3; i++) b[(size_t)i * p.T + 0] = 1;   // day0 の A に3人
    if (p.covOCell(1, 0, 3) != 1) { printf("REPAIR-TEST FAIL: covOCell 前提が崩れています\n"); failures++; }
    bool found = false;
    for (int trial = 0; trial < 200 && !found; trial++) {
        SaChunk st(p, b.data(), 4242 + trial);
        std::mt19937_64 r2(9000 + trial);
        Fix f = findCovOFixN(p, st, r2);
        if (f.ok() && f.j == 0 && b[(size_t)f.i * p.T + 0] == 1) found = true;
    }
    if (!found) {
        printf("REPAIR-TEST FAIL: findCovOFixN が need2 単独定義の過剰を検出しない\n");
        failures++;
    }
    printf("REPAIR need2-only: %s (day-repair got=%d/2, covO-fix %s)\n",
           failures == 0 ? "OK" : "FAILED", got, found ? "found" : "missed");
    return failures;
}

// [3.409.22] 修復器の候補順位に使う marginal cost が「全量再計算の差分」と厳密に一致するか。
//   weeklyMarginalN / fairMarginalN はどちらも**作業配列を一時的に書き換えて戻す**実装なので、
//   復元漏れや添字ずれがあると静かに誤った順位を返す（採否は checker が守るので勤務表は壊れないが、
//   候補選択が歪む＝パリティ番兵では捕まらない領域）。ここでは同じ盤面変更を実際に適用して
//   全量から測り直し、marginal がその差分と一致することを確認する。
static int runMarginalCostTest() {
    int failures = 0;
    MagiProblem p;
    p.S = 6; p.T = 14; p.K = 3; p.G = 2; p.restIdx = 0; p.dow0 = 3; p.use2 = false;
    p.sgrp.assign(p.S, 0);
    for (int i = 3; i < p.S; i++) p.sgrp[i] = 1;
    p.ssk.assign(p.S, -1);
    p.canDo.assign((size_t)p.S * p.K, 1);
    p.wish.assign((size_t)p.S * p.T, -1);
    p.need1.assign((size_t)p.K * p.T, -1);
    p.need2.assign((size_t)p.K * p.T, -1);
    p.rangeLo.assign((size_t)p.S * p.K, INT32_MIN);
    p.rangeHi.assign((size_t)p.S * p.K, INT32_MAX);
    p.apt.assign((size_t)p.S * p.K, -1);
    p.bucket.assign((size_t)p.G, {0, 1, 2});
    finalizeProblem(p);

    std::mt19937_64 rng(777);
    std::vector<int> a((size_t)p.S * p.T);
    for (auto& v : a) v = (int)(rng() % (uint64_t)p.K);

    auto weeklyOf = [&](const std::vector<int>& bd, int i) {
        std::vector<int> wd((size_t)p.K * 7, 0);
        for (int jj = 0; jj < p.T; jj++) {
            int k = bd[(size_t)i * p.T + jj];
            if (k >= 0 && k < p.K) wd[(size_t)k * 7 + (size_t)((p.dow0 + jj) % 7)]++;
        }
        long long d = 0;
        for (int k = 0; k < p.K; k++) d += weeklyDevOfBucket(&wd[(size_t)k * 7]);
        return d;
    };
    auto fairOf = [&](const std::vector<int>& bd) {
        std::vector<int> counts((size_t)p.S * p.K, 0);
        for (int i = 0; i < p.S; i++)
            for (int jj = 0; jj < p.T; jj++) {
                int k = bd[(size_t)i * p.T + jj];
                if (k >= 0 && k < p.K) counts[(size_t)i * p.K + k]++;
            }
        long long d = 0;
        for (int g = 0; g < p.G; g++) {
            const auto& mem = p.members[g];
            if ((int)mem.size() < 2) continue;
            for (int k = 0; k < p.K; k++) {
                if (!p.bucketHas[(size_t)g * p.K + k]) continue;
                int sum = 0;
                for (int x : mem) sum += counts[(size_t)x * p.K + k];
                long long tgt = jround((double)sum / (double)mem.size());
                for (int x : mem) d += std::llabs((long long)counts[(size_t)x * p.K + k] - tgt);
            }
        }
        return d;
    };

    int checked = 0, nonZeroWeekly = 0, nonZeroFair = 0;
    for (int trial = 0; trial < 400; trial++) {
        int i = (int)(rng() % (uint64_t)p.S);
        int j = (int)(rng() % (uint64_t)p.T);
        int oldK = a[(size_t)i * p.T + j];
        int newK = (int)(rng() % (uint64_t)p.K);
        if (newK == oldK) continue;
        checked++;

        std::vector<int> wd((size_t)p.K * 7, 0);
        for (int jj = 0; jj < p.T; jj++) {
            int k = a[(size_t)i * p.T + jj];
            if (k >= 0 && k < p.K) wd[(size_t)k * 7 + (size_t)((p.dow0 + jj) % 7)]++;
        }
        std::vector<int> wdCopy = wd;
        std::vector<int> counts((size_t)p.S * p.K, 0);
        for (int s2 = 0; s2 < p.S; s2++)
            for (int jj = 0; jj < p.T; jj++) {
                int k = a[(size_t)s2 * p.T + jj];
                if (k >= 0 && k < p.K) counts[(size_t)s2 * p.K + k]++;
            }
        std::vector<int> countsCopy = counts;
        std::vector<int> grpTotal((size_t)p.G * p.K, 0);
        for (int s2 = 0; s2 < p.S; s2++)
            for (int k = 0; k < p.K; k++)
                grpTotal[(size_t)p.sgrp[s2] * p.K + k] += counts[(size_t)s2 * p.K + k];

        const int bucket = (p.dow0 + j) % 7;
        long long mWeekly = weeklyMarginalN(wd.data(), p.K, bucket, oldK, newK);
        long long mFair = fairMarginalN(p, i, oldK, -1, counts, grpTotal)
                        + fairMarginalN(p, i, newK, 1, counts, grpTotal);

        if (wd != wdCopy) { printf("MARGINAL-TEST FAIL: weeklyMarginalN が作業配列を戻していない\n"); failures++; break; }
        if (counts != countsCopy) { printf("MARGINAL-TEST FAIL: fairMarginalN が counts を戻していない\n"); failures++; break; }

        long long wBefore = weeklyOf(a, i), fBefore = fairOf(a);
        a[(size_t)i * p.T + j] = newK;
        long long wAfter = weeklyOf(a, i), fAfter = fairOf(a);
        a[(size_t)i * p.T + j] = oldK;

        if (mWeekly != wAfter - wBefore) {
            printf("MARGINAL-TEST FAIL: weekly %lld != 全量差分 %lld\n", mWeekly, wAfter - wBefore);
            failures++; break;
        }
        if (mFair != fAfter - fBefore) {
            printf("MARGINAL-TEST FAIL: fair %lld != 全量差分 %lld\n", mFair, fAfter - fBefore);
            failures++; break;
        }
        if (mWeekly != 0) nonZeroWeekly++;
        if (mFair != 0) nonZeroFair++;
    }
    // 全部ゼロなら「一致」に意味が無い（何も測っていない緑）。実際に効いていることまで見る。
    if (failures == 0 && (nonZeroWeekly == 0 || nonZeroFair == 0)) {
        printf("MARGINAL-TEST FAIL: 非ゼロの marginal が観測されず検証が空振り (weekly=%d fair=%d)\n",
               nonZeroWeekly, nonZeroFair);
        failures++;
    }

    // destroyRepairViolationsN が希望固定セルを触らず、担当可シフトだけを書くこと。
    //   canDo は「群 bucket に含まれるか」から導かれる（NativeEval の flatten がそう詰める）ので、
    //   bucket と canDo は必ず揃えて作る。片方だけ弄ると実在しない問題になり、テストが
    //   自分の作った不整合を捕まえてしまう（初版で実際に踏んだ）。
    p.wish[(size_t)0 * p.T + 0] = 1;              // 職員0/day0 を希望固定
    p.bucket[1] = {0, 1};                          // 群1（職員3-5）はシフト2を担当しない
    for (int i = 0; i < p.S; i++)
        for (int k = 0; k < p.K; k++)
            p.canDo[(size_t)i * p.K + k] = (p.sgrp[i] == 1 && k == 2) ? 0 : 1;
    finalizeProblem(p);
    // 初期盤面も担当可だけで作る（repair は最大8セルしか触らないので、触っていないセルの
    //   違反を数えると「repair のせい」に見えてしまう）。
    std::vector<int> vb = a;
    for (int i = 0; i < p.S; i++)
        for (int j = 0; j < p.T; j++)
            if (!p.cd(i, vb[(size_t)i * p.T + j])) vb[(size_t)i * p.T + j] = 0;
    std::vector<int> cells;
    for (int i = 0; i < p.S; i++) for (int j = 0; j < p.T; j += 3) cells.push_back(i * p.T + j);
    int touched = 0;
    for (int trial = 0; trial < 50; trial++) {
        std::vector<int> bd = vb;
        std::mt19937_64 r3(31337 + trial);
        destroyRepairViolationsN(p, bd.data(), cells, r3);
        if (bd[0] != vb[0]) { printf("MARGINAL-TEST FAIL: 希望固定セルが変更された\n"); failures++; break; }
        bool bad = false;
        for (int i = 0; i < p.S && !bad; i++)
            for (int j = 0; j < p.T; j++) {
                if (bd[(size_t)i * p.T + j] != vb[(size_t)i * p.T + j]) touched++;
                if (!p.cd(i, bd[(size_t)i * p.T + j])) { bad = true; break; }
            }
        if (bad) { printf("MARGINAL-TEST FAIL: 担当外シフトが割り当てられた\n"); failures++; break; }
    }
    // 1セルも動かないなら上の2つは何も検証していない（空振りの緑）。
    if (failures == 0 && touched == 0) {
        printf("MARGINAL-TEST FAIL: destroyRepairViolationsN が1セルも動かさず検証が空振り\n");
        failures++;
    }

    // **operator が実際に weekly/fair を費用へ入れているか**を外から観測する。
    //   個人回数の上下限も apt も設定しないので staffCountPenaltyAtN は常に 0＝候補は
    //   weekly+fair だけで決まる。ここを外すと全候補が同点になり reservoir 抽選＝
    //   最小でない候補も選ばれる（＝ヘルパを持っていても呼んでいなければ落ちる）。
    int decided = 0;
    {
        MagiProblem q;
        q.S = 2; q.T = 14; q.K = 3; q.G = 1; q.restIdx = 0; q.dow0 = 0; q.use2 = false;
        q.sgrp.assign(q.S, 0); q.ssk.assign(q.S, -1);
        q.canDo.assign((size_t)q.S * q.K, 1);
        q.wish.assign((size_t)q.S * q.T, -1);
        q.need1.assign((size_t)q.K * q.T, -1);
        q.need2.assign((size_t)q.K * q.T, -1);
        q.rangeLo.assign((size_t)q.S * q.K, INT32_MIN);
        q.rangeHi.assign((size_t)q.S * q.K, INT32_MAX);
        q.apt.assign((size_t)q.S * q.K, -1);
        q.bucket.assign((size_t)q.G, {0, 1, 2});
        finalizeProblem(q);

        auto costOf = [&](const std::vector<int>& bd) {
            std::vector<int> wd((size_t)q.K * 7, 0);
            for (int jj = 0; jj < q.T; jj++) {
                int k = bd[(size_t)0 * q.T + jj];
                if (k >= 0 && k < q.K) wd[(size_t)k * 7 + (size_t)((q.dow0 + jj) % 7)]++;
            }
            long long d = 0;
            for (int k = 0; k < q.K; k++) d += weeklyDevOfBucket(&wd[(size_t)k * 7]);
            std::vector<int> counts((size_t)q.S * q.K, 0);
            for (int i = 0; i < q.S; i++)
                for (int jj = 0; jj < q.T; jj++) {
                    int k = bd[(size_t)i * q.T + jj];
                    if (k >= 0 && k < q.K) counts[(size_t)i * q.K + k]++;
                }
            for (int k = 0; k < q.K; k++) {
                int sum = counts[k] + counts[(size_t)q.K + k];
                long long tgt = jround((double)sum / 2.0);
                d += std::llabs((long long)counts[k] - tgt) + std::llabs((long long)counts[(size_t)q.K + k] - tgt);
            }
            return d;
        };

        std::mt19937_64 gen(4242);
        for (int trial = 0; trial < 4000 && decided < 30 && failures == 0; trial++) {
            std::vector<int> bd((size_t)q.S * q.T);
            for (auto& v : bd) v = (int)(gen() % (uint64_t)q.K);
            int j0 = (int)(gen() % (uint64_t)q.T);
            int old = bd[j0];
            // 候補（old 以外）の全量費用を測り、**厳密に唯一の最小**があるときだけ判定に使う。
            long long best = INT64_MAX; int bestK = -1; int ties = 0;
            for (int k = 0; k < q.K; k++) {
                if (k == old) continue;
                std::vector<int> t = bd; t[j0] = k;
                long long c = costOf(t);
                if (c < best) { best = c; bestK = k; ties = 1; }
                else if (c == best) ties++;
            }
            if (ties != 1) continue;
            std::vector<int> t0 = bd; t0[j0] = old;
            if (costOf(t0) <= best) continue;   // 動かさない方が良い局面は対象外
            decided++;
            std::vector<int> out = bd;
            std::mt19937_64 r4(555 + trial);
            destroyRepairViolationsN(q, out.data(), std::vector<int>{j0}, r4);
            if (out[j0] != bestK) {
                printf("MARGINAL-TEST FAIL: 候補選択が weekly+fair の最小と一致しない "
                       "(選んだ=%d 期待=%d j=%d)\n", out[j0], bestK, j0);
                failures++;
            }
        }
        if (failures == 0 && decided < 30) {
            printf("MARGINAL-TEST FAIL: 判定に使える局面が %d 件しか作れず検証が空振り\n", decided);
            failures++;
        }
    }
    printf("MARGINAL cost parity: %s (checked=%d weekly非ゼロ=%d fair非ゼロ=%d 再割当セル=%d 候補選択=%d局面)\n",
           failures == 0 ? "OK" : "FAILED", checked, nonZeroWeekly, nonZeroFair, touched, decided);
    return failures;
}

// [3.409.23/監査G3] 制約 index の入口検証。ここで拒否しないと SaChunk のビット経路が
//   grpMask[(size_t)c.g]（負値→巨大 size_t）と dayShiftMask[j*K + c.s]（c.s>=K は隣日・末尾越え）を
//   無検査で引く。天井は SIGSEGV で**2層番兵では捕捉できない**種類なので、入口で閉じたことを直接確かめる。
static int runConsIndexGuardTest() {
    int failures = 0;
    auto base = []() {
        MagiProblem p;
        p.S = 3; p.T = 5; p.K = 2; p.G = 2; p.restIdx = 0; p.dow0 = 0; p.use2 = false;
        p.sgrp.assign(p.S, 0); p.ssk.assign(p.S, 0);
        p.canDo.assign((size_t)p.S * p.K, 1);
        p.wish.assign((size_t)p.S * p.T, -1);
        p.need1.assign((size_t)p.K * p.T, -1);
        p.need2.assign((size_t)p.K * p.T, -1);
        p.rangeLo.assign((size_t)p.S * p.K, INT32_MIN);
        p.rangeHi.assign((size_t)p.S * p.K, INT32_MAX);
        p.apt.assign((size_t)p.S * p.K, -1);
        p.bucket.assign((size_t)p.G, {0, 1});
        finalizeProblem(p);
        return p;
    };
    struct Case { const char* name; bool expectValid; };
    // 正当: 群 id が職員数・G を超えていても buildGroupMasks が長さを合わせるので受け入れる
    //   （上限を課すと「職員数より多いスキル群」を持つ正当なデータを誤って拒否する）。
    {
        MagiProblem p = base();
        p.cons41.push_back({1, 1, 0, 2});
        p.cons41s.push_back({7, 0, 0, 3});
        p.cons42.push_back({0, 0, 1, 1});
        p.cons42s.push_back({9, 1, 3, 0});
        // [3.442.0/M4] cons1/cons2 も正当な値なら通す（Kotlin の Problem 構築と同じ意味論）。
        p.cons1.push_back({3, 1, 2});
        p.cons2.push_back({0, 4});
        if (!consIndicesValidN(p)) { printf("CONS-GUARD FAIL: 正当な制約を拒否した\n"); failures++; }
    }
    // 不正: 負の群 id / 範囲外シフト id を4族それぞれで拒否する。
    // [3.442.0/M4] cons1.si は rowMask/staffForShift/applyCell(=ssn への書込) の index、
    //   cons2.si は ssn の index。d1<=0 は bit 経路 `1ULL << d1` のシフト量が負＝UB。
    const char* names[] = {"cons41.g<0", "cons41.s>=K", "cons41s.s<0",
                           "cons42.g2<0", "cons42.s1>=K", "cons42s.s2>=K",
                           "cons1.si<0", "cons1.si>=K", "cons1.d1<=0", "cons1.d2<=0",
                           "cons2.si>=K", "cons2.c<=0"};
    for (int c = 0; c < 12; c++) {
        MagiProblem p = base();
        switch (c) {
            case 0: p.cons41.push_back({-1, 0, 0, 1}); break;
            case 1: p.cons41.push_back({0, p.K, 0, 1}); break;
            case 2: p.cons41s.push_back({0, -1, 0, 1}); break;
            case 3: p.cons42.push_back({0, 0, -1, 1}); break;
            case 4: p.cons42.push_back({0, p.K, 0, 1}); break;
            case 5: p.cons42s.push_back({0, 0, 0, p.K}); break;
            case 6: p.cons1.push_back({3, -1, 1}); break;
            case 7: p.cons1.push_back({3, p.K, 1}); break;
            case 8: p.cons1.push_back({0, 1, 1}); break;
            case 9: p.cons1.push_back({3, 1, 0}); break;
            case 10: p.cons2.push_back({p.K, 1}); break;
            case 11: p.cons2.push_back({0, 0}); break;
        }
        if (consIndicesValidN(p)) {
            printf("CONS-GUARD FAIL: %s を受け入れた\n", names[c]);
            failures++;
        }
    }
    printf("CONS index guard: %s\n", failures == 0 ? "OK" : "FAILED");
    return failures;
}

int main(int argc, char** argv) {
    long long totalMoves = 0, mismatches = 0;
    // --shared-only は共有ハンドルの競合検査だけを走らせる（ThreadSanitizer 用）。
    //   パリティループは TSAN 下では桁違いに遅く、そのままでは競合検査に到達しない。
    bool sharedOnly = false;
    for (int ai = 1; ai < argc; ai++)
        if (strcmp(argv[ai], "--shared-only") == 0) sharedOnly = true;

    // --expect=<path> は言語跨ぎパリティの期待値ファイル。複数指定でき、flat 引数と
    //   **出現順**で対応づける（k 番目の flat ⇔ k 番目の --expect）。CI は順序を制御するので
    //   `magi_host a.flat b.flat --expect=a.exp --expect=b.exp` で1回のベンチ実行のまま
    //   複数の実データ形状を同時に言語跨ぎ照合できる（旧: --expect は単一で全 flat が共有していた）。
    std::vector<const char*> expects;
    for (int ai = 1; ai < argc; ai++)
        if (strncmp(argv[ai], "--expect=", 9) == 0) expects.push_back(argv[ai] + 9);

    // --expect は flat 引数と出現順で1:1対応する契約。件数がずれると、対応の無い flat 側は
    // 言語跨ぎ照合が無警告でスキップされる（例: 将来フィクスチャを追加して対応する --expect を
    // 書き忘れると、新フィクスチャだけ照合されないまま「MATCH」扱いの隣でCIが通り続ける）。
    // 件数不一致を事前に数えて fail-loud にする。
    if (!expects.empty()) {
        if (sharedOnly) {
            // --shared-only は --expect（言語跨ぎ照合）を一切見ない別モードのため、
            // 両方渡しても黙って --expect 側を無視していた。無言スキップは罠になるので明示する。
            printf("note: --shared-only ignores --expect= (%zu given) — only the concurrency "
                   "check runs in this mode.\n", expects.size());
        } else {
            int flatCount = 0;
            for (int ai = 1; ai < argc; ai++)
                if (strncmp(argv[ai], "--", 2) != 0) flatCount++;
            if ((int)expects.size() != flatCount) {
                printf("--expect= count (%zu) does not match flat-file argument count (%d) — "
                       "each flat file needs exactly one --expect= in the same order, or none.\n",
                       expects.size(), flatCount);
                return 2;
            }
        }
    }

    // ---- 実データ問題（flat ファイル引数）: 素の盤面＋実運転ノイズ(-1/非canDo)入り盤面 ----
    int flatIdx = 0;
    for (int ai = 1; ai < argc; ai++) {
        if (strncmp(argv[ai], "--", 2) == 0) continue;
        MagiProblem p;
        std::vector<int> board;
        if (!loadFlat(argv[ai], p, board)) return 2;
        // [923bf07 由来] 共有ハンドル並列テスト（実データで）。p の形状に依らない構造的性質
        // （MagiProblem が読み取り専用で共有安全か）の検査なので、複数 flat 引数があっても
        // 最初の1件だけで十分＝2件目以降は同じことを別データで繰り返すだけの無駄になる。
        const char* expectPath = (flatIdx < (int)expects.size()) ? expects[flatIdx] : nullptr;
        const bool isFirstFlat = (flatIdx == 0);
        flatIdx++;
        if (isFirstFlat) runSharedHandleConcurrency(p, board, kSharedHandleThreads, mismatches);
        if (sharedOnly) continue;
        if (expectPath && !checkCrossLanguage(p, board, expectPath)) mismatches++;
        printf("REAL %s: S=%d T=%d K=%d G=%d rest=%d c1=%zu c2=%zu c41=%zu c42=%zu c3n=%zu\n",
               argv[ai], p.S, p.T, p.K, p.G, p.restIdx, p.cons1.size(), p.cons2.size(),
               p.cons41.size(), p.cons42.size(), p.cons3n.size());
        for (uint64_t seed = 1; seed <= 6; seed++) {
            runParityLoop(p, board, "real(as-is)", seed, seed % 2 == 0, 40000, totalMoves, mismatches);
            std::mt19937_64 nz(seed * 7717ULL);
            std::vector<int> noisy = board;
            injectRealisticNoise(p, noisy, nz);
            runParityLoop(p, noisy, "real(noisy -1/nonCanDo)", seed, seed % 2 == 0, 40000, totalMoves, mismatches);
        }
    }

    if (sharedOnly) {
        // flatIdx==0 means the concurrency check never ran (no flat file argument was
        // given) — without that, mismatches stays at its initial 0 and we'd silently
        // report a clean pass despite exercising zero threads and zero code. Fail loudly
        // instead, since this flag's whole purpose is a ThreadSanitizer-verified check.
        //
        // This guard is deliberately scoped to --shared-only rather than hoisted to a
        // generic "did any short-circuiting flag actually do something" check at the top
        // of main(): today there is exactly one such flag, and the non-shared-only path
        // is never vacuous (it always falls through to the synthetic-fixture loop below),
        // so a general mechanism would have no second caller to justify it (see this file's
        // own history: --shared-only's own guard was itself missing for a while — 64a8083 —
        // which is the concrete precedent for *why* this comment exists). If a second
        // early-exit flag is ever added, copy this pattern (own zero-work guard, own
        // message) rather than reaching for shared machinery until there are ≥2 real
        // instances of the same shape to factor out.
        if (flatIdx == 0) {
            printf("SHARED-ONLY: no flat file argument given — nothing was checked\n");
            return 2;
        }
        printf("SHARED-ONLY: %lld mismatches\n", mismatches);
        return mismatches == 0 ? 0 : 1;
    }

    // ---- 合成フィクスチャ ----
    struct Dim { int S, T, K, G; bool use2; const char* name; };
    Dim dims[] = {
        {10, 31, 6, 3, true,  "real-ish 10x31 K6 (P1+P2)"},
        {10, 31, 6, 3, false, "10x31 K6 (P1 only)"},
        {20, 31, 12, 4, true, "20x31 K12"},
        {8,  28, 5, 2, true,  "8x28 K5"},
        {40, 62, 20, 6, true, "40x62 K20 (large, still <=64)"},
    };
    for (const Dim& d : dims) {
        for (uint64_t seed = 1; seed <= 6; seed++) {
            MagiProblem p = buildProblem(d.S, d.T, d.K, d.G, seed * 7919ULL, d.use2);
            std::mt19937_64 rng(seed * 104729ULL);
            std::vector<int> board = randomBoard(p, rng);
            // odd seed = scalar path, even seed = bit-op path
            runParityLoop(p, board, d.name, seed, seed % 2 == 0, 40000, totalMoves, mismatches);
            // [実データ形状] -1/非canDo 混入盤面でも同一パリティ（旧: 全セル canDo 割当のみで未照合）。
            std::vector<int> noisy = board;
            injectRealisticNoise(p, noisy, rng);
            runParityLoop(p, noisy, "noisy", seed + 100, seed % 2 == 1, 40000, totalMoves, mismatches);
        }
    }
    printf("\nPARITY: %lld moves checked (scalar + bit-op paths), %lld mismatches\n", totalMoves, mismatches);

    // ---- benchmark: raw deltaApply throughput (apply only, no full recompute) ----
    auto benchOne = [](bool forceScalar) -> double {
        MagiProblem p = buildProblem(10, 31, 6, 3, 42, true);
        std::mt19937_64 rng(12345);
        std::vector<int> board = randomBoard(p, rng);
        SaChunk st(p, board.data(), 777);
        st.useBits = st.useBits && !forceScalar;   // force scalar path for A/B
        const long long N = 20'000'000;
        auto t0 = std::chrono::high_resolution_clock::now();
        for (long long m = 0; m < N; m++) {
            int i = rng() % p.S, j = rng() % p.T;
            const auto& b = p.bucket[p.sgrp[i]];
            st.deltaApply(i, j, b[rng() % b.size()]);
        }
        auto t1 = std::chrono::high_resolution_clock::now();
        double ns = std::chrono::duration_cast<std::chrono::nanoseconds>(t1 - t0).count();
        return (double)N / (ns / 1e9);   // moves/sec
    };
    double scalar = benchOne(true);
    double bits   = benchOne(false);
    printf("BENCH deltaApply (10x31 K6): scalar %.2f M moves/s, bit-op %.2f M moves/s, speedup x%.2f\n",
           scalar / 1e6, bits / 1e6, bits / scalar);
    int repairFail = runNeed2OnlyRepairTest() + runMarginalCostTest() + runConsIndexGuardTest();
    return (mismatches == 0 && repairFail == 0) ? 0 : 1;
}
