#!/usr/bin/env python3
"""MAGI design lint — melta-ui 流「壊れたら気づくハーネス」（docs/DESIGN.md の禁止事項 P1-P4）。

一次ソース（MainActivity.MagiTheme / MagiTokens.kt）に集約されたトークンからの逸脱を静的検査する。
Compose/Kotlin をコンパイルせずに grep 相当で検出（サンドボックスでも走る）。advisory=既定は非 fatal。

使い方:
    python3 tools/design_lint.py            # 報告のみ（exit 0）
    python3 tools/design_lint.py --strict   # 違反があれば exit 1（CI で fail させたいとき）

検査:
    P1 純黒本文/背景     : ui/*.kt の Color(0xFF000000) / Color.Black（UD の MainActivity は対象外）
    P2 生 hex の散布      : ui/*.kt（MagiTokens.kt 除く）の Color(0x……) 直書き（baseline 監視）
    P3 重い影            : .shadow( / shadowElevation の使用
    P4 任意角丸          : ui/*.kt の RoundedCornerShape(<dp>) 直書き（pill=999/CircleShape は除外）
    P5 テンプレート食い込み: 文字列テンプレートで変数の直後に日本語が続く（Kotlin は日本語を識別子文字と
                            して扱うため `${'$'}count件` は `count件` という未定義シンボルになる＝必ずビルドが落ちる）
    P6 message severity  : message を書くのに messageIsError を書かない copy(…)
    P7 二重エンコード      : UTF-8 を Latin-1 として読んだ内容を保存した文字化け（追跡中の全テキスト）
    P8 DS の ✅ 誤表示     : magi_design_system.md が「実装済(✅)」と書いた共通コンポーネントの実在確認
"""
import os
import re
import subprocess
import sys
import unicodedata

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
UI_DIR = os.path.join(ROOT, "app/src/main/java/com/magi/app/ui")

RE_BLACK = re.compile(r"Color\(0xFF000000\)|Color\.Black")
RE_HEX = re.compile(r"Color\(0x[0-9A-Fa-f]{8}\)")
RE_SHADOW_MODIFIER = re.compile(r"\.shadow\(")
RE_SHADOW_ELEV = re.compile(r"shadowElevation\s*=\s*(\d+)\s*\.dp")
RE_SHAPE = re.compile(r"RoundedCornerShape\(\s*(\d+)\s*\.dp")
HEAVY_SHADOW_DP = 4  # 4dp 以上を「重い影」とみなす（軽い1-2dp の区切り影は許容）


def ui_files():
    if not os.path.isdir(UI_DIR):
        return []
    return sorted(os.path.join(UI_DIR, f) for f in os.listdir(UI_DIR) if f.endswith(".kt"))


# --- P5: 文字列テンプレートの日本語食い込み -----------------------------------------------
# Kotlin の識別子は「文字または数字またはアンダースコア」で、**日本語も「文字」に含まれる**。
# そのため `"${'$'}count件"` は変数 `count件` の参照と解釈され未定義シンボルになる（3.290.0・3.397.0 で
# 実際にビルドを落とした）。サンドボックスでは UI 層をコンパイルできず CI 1周（約1.5分）まで気づけない
# ため、push 前に静的検出できるようにする。**発生すれば必ずコンパイルエラー＝常に fatal。**
RE_TEMPLATE_REF = re.compile(r"\$[A-Za-z_][A-Za-z0-9_]*(.)")


def _is_kotlin_ident_char(ch):
    """Kotlin の識別子継続文字（isLetter || isDigit）に相当するか。ASCII は別途扱うのでここでは非 ASCII のみ。"""
    if ord(ch) < 128:
        return False
    cat = unicodedata.category(ch)
    return cat.startswith("L") or cat == "Nd"


def kotlin_files():
    roots = [os.path.join(ROOT, "app/src/main/java"), os.path.join(ROOT, "app/src/test/java")]
    out = []
    for root in roots:
        for dirpath, _, names in os.walk(root):
            out += [os.path.join(dirpath, n) for n in names if n.endswith(".kt")]
    return sorted(out)

# [P6] UiState.message を書くのに messageIsError を書かない copy(...)。
#   data class の copy は**既定値でなく現在値を引き継ぐ**ため、直前がエラーだと通常メッセージまで
#   失敗色・長時間表示のまま出る（3.400.0 で実際に作り込み、3.406.0 で全数修正した）。
#   「message を書くなら severity も必ず書く」を機械で強制する。コンパイルは通ってしまうので lint が要る。
RE_COPY_MESSAGE = re.compile(r"\bcopy\(")

# [3.409.0] 旧実装は「1**物理行**に copy( と message = が揃う」ことを要求しており、複数行に折り返した
#   `copy(\n  message = ...,\n)` を**丸ごと見落としていた**（実測 19 箇所を素通しして 0 件と報告）。
#   検査が守るはずの規則を検査自身が守れていない状態＝この repo が繰り返し踏んだ「防具が発火しない」型。
#   ブロック単位（copy( から括弧が閉じるまで）で見るよう作り直す。
#   括弧の深さを数える前に**文字列リテラルとコメントを消す**のが肝: message の値には
#   `"(${System.currentTimeMillis() - startMs}ms)"` のように**閉じない ASCII 括弧**が入りうるし、
#   `// [3.400.0] 旧: `message = "…"`` のようにコメント内の message= を拾うと誤検出になる（実際に踏んだ）。
def _strip_strings_and_comments(line):
    """行から文字列リテラルと行コメントを落とす（括弧の深さと識別子の検出を正しくするため）。"""
    out = []
    i = 0
    n = len(line)
    while i < n:
        c = line[i]
        if c == '"':
            i += 1
            while i < n:
                if line[i] == "\\":
                    i += 2
                    continue
                if line[i] == '"':
                    i += 1
                    break
                i += 1
            out.append(" ")
            continue
        if c == "/" and i + 1 < n and line[i + 1] == "/":
            break
        out.append(c)
        i += 1
    return "".join(out)


MAX_COPY_BLOCK_LINES = 80   # 走らせすぎないための保険（実データの最長は 27 行）


def find_p6():
    hits = []
    for path in kotlin_files():
        # 読めない .kt はここでは起こり得ない（起きたら検査が黙って0件になるので握り潰さない）。
        with open(path, encoding="utf-8") as fh:
            src = fh.read()
        lines = src.split("\n")
        i = 0
        while i < len(lines):
            st = lines[i].strip()
            if st.startswith("//") or st.startswith("*") or st.startswith("/*"):
                i += 1
                continue
            code = _strip_strings_and_comments(lines[i])
            if not RE_COPY_MESSAGE.search(code):
                i += 1
                continue
            # copy( から括弧が閉じるまでをブロックとして集める。
            depth = 0
            block = []
            j = i
            while j < len(lines) and j - i < MAX_COPY_BLOCK_LINES:
                c = _strip_strings_and_comments(lines[j])
                block.append(c)
                depth += c.count("(") - c.count(")")
                if depth <= 0:
                    break
                j += 1
            text = "\n".join(block)
            if re.search(r"\bmessage\s*=", text) and "messageIsError" not in text:
                hits.append("%s:%d" % (path.replace(ROOT + "/", ""), i + 1))
            i = j + 1
    return hits



# [P7] 二重エンコードの文字化け（UTF-8 を Latin-1/CP1252 として読んだ結果を再び UTF-8 で保存した状態）。
#   同梱の見本データ `assets/sample_state_v6.json` が実際にこの状態で出荷されており、アプリは自分の
#   asset を読むたび「文字化けを自動修復しました」と警告していた（3.407.0 で修復）。UTF-8 として妥当な
#   ため既存の検査は素通りする＝専用の検出が要る。判定は「U+00C0-U+00FF の直後に U+0080-U+00BF が続く」
#   ＝日本語(U+3000+)には当たらず、正規のラテン文字にもまず現れない並び。
_MOJIBAKE_LEAD = set(range(0x00C0, 0x0100))
_MOJIBAKE_TAIL = set(range(0x0080, 0x00C0))
# 文字化けの例をわざと本文に持つファイル（修復ロジック本体とそのテスト）は対象外。
MOJIBAKE_EXEMPT = (
    "app/src/main/java/com/magi/app/model/MojibakeRepair.kt",
    "app/src/test/java/com/magi/app/model/MojibakeRepairTest.kt",
)


def tracked_text_files():
    out = subprocess.run(["git", "ls-files"], cwd=ROOT, capture_output=True, text=True).stdout.split()
    return [f for f in out if not f.startswith(MOJIBAKE_EXEMPT)]


def find_p7():
    hits = []
    for rel in tracked_text_files():
        try:
            src = open(os.path.join(ROOT, rel), encoding="utf-8").read()
        except (UnicodeDecodeError, IsADirectoryError, FileNotFoundError):
            continue
        for n, line in enumerate(src.split("\n"), 1):
            for i in range(len(line) - 1):
                if ord(line[i]) in _MOJIBAKE_LEAD and ord(line[i + 1]) in _MOJIBAKE_TAIL:
                    hits.append("%s:%d" % (rel, n))
                    break
    return hits


def scan_templates():
    hits = []
    for path in kotlin_files():
        rel = os.path.relpath(path, ROOT)
        with open(path, encoding="utf-8") as fh:
            for n, line in enumerate(fh, 1):
                code = line.split("//", 1)[0]
                for m in RE_TEMPLATE_REF.finditer(code):
                    if _is_kotlin_ident_char(m.group(1)):
                        hits.append(f"{rel}:{n} ({m.group(0)})")
    return hits


def scan():
    findings = {"P1": [], "P2": [], "P3": [], "P4": []}
    for path in ui_files():
        rel = os.path.relpath(path, ROOT)
        # MagiTokens.kt は一次トークン層。ensureReadable の黒/白フォールバックは意図的なので P1/P2 の対象外。
        is_tokens = os.path.basename(path) == "MagiTokens.kt"
        with open(path, encoding="utf-8") as fh:
            for n, line in enumerate(fh, 1):
                code = line.split("//", 1)[0]  # コメントは無視
                if not is_tokens and RE_BLACK.search(code):
                    findings["P1"].append(f"{rel}:{n}")
                if not is_tokens and RE_HEX.search(code):
                    findings["P2"].append(f"{rel}:{n}")
                if RE_SHADOW_MODIFIER.search(code):
                    findings["P3"].append(f"{rel}:{n} (.shadow)")
                for m in RE_SHADOW_ELEV.finditer(code):
                    if int(m.group(1)) >= HEAVY_SHADOW_DP:
                        findings["P3"].append(f"{rel}:{n} (shadowElevation {m.group(1)}dp)")
                for m in RE_SHAPE.finditer(code):
                    dp = int(m.group(1))
                    # 密なデータセル(≤6dp: グリッド/カレンダー/集計)と pill(999) は意図的＝対象外。
                    # カード/チップ級(≥8dp)の任意角丸のみ「tier(shapes.*)に寄せる候補」として監視。
                    if 8 <= dp != 999:
                        findings["P4"].append(f"{rel}:{n} ({dp}dp)")
    return findings


# [3.409.5] P2/P4 の現状件数。**既存分は許し、増えたら落とす**ラチェットの基準。
#   2026-08-19 実測（3.409.4 時点）: P2=1（MagiScheduleViews.kt:1506）・P4=12（16dp×2・8dp×10）。
#   [3.409.6] その 13 件を全て tier へ寄せて **0 件**にした（P4: 8dp→shapes.extraSmall(10dp)/
#   16dp→shapes.large(18dp)、最低>上限のエラー枠だけ 3.403.0 の先例に合わせ shapes.medium。
#   P2: ensureReadable の第2引数を Color(0xFFFFFFFF)→Color.White＝兄弟の呼出と同じ書き方へ）。
#   **0 が基準になったので、以後この2つは「1 件でも増えたら落ちる」**＝任意値は例外でなく禁止に戻る。
#   意図して増やすときは、この定数と一緒に「なぜ任意値が要るか」を DESIGN.md へ書く。
P2_BASELINE = 0
P4_BASELINE = 0


# [3.409.10] P8: `docs/magi_design_system.md` の「✅＝実装済」が実在するかを機械で確かめる。
#   この ✅ は読み手が「共通コンポーネントが用意されている」と信じる根拠になる（実際 3.409.8 では
#   ✅ を理由に未使用コンポーネントを残す判断をした）。ところが実測すると 4.4 QuickActionTile と
#   4.12 MagiCalendarMonthView/DayShiftCell/ShiftEventPill は**コードに存在しない**＝
#   状態列そのものが信用できなかった。✅ 節の kotlin ブロックに書かれた `fun 名前(` を抜き出し、
#   実装に同名の関数があるかを突き合わせる（🟡/⬜ は「未実装/一部」の宣言なので対象外）。
DS_DOC = os.path.join(ROOT, "docs/magi_design_system.md")
RE_DS_HEAD = re.compile(r"^### (\d+\.\d+) (.*)$")
RE_DS_FUN = re.compile(r"\bfun ([A-Za-z]\w*)\s*\(")


def find_p8():
    """✅ と書かれた節の kotlin ブロックの関数が実装に存在するか。"""
    out = []
    if not os.path.exists(DS_DOC):
        return out
    with open(DS_DOC, encoding="utf-8") as fh:
        lines = fh.read().split("\n")
    src = []
    for base, _dirs, files in os.walk(os.path.join(ROOT, "app/src/main/java")):
        for fn in files:
            if fn.endswith(".kt"):
                with open(os.path.join(base, fn), encoding="utf-8") as f:
                    src.append(f.read())
    code = "\n".join(src)
    sec, mark, in_block, n0 = None, False, False, 0
    for n, line in enumerate(lines, 1):
        m = RE_DS_HEAD.match(line)
        if m:
            # 状態は見出しで**最初に現れた**グリフ（後ろの補足に別のグリフが出ることがある。
            #   例「⬜（… ✅ を訂正）」＝状態は ⬜。素朴に `"✅" in 見出し` にすると訂正文で誤検出する）。
            first = min(
                (m.group(2).index(g), g) for g in ("✅", "🟡", "⬜") if g in m.group(2)
            )[1] if any(g in m.group(2) for g in ("✅", "🟡", "⬜")) else ""
            sec, mark, in_block, n0 = m.group(1), (first == "✅"), False, n
            continue
        if line.startswith("```"):
            in_block = not in_block
            continue
        if in_block and mark:
            for fm in RE_DS_FUN.finditer(line):
                name = fm.group(1)
                if f"fun {name}(" not in code:
                    out.append(f"docs/magi_design_system.md:{n0} §{sec} ✅ だが実装に無い: {name}")
    return out


RE_BEGIN_CAPTURE = re.compile(r"\bval\s+(\w+)\s*=\s*beginBoardJob\s*\(")
RE_BEGIN_ANY = re.compile(r"\bbeginBoardJob\s*\(")
RE_BEGIN_DEF = re.compile(r"\bfun\s+beginBoardJob\b")


def _strip_kotlin_file(lines):
    """行コメント・文字列に加え、**複数行の /* */（KDoc 含む）**も落とした行リストを返す。

    [3.409.13/レビュー#3] 旧 P9 は行単位の _strip_strings_and_comments だけを（しかも endBoardJob の
    照合には**使わずに**）呼んでおり、①コメント中の「finally」が本物の finally として数えられる
    ②コメントアウトされた `// endBoardJob(...)` が解放として通る、の両方が注入試験で実証された。
    KDoc（`/** … */`）は行単位ストリッパでは落ちないので、ファイル単位でブロックコメントを追跡する。
    """
    out = []
    in_block = False
    for line in lines:
        res, i, n = [], 0, len(line)
        while i < n:
            if in_block:
                j = line.find("*/", i)
                if j < 0:
                    i = n
                else:
                    in_block = False
                    i = j + 2
                continue
            c = line[i]
            if c == '"':
                i += 1
                while i < n:
                    if line[i] == "\\":
                        i += 2
                        continue
                    if line[i] == '"':
                        i += 1
                        break
                    i += 1
                res.append(" ")
                continue
            if c == "/" and i + 1 < n and line[i + 1] == "/":
                break
            if c == "/" and i + 1 < n and line[i + 1] == "*":
                in_block = True
                i += 2
                continue
            res.append(c)
            i += 1
        out.append("".join(res))
    return out


def _p9_scan_site(lines, i, col, var):
    """begin サイト1つを追跡し、(結果, 行番号) を返す。

    結果: "ok"=finally 内で解放 / "outside"=解放は在るが finally の外 / "missing"=解放が無い。

    [3.409.13/レビュー#3,#4] 判定は**ブロック所属**で行う: `{` を積むとき直前の識別子が
    `finally` かを記録し、endBoardJob 出現時にスタック上へ finally ブロックが在るかを見る。
    旧実装の「begin〜end の行範囲に finally という語が在るか」は、①コメントの finally で偽陰性
    ②1行 `try { .. } finally { endBoardJob(t) }`（end 行自身の finally が range(i, end_at) に
    入らない）で偽陽性、の両方を注入試験で実証した欠陥だった。
    深さが begin 行の水準を下回ったら関数ブロックを抜けた＝探索終了（全サイトが同名 boardToken を
    使うため、前方無制限だと隣の関数の解放を拾って対に見える＝3.409.12 の注入で実証済み）。
    """
    re_end = re.compile(r"\bendBoardJob\s*\(\s*" + re.escape(var) + r"\s*\)")
    stack = []      # True = finally 直後に開いたブロック
    word = ""
    for j in range(i, min(i + 800, len(lines))):
        text = lines[j]
        starts = {mm.start() for mm in re_end.finditer(text)}
        k = col if j == i else 0
        while k < len(text):
            if k in starts:
                return ("ok" if any(stack) else "outside"), j
            c = text[k]
            if c.isalnum() or c == "_":
                word += c
            elif c.isspace():
                pass    # `finally {` の空白は語を跨がない
            else:
                if c == "{":
                    stack.append(word == "finally")
                elif c == "}":
                    if stack:
                        stack.pop()
                    else:
                        return "missing", j   # begin の水準より外へ出た＝関数を抜けた
                word = ""
            k += 1
        word = ""   # 行末は語を切る
    return "missing", i


def find_p9():
    """盤面を差し替えるジョブの取得（beginBoardJob）が finally の解放（endBoardJob）と対になっているか。

    これを外すと **アプリが恒久的に読み取り専用へ固着する**（`optimizeInFlight()` が真のままになり、
    セル編集・一括シート・Undo/Redo・設定変更のガードが全部閉じたまま戻らない）。
    3.333.0／3.382.0／3.404.0 は同じ「旗を立てて確実に戻さない」型を3回踏んでおり、しかも
    **3回とも呼び出し側の書き忘れ**だった＝ヘルパーの単体テストでは捕まらない。よって機械検査で止める。

    検出するのは3通り: ①戻り値のトークンを捨てた裸の呼び出し（解放が構造的に不可能）
    ②endBoardJob がどこにも無い ③endBoardJob は在るが finally ブロックの中でない。
    """
    hits = []
    for path in kotlin_files():
        try:
            raw = open(path, encoding="utf-8").read().split("\n")
        except (UnicodeDecodeError, IsADirectoryError, FileNotFoundError):
            continue
        if not any("beginBoardJob" in l for l in raw):
            continue
        lines = _strip_kotlin_file(raw)
        rel = os.path.relpath(path, ROOT)
        for i, line in enumerate(lines):
            m = RE_BEGIN_CAPTURE.search(line)
            if not m:
                # [レビュー#2] `beginBoardJob("x")` と裸で呼ぶとトークンが失われ**解放が構造的に不可能**
                #   ＝最悪の書き間違いなのに旧 P9 は完全に素通しだった（注入で実証）。定義行は除外。
                if RE_BEGIN_ANY.search(line) and not RE_BEGIN_DEF.search(line):
                    hits.append(f"{rel}:{i + 1}  beginBoardJob の戻り値（トークン）を捨てています（endBoardJob で解放できず読み取り専用に固着します）")
                continue
            state, at = _p9_scan_site(lines, i, m.end(), m.group(1))
            if state == "missing":
                hits.append(f"{rel}:{i + 1}  endBoardJob({m.group(1)}) が見つかりません（旗が戻らず読み取り専用に固着します）")
            elif state == "outside":
                hits.append(f"{rel}:{at + 1}  endBoardJob({m.group(1)}) が finally の外です（例外・停止で旗が戻りません）")
    return hits


def main():
    strict = "--strict" in sys.argv
    findings = scan()
    findings["P5"] = scan_templates()
    findings["P6"] = find_p6()
    findings["P7"] = find_p7()
    findings["P8"] = find_p8()
    findings["P9"] = find_p9()
    labels = {
        "P1": "純黒本文/背景 (Color.Black / 0xFF000000)",
        "P2": "生 hex 直書き (Color(0x……)) ※MagiTokens.kt 除く=baseline監視",
        "P3": "重い影 (.shadow / shadowElevation)",
        "P4": "任意角丸 (RoundedCornerShape(<dp>)) ※999=pill 除外",
        "P5": "テンプレート食い込み (変数の直後に日本語＝必ずコンパイルエラー)",
        "P6": "message を書くのに messageIsError を書かない copy(…)（copy は現在値を引き継ぐ＝失敗色が残る）",
        "P7": "二重エンコードの文字化け（UTF-8 を Latin-1 として読んだ内容を保存した状態）",
        "P8": "magi_design_system.md の ✅（実装済）と実装の食い違い",
        "P9": "beginBoardJob と finally の endBoardJob が対になっていない（読み取り専用に固着）",
    }
    total = sum(len(v) for v in findings.values())
    print("=== MAGI design lint (docs/DESIGN.md P1-P4) ===")
    for key in ("P1", "P2", "P3", "P4", "P5", "P6", "P7", "P8", "P9"):
        hits = findings[key]
        print(f"\n[{key}] {labels[key]}: {len(hits)} 件")
        for h in hits[:40]:
            print(f"    {h}")
        if len(hits) > 40:
            print(f"    …ほか {len(hits) - 40} 件")
    hard = (len(findings["P1"]) + len(findings["P3"]) + len(findings["P5"])
            + len(findings["P6"]) + len(findings["P7"]) + len(findings["P8"])
            + len(findings["P9"]))
    print(f"\n合計 {total} 件（P1純黒+P3影+P5テンプレート+P6メッセージ+P8DS表示+P9ジョブ解放 severity=hard {hard} 件 / P2生hex・P4角丸=baseline監視）。")

    # [3.409.5] P2/P4 は「baseline 監視」と名乗りながら**baseline を記録していなかった**＝20件増えても
    #   exit 0 で静かに通る。`docs/DESIGN.md` §4 はこれを「禁止事項（machine-checkable）」と呼んでいるのに
    #   強制されていない状態だった（今回のセッションで P6 に見つけたのと同じ「検査が守れていない」型）。
    #   既存分は許し、**増えたときだけ落とす**ラチェットにする。減ったら baseline を下げるよう促す
    #   （下げ忘れると次の増加を見逃す＝ラチェットが緩む）。
    # [3.409.6] 途中で return せず**全部集めてから**落とす。1つ直して再実行して次が出る、を繰り返させない
    #   （旧: 最初に当たった検査だけ報告して return 1＝P2 と P4 が同時に増えても P4 が見えなかった）。
    blockers = []
    for key, base in (("P2", P2_BASELINE), ("P4", P4_BASELINE)):
        n = len(findings[key])
        if n > base:
            blockers.append(
                f"{key}: baseline {base} 件 → {n} 件に増えています。"
                f"{'色は MagiTokens.kt / MagiTheme の colorScheme ' if key == 'P2' else '角丸は MagiTheme の Shapes(small/medium/large) '}"
                f"を使ってください（どうしても必要なら根拠を添えて {key}_BASELINE を更新）。")
        elif n < base:
            blockers.append(
                f"{key}: baseline {base} 件 → {n} 件に減りました。tools/design_lint.py の "
                f"{key}_BASELINE を {n} へ下げてください（下げ忘れると次の増加を見逃します）。")
    # P5 は「様式の逸脱」でなく**確実なコンパイルエラー**なので、--strict でなくても失敗させる。
    if findings["P5"]:
        blockers.append("P5: 変数の直後に日本語が続いています。必ず波括弧で囲んでください（例: 「N件」なら {count} を波括弧で）。")
    # P6 はコンパイルが通ってしまう＝実機で「成功なのに失敗色」として初めて気づく。ここで止める。
    if findings["P6"]:
        blockers.append("P6: message を書くなら messageIsError も必ず書いてください（copy は既定値でなく現在値を引き継ぎます）。")
    # P7 は「UTF-8 として妥当なので誰も気づかない」型＝出荷物に入り込む。ここで止める。
    if findings["P8"]:
        blockers.append("P8: magi_design_system.md の ✅（実装済）が実装と食い違っています。実装するか、状態を 🟡/⬜ へ直してください。")
    # P9 は落ちても例外が出ない＝実機で「何をしても編集できない」として初めて気づく。ここで止める。
    if findings["P9"]:
        blockers.append("P9: beginBoardJob には finally の endBoardJob を必ず対にしてください（旗が戻らないとアプリが読み取り専用に固着します）。")
    if findings["P7"]:
        blockers.append("P7: 二重エンコードの文字化けです。`text.encode('latin-1').decode('utf-8')` で復号できます（可逆であることを確認してから保存）。")
    if blockers:
        for line in blockers:
            print(line)
        return 1
    if strict and hard > 0:
        print("--strict: P1/P3 の hard 違反があるため exit 1")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
