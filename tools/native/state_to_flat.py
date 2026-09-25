#!/usr/bin/env python3
"""state JSON → host_parity_bench 用フラット問題ファイル変換。

Problem.kt の構築意味論と NativeEval.createHandle の平坦配列レイアウトを忠実に複製する。
（実データ形状のパリティフィクスチャ生成用。golden_state.json など実 state で
 SaChunk のΔ評価と fullEval の自己整合をホストで照合できるようにする＝backlog#6 の
 「実データ形状の網羅」残課題への対応。）

使い方: python3 tools/native/state_to_flat.py <state.json> <out.flat>

出力形式（空白区切り整数、行は自由・セクション毎に長さ前置）:
  MAGIFLAT1
  meta(7): S T K G restIdx dow0 use2
  staff(2S) / canDo(S*K) / wish(S*T) / needs(2*K*T) / ranges(3*S*K)
  cons(可変) / c3(可変) / bucket(可変)
  board(S*T): state.schedule を normalizeSchedule 相当（範囲外→-1）で正規化した盤面
"""
import json
import sys
from datetime import date

INT32_MIN = -2**31
INT32_MAX = 2**31 - 1


def to_int_or_none(v):
    """Kotlin の String.trim().toIntOrNull() 相当（JSON は int と str が混在するため両対応）。"""
    if v is None:
        return None
    if isinstance(v, bool):
        return None
    if isinstance(v, int):
        return v
    s = str(v).strip()
    if not s:
        return None
    try:
        return int(s)
    except ValueError:
        return None


def is_blank(v):
    return v is None or str(v).strip() == ""


def rest_index(shifts):
    """restIdx — StateParser.parse の role 解決＋MirrorCore.restShiftIndex、無ければ -1（NativeEval の `?: -1`）。

    明示の role（"rest"/"none"）が1つも無い JSON（旧JSON・"" で書いた保存）だけ記号"休"へ付ける。
    """
    for i, s in enumerate(shifts):
        if s.get("role") == "rest":
            return i
    if any(s.get("role") == "none" for s in shifts):
        return -1
    return next((i for i, s in enumerate(shifts) if s.get("kigou") == "休"), -1)


def main():
    if len(sys.argv) != 3:
        print(__doc__)
        sys.exit(2)
    st = json.load(open(sys.argv[1], encoding="utf-8"))
    shifts = st["shifts"]
    groups = st["groups"]
    staff = st["staff"]
    schedule = st["schedule"]
    S = len(staff)
    T = len(schedule[0]) if schedule else 0
    K = len(shifts)
    G = len(groups)
    use2 = 1 if st.get("use2Patterns") else 0

    def shift_idx(kigou):
        for i, s in enumerate(shifts):
            if s["kigou"] == kigou:
                return i
        return -1

    def group_idx(kigou):
        for i, g in enumerate(groups):
            if g["kigou"] == kigou:
                return i
        return -1

    skill_groups = st.get("skillGroups", [])

    def skill_group_idx(kigou):
        for i, g in enumerate(skill_groups):
            if g["kigou"] == kigou:
                return i
        return -1

    rest_idx = rest_index(shifts)
    # dow0 = ISO dayOfWeek(月=1..日=7) % 7 — Problem.dow0
    y, m, d = (int(x) for x in st["startDate"].split("-"))
    dow0 = (date(y, m, d).isoweekday()) % 7

    sgrp = [s["groupIdx"] for s in staff]
    ssk = [s.get("skillIdx", -1) for s in staff]  # 既定 -1＝未所属（StateParser と同じ）

    # bucket[g] = groupShift[g][k]==1 のシフト index
    gshift = st["groupShift"]
    bucket = []
    for g in range(G):
        row = gshift[g] if g < len(gshift) else []
        bucket.append([k for k in range(K) if k < len(row) and to_int_or_none(row[k]) == 1])

    can_do = [0] * (S * K)
    for i in range(S):
        for k in bucket[sgrp[i]]:
            can_do[i * K + k] = 1

    wish = [-1] * (S * T)
    for key, v in st.get("wishes", {}).items():
        p = key.split(",")
        i = to_int_or_none(p[0]) if len(p) > 0 else None
        j = to_int_or_none(p[1]) if len(p) > 1 else None
        if i is not None and j is not None and 0 <= i < S and 0 <= j < T:
            wish[i * T + j] = to_int_or_none(v) if to_int_or_none(v) is not None else -1

    # needAt: 日別override（非blank）→ シフト既定（blank→-1）
    need_day1 = st.get("needDay1", {})
    need_day2 = st.get("needDay2", {})

    def need_at(k, j, p2):
        mp = need_day2 if p2 else need_day1
        v = mp.get(f"{k},{j}")
        if not is_blank(v):
            iv = to_int_or_none(v)
            if iv is not None:
                return iv
        d = shifts[k]["need2"] if p2 else shifts[k]["need1"]
        if is_blank(d):
            return -1
        iv = to_int_or_none(d)
        return iv if iv is not None else -1

    needs = [0] * (2 * K * T)
    for k in range(K):
        for j in range(T):
            needs[k * T + j] = need_at(k, j, False)
            needs[K * T + k * T + j] = need_at(k, j, True)

    range_lo = [INT32_MIN] * (S * K)
    range_hi = [INT32_MAX] * (S * K)
    for key, r in st.get("staffRange", {}).items():
        p = key.split(",")
        i = to_int_or_none(p[0]) if len(p) > 0 else None
        k = to_int_or_none(p[1]) if len(p) > 1 else None
        if i is None or k is None or not (0 <= i < S and 0 <= k < K):
            continue
        lo = to_int_or_none(r.get("lo"))
        hi = to_int_or_none(r.get("hi"))
        # [3.509.1] 負数は未設定扱い — Problem.rangeLo/Hi
        lo = lo if lo is not None and lo >= 0 else None
        hi = hi if hi is not None and hi >= 0 else None
        if lo is not None:
            range_lo[i * K + k] = lo
        if hi is not None:
            range_hi[i * K + k] = hi

    # apt: 群目標→個人展開（canDoゲート＋staffRange[lo,hi]クランプ）— Problem.apt
    gsa = st.get("groupShiftApt", [])
    apt = [-1] * (S * K)
    for i in range(S):
        g = sgrp[i]
        row = gsa[g] if g < len(gsa) else None
        if row is None:
            continue
        can_k = bucket[g] if g < len(bucket) else []
        for k in range(K):
            t = to_int_or_none(row[k]) if k < len(row) else None
            if t is None or t < 0 or k not in can_k:
                continue
            # [3.509.0/決定 D9] 個人の下限または上限がある組には群目標を適用しない — Problem.apt
            if range_lo[i * K + k] != INT32_MIN or range_hi[i * K + k] != INT32_MAX:
                continue
            # [3.508.0] 到達範囲クランプ — Problem.apt と同式（希望固定込みの実効下限/上限の他シフト合計）
            def wish_cnt(kk):
                return sum(1 for j in range(T) if wish[i * T + j] == kk and kk in can_k)
            def eff_lo(kk):
                lo2 = 0 if range_lo[i * K + kk] == INT32_MIN else range_lo[i * K + kk]
                return max(lo2, wish_cnt(kk))
            def eff_hi(kk):
                placeable = kk in can_k and not (range_hi[i * K + kk] == 0 and kk != rest_idx)
                hi2 = (T if range_hi[i * K + kk] == INT32_MAX else range_hi[i * K + kk]) if placeable else 0
                return max(hi2, wish_cnt(kk))
            sum_hi = sum(eff_hi(k2) for k2 in range(K) if k2 != k)
            sum_lo = sum(eff_lo(k2) for k2 in range(K) if k2 != k)
            reach_lo = max(T - sum_hi, wish_cnt(k))
            reach_hi = T - sum_lo
            if reach_lo <= reach_hi:
                t = max(t, reach_lo)
                t = min(t, reach_hi)
            apt[i * K + k] = t

    # cons blob（NativeEval.createHandle と同順）
    cons = []
    c1 = []
    for c in st.get("cons1", []):
        d1 = to_int_or_none(c.get("day1")) or 0
        si = shift_idx(c.get("shiftKigou", ""))
        d2 = to_int_or_none(c.get("day2")) or 0
        if d1 > 0 and si >= 0 and d2 > 0:
            c1.append((d1, si, d2))
    cons.append(len(c1))
    for r in c1:
        cons.extend(r)
    c2 = []
    for c in st.get("cons2", []):
        si = shift_idx(c.get("shiftKigou", ""))
        ct = to_int_or_none(c.get("count")) or 0
        if si >= 0 and ct > 0:
            c2.append((si, ct))
    cons.append(len(c2))
    for r in c2:
        cons.extend(r)

    def resolve_c41(rows, gidx_fn):
        out = []
        for c in rows:
            gi = gidx_fn(c.get("groupKigou", ""))
            si = shift_idx(c.get("shiftKigou", ""))
            has_lo = not is_blank(c.get("l"))
            has_hi = not is_blank(c.get("u"))
            lo = to_int_or_none(c.get("l")) if has_lo else None
            hi = to_int_or_none(c.get("u")) if has_hi else None
            lo = lo if lo is not None else 0
            hi = hi if hi is not None else INT32_MAX
            if not has_hi:
                hi = INT32_MAX
            if not has_lo:
                lo = 0
            if gi >= 0 and si >= 0 and (has_lo or has_hi):
                out.append((gi, si, lo, hi))
        return out

    def resolve_c42(rows, gidx_fn):
        out = []
        for c in rows:
            g1 = gidx_fn(c.get("g1Kigou", ""))
            g2 = gidx_fn(c.get("g2Kigou", ""))
            s1 = shift_idx(c.get("s1Kigou", ""))
            s2 = shift_idx(c.get("s2Kigou", ""))
            if g1 >= 0 and g2 >= 0 and s1 >= 0 and s2 >= 0:
                out.append((g1, s1, g2, s2))
        return out

    c41 = resolve_c41(st.get("cons41", []), group_idx)
    cons.append(len(c41))
    for r in c41:
        cons.extend(r)
    c42 = resolve_c42(st.get("cons42", []), group_idx)
    cons.append(len(c42))
    for r in c42:
        cons.extend(r)
    c41s = resolve_c41(st.get("cons41s", []), skill_group_idx)
    cons.append(len(c41s))
    for r in c41s:
        cons.extend(r)
    c42s = resolve_c42(st.get("cons42s", []), skill_group_idx)
    cons.append(len(c42s))
    for r in c42s:
        cons.extend(r)
    # [3.542.0] cons3w: [n, (wishK, prevK)*]（Problem.cons3w: 両記号が解決できる行だけ）
    c3w = []
    for c in st.get("cons3w", []):
        x = shift_idx(c.get("wishKigou", ""))
        y = shift_idx(c.get("prevKigou", ""))
        if x >= 0 and y >= 0:
            c3w.append((x, y))
    cons.append(len(c3w))
    for r in c3w:
        cons.extend(r)

    # c3 blob: 各族 count, (len, seq...)*
    def resolve_c3(rows):
        out = []
        for row in rows:
            raw = row.get("pattern", [])
            # 最初の blank で切る（Problem.resolveC3）
            body = []
            for sym in raw:
                if is_blank(sym):
                    break
                body.append(sym)
            if not body:
                continue
            seq = []
            ok = True
            for sym in body:
                si = shift_idx(sym)
                if si < 0:
                    ok = False
                    break
                seq.append(si)
            if not ok or len(seq) > T:
                continue
            out.append(seq)
        return out

    c3blob = []
    for fam in ("cons3", "cons3n", "cons3m", "cons3mn"):
        rows = resolve_c3(st.get(fam, []))
        c3blob.append(len(rows))
        for seq in rows:
            c3blob.append(len(seq))
            c3blob.extend(seq)

    bucket_blob = []
    for g in range(G):
        b = bucket[g]
        bucket_blob.append(len(b))
        bucket_blob.extend(b)

    # board: normalizeSchedule 相当（範囲外・欠損・null→-1。-1 セルは実ランタイムで正当に現れる）
    board = []
    for i in range(S):
        row = schedule[i] if i < len(schedule) else []
        for j in range(T):
            k = to_int_or_none(row[j]) if j < len(row) else None
            k = k if k is not None else -1
            board.append(k if 0 <= k < K else -1)

    meta = [S, T, K, G, rest_idx, dow0, use2]
    staff_arr = sgrp + ssk

    with open(sys.argv[2], "w") as f:
        f.write("MAGIFLAT1\n")
        for arr in (meta, staff_arr, can_do, wish, needs,
                    range_lo + range_hi + apt,
                    cons, c3blob, bucket_blob, board):
            f.write(str(len(arr)) + "\n")
            f.write(" ".join(str(x) for x in arr) + "\n")
    n_neg = sum(1 for x in board if x < 0)
    print(f"wrote {sys.argv[2]}: S={S} T={T} K={K} G={G} rest={rest_idx} dow0={dow0} use2={use2} "
          f"c1={len(c1)} c2={len(c2)} c41={len(c41)} c42={len(c42)} board(-1)={n_neg}")


if __name__ == "__main__":
    main()
