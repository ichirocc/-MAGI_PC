#!/usr/bin/env python3
"""追加したコメント行を差分から機械的に抜き出す（comment-check スキルの手順1・4）。

    python3 tools/comment_ratio.py                 # 作業ツリー vs HEAD（コミット前の点検）
    python3 tools/comment_ratio.py --staged        # ステージ済みだけ
    python3 tools/comment_ratio.py --range A..B    # コミット範囲（PR 全体・過去の計測）
    python3 tools/comment_ratio.py --summary       # 数字だけ
    python3 tools/comment_ratio.py --repo ../-magi_pc --range A..B   # 別リポジトリ

数え方（Zenn「ルールではなく skill に指示を書くことで、Claude のコメントを減らせた」と同じ）:
追加行のうちコメント行 / 非空の追加行、と 4 行以上続くコメントの塊。.md/.json/.txt は対象外。
ルートは自分の位置から決める（実行位置に依存しない）。
"""
import argparse, os, re, statistics, subprocess, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SLASH = {".kt", ".kts", ".java", ".cs", ".cpp", ".h", ".c", ".js", ".ts", ".swift"}
HASH = {".py", ".yml", ".yaml", ".ps1", ".sh", ".toml", ".gradle", ".properties", ".iss"}
XML = {".xml", ".xaml", ".html", ".axaml"}
SKIP = {".md", ".json", ".txt", ".csv", ".svg", ".lock"}


def is_comment(ext, s):
    if ext in SLASH:
        return s.startswith(("//", "/*", "*", "*/"))
    if ext in HASH:
        if ext == ".iss":
            return s.startswith(";")
        return s.startswith("#") and not s.startswith("#!") and not (ext == ".sh" and s.startswith("#!"))
    if ext in XML:
        return s.startswith(("<!--", "-->")) or (s.startswith("<") is False and s.endswith("-->"))
    return False


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--range", help="A..B の git 範囲（省略時は作業ツリー vs HEAD）")
    ap.add_argument("--staged", action="store_true")
    ap.add_argument("--summary", action="store_true")
    ap.add_argument("--repo", help="別リポジトリを測るときのルート（既定は自分の位置から決めたルート）")
    ap.add_argument("paths", nargs="*", help="限定する pathspec")
    a = ap.parse_args()
    root = os.path.abspath(a.repo) if a.repo else ROOT
    cmd = ["git", "-C", root, "diff", "-U0", "--no-color"]
    if a.range:
        cmd.append(a.range)
    elif a.staged:
        cmd.append("--cached")
    else:
        cmd.append("HEAD")
    cmd += ["--", *a.paths]
    diff = subprocess.run(cmd, capture_output=True, text=True, errors="replace").stdout
    files, cur_file, ext, ln = {}, None, "", 0
    cm = code = 0
    blocks, run = [], 0
    for line in diff.splitlines():
        if line.startswith("+++ "):
            cur_file = line[6:] if line.startswith("+++ b/") else None
            ext = os.path.splitext(cur_file or "")[1].lower()
            continue
        if line.startswith("@@"):
            m = re.search(r"\+(\d+)", line)
            ln = int(m.group(1)) if m else 0
            if run >= 4: blocks.append(run)
            run = 0
            continue
        if not line.startswith("+") or cur_file is None or ext in SKIP:
            continue
        body = line[1:]
        s = body.strip()
        n = ln; ln += 1
        if not s:
            continue
        if is_comment(ext, s):
            cm += 1; run += 1
            files.setdefault(cur_file, []).append((n, body.rstrip()))
        else:
            code += 1
            if run >= 4: blocks.append(run)
            run = 0
    if run >= 4: blocks.append(run)
    total = cm + code
    if not a.summary:
        for f, rows in files.items():
            print(f"== {f}")
            for n, body in rows:
                print(f"{n:5d}: {body}")
        print()
    ratio = (100.0 * cm / total) if total else 0.0
    print(f"comment lines={cm} code lines={code} ratio={ratio:.1f}% "
          f"blocks(>=4)={len(blocks)} max block={max(blocks) if blocks else 0}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
