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
static bool runSharedHandleConcurrency(const MagiProblem& p, const std::vector<int>& board,
                                       int threads, long long& mismatches) {
    struct Res { std::vector<int> cur, best; long long out[6]; };
    auto oneChunk = [&](uint64_t seed, Res& r) {
        r.cur = board; r.best = board;
        long long bestScore = fullEvalCombined(p, r.best.data());
        // ラダーは軽くてよい。ここで確かめたいのは「同じ p を並列に読んで壊れないか」であって
        // 探索の深さではない（深さはパリティループが担当）。ThreadSanitizer は10〜20倍遅く、
        // 重いラダーだと競合検査そのものが時間内に回らない。
        runSaChunk(p, r.cur.data(), r.best.data(), bestScore, seed, 1.0, 0.5, 0.5, 1, r.out);
    };
    // 逐次で基準を取る
    std::vector<Res> serial((size_t)threads);
    for (int t = 0; t < threads; t++) oneChunk((uint64_t)(t + 1) * 9176ULL, serial[(size_t)t]);
    // 同じ seed を並列で。p は共有＝ここで競合があれば結果がずれるか TSAN が鳴る。
    std::vector<Res> par((size_t)threads);
    std::vector<std::thread> ts;
    for (int t = 0; t < threads; t++)
        ts.emplace_back([&, t] { oneChunk((uint64_t)(t + 1) * 9176ULL, par[(size_t)t]); });
    for (auto& th : ts) th.join();
    bool ok = true;
    for (int t = 0; t < threads; t++) {
        if (serial[(size_t)t].cur != par[(size_t)t].cur ||
            serial[(size_t)t].best != par[(size_t)t].best) { ok = false; break; }
        for (int x = 0; x < 6; x++)
            if (serial[(size_t)t].out[x] != par[(size_t)t].out[x]) { ok = false; break; }
        if (!ok) break;
    }
    printf("SHARED-HANDLE x%d threads: %s\n", threads, ok ? "identical to serial" : "MISMATCH");
    if (!ok) mismatches++;
    return ok;
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

    // ---- 実データ問題（flat ファイル引数）: 素の盤面＋実運転ノイズ(-1/非canDo)入り盤面 ----
    int flatIdx = 0;
    for (int ai = 1; ai < argc; ai++) {
        if (strncmp(argv[ai], "--", 2) == 0) continue;
        MagiProblem p;
        std::vector<int> board;
        if (!loadFlat(argv[ai], p, board)) return 2;
        // [923bf07 由来] 共有ハンドル並列テスト（実データで）。--shared-only なら以降のパリティは省く。
        runSharedHandleConcurrency(p, board, 8, mismatches);
        if (sharedOnly) { flatIdx++; continue; }
        const char* expectPath = (flatIdx < (int)expects.size()) ? expects[flatIdx] : nullptr;
        flatIdx++;
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
    return mismatches == 0 ? 0 : 1;
}
