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
"""
import os
import re
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


def main():
    strict = "--strict" in sys.argv
    findings = scan()
    findings["P5"] = scan_templates()
    labels = {
        "P1": "純黒本文/背景 (Color.Black / 0xFF000000)",
        "P2": "生 hex 直書き (Color(0x……)) ※MagiTokens.kt 除く=baseline監視",
        "P3": "重い影 (.shadow / shadowElevation)",
        "P4": "任意角丸 (RoundedCornerShape(<dp>)) ※999=pill 除外",
        "P5": "テンプレート食い込み (変数の直後に日本語＝必ずコンパイルエラー)",
    }
    total = sum(len(v) for v in findings.values())
    print("=== MAGI design lint (docs/DESIGN.md P1-P4) ===")
    for key in ("P1", "P2", "P3", "P4", "P5"):
        hits = findings[key]
        print(f"\n[{key}] {labels[key]}: {len(hits)} 件")
        for h in hits[:40]:
            print(f"    {h}")
        if len(hits) > 40:
            print(f"    …ほか {len(hits) - 40} 件")
    hard = len(findings["P1"]) + len(findings["P3"]) + len(findings["P5"])
    print(f"\n合計 {total} 件（P1純黒+P3影+P5テンプレート=hard {hard} 件 / P2生hex・P4角丸=baseline監視）。")
    # P5 は「様式の逸脱」でなく**確実なコンパイルエラー**なので、--strict でなくても失敗させる。
    if findings["P5"]:
        print("P5: 変数の直後に日本語が続いています。必ず波括弧で囲んでください（例: 「N件」なら {count} を波括弧で）。")
        return 1
    if strict and hard > 0:
        print("--strict: P1/P3 の hard 違反があるため exit 1")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
