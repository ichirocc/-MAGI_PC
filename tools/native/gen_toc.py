#!/usr/bin/env python3
"""magi_native.cpp の行番号付き TOC（目次コメント）を生成・更新する。

目的: 2400行超の単一 translation unit を AI/人間がレビューするとき、どの機能が
どの行にあるかへ即ジャンプできるようにする（コードには一切触らない＝コメントのみ）。

仕組み:
- namespace 直下（列0）の宣言行を機械抽出する: struct / 関数 / 定数 / JNI エントリ
  （`extern "C" JNIEXPORT` の次行 `Java_com_magi_app_v6_NativeBridge_<name>(` から短名を取る）。
- TOC は BEGIN/END 番兵コメントで囲んで `namespace {` の直前へ挿入する。既存 TOC は
  再生成時にまず除去する（冪等: 2回連続で走らせても差分ゼロ）。
- 行番号は **TOC 挿入後** の値（不動点計算: TOC 自身の行数ぶん全エントリがずれるため、
  ずらした結果で番号を振り直す。TOC の行数はエントリ数に依存し数字の桁で変わらないため
  1回のオフセット適用で安定する）。

使い方: `python3 tools/native/gen_toc.py [--check]`
  --check … 生成結果と現状が一致しなければ非ゼロ終了（TOC の陳腐化検知。CI 配線は任意）。

コードを改修して行がずれたら、このツールを再実行して TOC を更新すること。
"""
import re
import sys
import os

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.normpath(os.path.join(HERE, "..", "..", "app", "src", "main", "cpp", "magi_native.cpp"))

BEGIN = "// ===== TOC（自動生成: tools/native/gen_toc.py。コード改修時は再実行して更新） ====="
END = "// ===== TOC end ====="

RE_JNI_NAME = re.compile(r"^Java_com_magi_app_v6_NativeBridge_(\w+)\s*\(")
RE_STRUCT = re.compile(r"^struct (\w+)")
RE_CONST = re.compile(r"^static const [\w :<>]*?\b(\w+)\s*=")
RE_FN = re.compile(
    r"^(?:static\s+|inline\s+|extern\s+)*(?:const\s+)?"
    r"[A-Za-z_][\w:<>,*& ]*?[ *&](\w+)\s*\("
)


def extract_entries(lines):
    """(0-based line index, kind, name) のリスト。列0の宣言のみ・TOC/コメント/プリプロセッサ除外。"""
    entries = []
    i = 0
    while i < len(lines):
        l = lines[i]
        if l.startswith("extern \"C\" JNIEXPORT"):
            if i + 1 < len(lines):
                m = RE_JNI_NAME.match(lines[i + 1])
                if m:
                    entries.append((i, "JNI", m.group(1)))
                    i += 2
                    continue
        if l.startswith(("//", "#", "}", " ", "\t")) or l.strip() == "" or l.startswith("namespace"):
            i += 1
            continue
        m = RE_STRUCT.match(l)
        if m:
            entries.append((i, "struct", m.group(1)))
            i += 1
            continue
        m = RE_CONST.match(l)
        if m:
            entries.append((i, "const", m.group(1)))
            i += 1
            continue
        m = RE_FN.match(l)
        if m:
            entries.append((i, "fn", m.group(1)))
            i += 1
            continue
        i += 1
    return entries


def build(text):
    lines = text.split("\n")
    # 既存 TOC を除去（冪等化）
    if BEGIN in lines:
        b = lines.index(BEGIN)
        e = lines.index(END)
        assert e > b
        # TOC 直後の空行も一緒に取り除く（挿入時に足すため）
        tail = e + 1
        if tail < len(lines) and lines[tail].strip() == "":
            tail += 1
        lines = lines[:b] + lines[tail:]

    # 挿入位置 = `namespace {` の直前
    ins = next(i for i, l in enumerate(lines) if l.startswith("namespace {"))
    entries = extract_entries(lines)
    entries = [(i, k, n) for (i, k, n) in entries if i > ins]

    # TOC 本体。挿入後の 1-based 番号 = 元 0-based index + len(toc) + 1
    #（エントリは全て挿入位置より後ろ＝TOC の行数ぶんだけ一様にずれる）。
    toc_len = len(entries) + 3  # BEGIN + entries + END + 直後の空行1
    toc = [BEGIN]
    for i, kind, name in entries:
        toc.append(f"//  L{i + toc_len + 1:>5}  {kind:<6} {name}")
    toc.append(END)
    toc.append("")
    assert len(toc) == toc_len

    out = lines[:ins] + toc + lines[ins:]
    return "\n".join(out)


def main():
    with open(SRC, encoding="utf-8") as f:
        cur = f.read()
    new = build(cur)
    # 冪等性の自己検査（生成器のバグで番号が発振していないか）
    assert build(new) == new, "generator is not idempotent"
    if "--check" in sys.argv:
        if new != cur:
            print("TOC is stale. Run: python3 tools/native/gen_toc.py")
            sys.exit(1)
        print("TOC is up to date")
        return
    if new != cur:
        with open(SRC, "w", encoding="utf-8") as f:
            f.write(new)
        print(f"TOC updated in {SRC}")
    else:
        print("TOC already up to date")


if __name__ == "__main__":
    main()
