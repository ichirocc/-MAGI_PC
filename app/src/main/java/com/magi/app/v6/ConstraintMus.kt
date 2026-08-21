package com.magi.app.v6

/**
 * [Constraint IR + MUS（極小非充足コア）/ 3.272.0] 矛盾の「最小説明」エンジン。
 *
 * v8構想（Constraint IR + CP-SAT診断レーン + MUS/IIS）の第1段＝**依存ゼロ**の実装。設計原則:
 *  - **IRは宣言的メタデータのみ**（`Item` は scope とパラメータだけを持ち、評価器を持たない）。
 *    実行意味論は checker/evaluator/C++ の三面一致に据え置き＝第4の意味論を作らない。
 *    IRのドリフトは「誤った診断」（見えて無害）にしかならず「誤った勤務表」にはならない。
 *  - **不能性の判定は健全（sound）だが不完全な証明ルールに限定**: 発火＝真に矛盾。
 *    証明手段は (a) 厳密DP `SmartInitialScheduler.minDaysForFullCompliance`（窓ルールの真の最小
 *    必要日数）、(b) 鳩の巣論法（1職員の全シフト需要合計 > 期間日数）、(c) 二部マッチング
 *    （1日の必要人数が固定希望のもとで満たせるか＝クロス日制約を無視した緩和なので不能判定は健全）。
 *    見逃し（不完全）は安全側＝誤検知ゼロの設計（2b-2「false wallを出さない」と同方針）。
 *  - **deletion-based MUS**: 全体集合が証明可能に矛盾なら、1件ずつ外して証明が保てる限り除外→
 *    極小の「同時に満たせない組合せ」。各メンバーが緩和候補（IIS相当の提案）になる。
 *  - **ホットパス不変・読取専用・スコアリング不変**。出力は既存の SettingIssue チャネル
 *    （V6SanityPort 検査9）に流す。
 *
 * 既存の手彫り検査（2b-3=単一シフトの上限×窓 / 6b・6c=上限群の強制下限 / 検査3=下限合計）との
 * 分担: 本エンジンは**希望（wishLocked）が絡む矛盾**を担当する（既存検査はいずれも希望を扱わない）。
 * 提示側（V6SanityPort）は「コアに希望を含む矛盾」だけを出すことで重複ゼロにしている。
 */
object ConstraintMus {

    /** IR: ユーザーが実際に1件ずつ緩められる制約の宣言。評価器は持たない（設計原則）。 */
    sealed interface Item

    /** 実現可能な希望による固定（`Problem.wishLocked` 準拠＝実現不能な希望は含まれない）。 */
    data class WishPin(val staff: Int, val day: Int, val shift: Int) : Item

    /** 個人上限（staffRange hi、定義済みのみ）。 */
    data class RangeCap(val staff: Int, val shift: Int, val hi: Int) : Item

    /** 個人下限（staffRange lo、lo>0 のみ）。 */
    data class RangeFloor(val staff: Int, val shift: Int, val lo: Int) : Item

    /** 窓ルール（cons1、1行=1件。職員スコープの解析では canDo な職員にのみ適用される）。 */
    data class WindowRule(val shift: Int, val windowDays: Int, val minCount: Int) : Item

    /** 日別必要人数の実効下限（covUCell の意味論から逆算した「不足が出ない最小人数」）。 */
    data class DayNeed(val day: Int, val shift: Int, val need: Int) : Item

    data class StaffConflict(val staff: Int, val core: List<Item>)
    data class DayConflict(val day: Int, val core: List<Item>)

    /**
     * [性能] `minDaysForFullCompliance`（15日窓で数百msかかりうる重いDP）のプロセス全域キャッシュ。
     * key=(T, ルール部分集合) は入力の純関数＝安全にキャッシュ可能。buildGuidance はセル編集ごとに
     * 走る（makeUi の analyzeParallel 経由）ため、初回だけDPを払い2回目以降はほぼ0msにする。
     * ルール構成は滅多に変わらず部分集合の種類も高々 2^(シフト毎ルール数) で極小。
     * 値の null（計算不能）は MIN_VALUE 番兵で保持。
     *
     * [3.410.0/M-01] ただし「極小だから上限不要」は**期間日数 T やルールを編集し続ける限り単調に増える**
     * ことを見落としている（キーは (T, ルール部分集合)）。純粋な memo なので**捨てても正しさは不変**＝
     * 上限を超えたら丸ごと捨てる（再計算のコストだけ払う）。実運用の到達点はこの上限のはるか手前。
     */
    private const val MAX_CACHE_ENTRIES = 4096
    private val minDaysCache = java.util.concurrent.ConcurrentHashMap<String, Int>()
    private const val NULL_SENTINEL = Int.MIN_VALUE

    /** キャッシュ経由の `minDaysForFullCompliance`。検査2b-3と本エンジンで共用（同じ純関数）。 */
    fun cachedMinDays(t: Int, rules: List<Pair<Int, Int>>): Int? {
        if (rules.isEmpty()) return 0
        // [3.311.0] 旧: `first * 1000 + second` は (1,1000) と (2,0) が同じ 2000 になり衝突する。
        //   minCount>=1000 は不合理なデータだが入力可能で、これはプロセス全域キャッシュなので
        //   誤ヒットは誤った診断そのものになる。区切り文字を入れて衝突を構造的に消す。
        val key = t.toString() + "|" +
            rules.map { "${it.first}:${it.second}" }.sorted().joinToString(",")
        val c = minDaysCache[key]
        if (c != null) return if (c == NULL_SENTINEL) null else c
        val v = SmartInitialScheduler.minDaysForFullCompliance(t, rules)
        if (minDaysCache.size >= MAX_CACHE_ENTRIES) minDaysCache.clear()
        minDaysCache[key] = v ?: NULL_SENTINEL
        return v
    }

    /**
     * 職員1人スコープの証明可能矛盾を全職員について検出し、各々を極小コアへ縮約して返す。
     * 証明ルール（active な Item のみを使用＝MUSの意味論を保証）:
     *  A) シフト x の需要下限 demand_x = max(下限, 窓の厳密最小日数, xへの固定希望数) > 上限 cap_x
     *  B) 鳩の巣: Σ_x demand_x > T（各日はちょうど1シフト＝計T日しかない）
     *  C) 強制下限: 他の全担当シフトに上限がある場合、count_x ≥ T−Σ他上限 > cap_x
     */
    fun analyzeStaffConflicts(p: Problem): List<StaffConflict> {
        val out = ArrayList<StaffConflict>()
        for (i in 0 until p.S) {
            val allowed = p.allowedShiftsForStaff(i)
            if (allowed.isEmpty()) continue
            val universe = ArrayList<Item>()
            for (c in p.cons1) {
                if (c.shiftIdx !in 0 until p.K || c.day1 !in 1..p.T || c.day2 !in 1..c.day1) continue
                if (!p.canDo(i, c.shiftIdx)) continue
                universe.add(WindowRule(c.shiftIdx, c.day1, c.day2))
            }
            for (k in allowed) {
                val lo = p.rangeLo[i][k]
                if (lo != Int.MIN_VALUE && lo > 0) universe.add(RangeFloor(i, k, lo))
                val hi = p.rangeHi[i][k]
                if (hi != Int.MAX_VALUE) universe.add(RangeCap(i, k, hi))
            }
            for (j in 0 until p.T) if (p.wishLocked(i, j)) universe.add(WishPin(i, j, p.wish[i][j]))
            if (universe.isEmpty()) continue
            if (!staffProvablyInfeasible(p, allowed, universe)) continue
            val core = shrink(universe) { staffProvablyInfeasible(p, allowed, it) }
            if (core.isNotEmpty()) out.add(StaffConflict(i, core))
        }
        return out
    }

    /**
     * 日1つスコープの証明可能矛盾（固定希望のもとで必要人数の二部マッチングが不成立）を検出。
     * クロス日制約（c3n・回数等）を無視した緩和モデルなので「不能」判定は健全（実際はさらに厳しい）。
     */
    fun analyzeDayConflicts(p: Problem): List<DayConflict> {
        val out = ArrayList<DayConflict>()
        for (j in 0 until p.T) {
            val universe = ArrayList<Item>()
            for (k in 0 until p.K) {
                val eff = effectiveLowerBound(p, k, j)
                if (eff > 0) universe.add(DayNeed(j, k, eff))
            }
            if (universe.none { it is DayNeed }) continue
            for (i in 0 until p.S) if (p.wishLocked(i, j)) universe.add(WishPin(i, j, p.wish[i][j]))
            if (!dayProvablyInfeasible(p, universe)) continue
            val core = shrink(universe) { dayProvablyInfeasible(p, it) }
            if (core.isNotEmpty()) out.add(DayConflict(j, core))
        }
        return out
    }

    /** covUCell（source of truth）から逆算した「不足が出ない最小人数」。S人でも不足なら S+1。 */
    private fun effectiveLowerBound(p: Problem, k: Int, j: Int): Int {
        for (g in 0..p.S) if (p.covUCell(k, j, g) <= 0) return g
        return p.S + 1
    }

    /** deletion-based MUS。前提: infeasible(universe)==true。 */
    private fun shrink(universe: List<Item>, infeasible: (List<Item>) -> Boolean): List<Item> {
        val core = ArrayList(universe)
        var i = 0
        while (i < core.size) {
            val removed = core.removeAt(i)
            if (!infeasible(core)) {
                core.add(i, removed)
                i++
            }
            // 除去が成功したら同じ i 位置に次の要素が来るので i は進めない。
        }
        return core
    }

    private fun staffProvablyInfeasible(
        p: Problem,
        allowed: IntArray,
        items: List<Item>,
    ): Boolean {
        val caps = HashMap<Int, Int>()
        val floors = HashMap<Int, Int>()
        val rules = HashMap<Int, MutableList<WindowRule>>()
        val pins = HashMap<Int, Int>()
        for (it in items) when (it) {
            is RangeCap -> caps[it.shift] = it.hi
            is RangeFloor -> floors[it.shift] = it.lo
            is WindowRule -> rules.getOrPut(it.shift) { ArrayList() }.add(it)
            is WishPin -> pins[it.shift] = (pins[it.shift] ?: 0) + 1
            is DayNeed -> {}
        }
        // 窓ルールの厳密最小日数（rule部分集合ごとにmemo）。null（t>62等で計算不能）は 0 扱い＝
        //   下限を弱める方向なので健全（誤検知しない）。
        fun minWin(x: Int): Int {
            val rs = rules[x] ?: return 0
            if (rs.isEmpty()) return 0
            // null（t>62等で計算不能）は 0 扱い＝下限を弱める方向なので健全（誤検知しない）。
            return cachedMinDays(p.T, rs.map { it.windowDays to it.minCount }) ?: 0
        }
        fun demand(x: Int): Int = maxOf(floors[x] ?: 0, minWin(x), pins[x] ?: 0)
        // A) 需要下限 > 上限
        for (x in allowed) {
            val cap = caps[x] ?: continue
            if (demand(x) > cap) return true
        }
        // B) 鳩の巣: Σ需要下限 > T
        var sum = 0L
        for (x in allowed) sum += demand(x)
        if (sum > p.T) return true
        // C) 強制下限: 他の全担当シフトに上限がある場合のみ（未設定=無制限なので発火しない=保守的）
        for (x in allowed) {
            val cap = caps[x] ?: Int.MAX_VALUE
            var otherCapSum = 0L
            var allCapped = true
            for (y in allowed) {
                if (y == x) continue
                val cy = caps[y]
                if (cy == null) { allCapped = false; break }
                otherCapSum += minOf(cy, p.T)
            }
            if (!allCapped) continue
            val forcedMin = p.T - otherCapSum
            if (forcedMin > cap) return true
        }
        return false
    }

    private fun dayProvablyInfeasible(p: Problem, items: List<Item>): Boolean {
        val pinned = HashMap<Int, Int>()
        val slots = ArrayList<Int>()
        for (item in items) when (item) {
            is WishPin -> pinned[item.staff] = item.shift
            is DayNeed -> {
                val s = item.shift
                repeat(minOf(item.need, p.S + 1)) { slots.add(s) }
            }
            else -> {}
        }
        if (slots.isEmpty()) return false
        if (slots.size > p.S) return true   // 席数が職員数を超える＝固定希望に関わらず埋め不能（健全）
        val staffMatch = IntArray(p.S) { -1 }   // staff -> slot
        val slotMatch = IntArray(slots.size) { -1 }
        fun canServe(i: Int, shift: Int): Boolean {
            if (!p.canDo(i, shift)) return false
            val pin = pinned[i] ?: return true
            return pin == shift
        }
        fun tryAugment(slot: Int, visited: BooleanArray): Boolean {
            for (i in 0 until p.S) {
                if (visited[i] || !canServe(i, slots[slot])) continue
                visited[i] = true
                val cur = staffMatch[i]
                if (cur == -1 || tryAugment(cur, visited)) {
                    staffMatch[i] = slot
                    slotMatch[slot] = i
                    return true
                }
            }
            return false
        }
        var matched = 0
        for (s in slots.indices) if (tryAugment(s, BooleanArray(p.S))) matched++
        return matched < slots.size
    }
}
