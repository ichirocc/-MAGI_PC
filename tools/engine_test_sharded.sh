#!/bin/bash
# エンジンのテスト（MagiEngine.Tests）をプロセスを分けて並列に流す。テストは同じプロセスの中では並列にできない
#   （V6NativeOptimizer のプロセス全体の static、AssemblyInfo.cs）が、別のプロセスなら static も別々。
#   使い方: tools/engine_test_sharded.sh [本数]（既定はコア数、1 なら従来どおり 1 本）。所要は tools/engine_test_times.tsv の実測。
#   最後の 1 本は「ほかの本に割り当てたクラス以外すべて」＝表に無い新しいクラスも必ず流れる。
set -u
HERE=$(cd "$(dirname "$0")" && pwd); ROOT=$(cd "$HERE/.." && pwd)
export PATH=/root/.dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1
N=${1:-$(nproc 2>/dev/null || echo 1)}
PROJ="$ROOT/windows/MagiEngine.Tests/MagiEngine.Tests.csproj"
OUT=${MAGI_ENGINE_TEST_OUT:-/tmp/magi-engine-tests}; rm -rf "$OUT"; mkdir -p "$OUT"
dotnet build "$PROJ" -c Release --nologo -v q > "$OUT/build.log" 2>&1 || { tail -n 30 "$OUT/build.log"; echo "BUILD FAILED"; exit 1; }
if [ "$N" -le 1 ]; then dotnet test "$PROJ" -c Release --no-build --nologo -v q 2>&1 | grep -E "Passed!|Failed!|\[FAIL\]|error"; exit "${PIPESTATUS[0]}"; fi
python3 - "$HERE/engine_test_times.tsv" "$N" "$OUT" <<'PY'
import sys
rows = [l.rstrip('\n').split('\t') for l in open(sys.argv[1], encoding='utf-8') if l.strip()]
n = int(sys.argv[2]); out = sys.argv[3]
bins = [[0, []] for _ in range(n)]
for ms, cls in sorted(rows, key=lambda r: -int(r[0])):
    b = min(bins, key=lambda x: x[0]); b[0] += int(ms); b[1].append(cls)
bins.sort(key=lambda x: -x[0])
# 一番軽い本を「残り全部」に回す（表に無いクラスもここで流れる）。
named = [b[1] for b in bins[:-1]]
for i, cs in enumerate(named):
    open(f"{out}/filter{i}.txt", 'w').write('|'.join(f"FullyQualifiedName~{c}." for c in cs))
others = [c for cs in named for c in cs]
open(f"{out}/filter{n-1}.txt", 'w').write('&'.join(f"FullyQualifiedName!~{c}." for c in others))
PY
pids=()
for ((i = 0; i < N; i++)); do
  dotnet test "$PROJ" -c Release --no-build --nologo -v q --filter "$(cat "$OUT/filter$i.txt")" > "$OUT/shard$i.log" 2>&1 &
  pids+=("$i:$!")
done
rc=0; total=0
for e in "${pids[@]}"; do
  i=${e%%:*}; wait "${e#*:}" || rc=1
  line=$(grep -E "Passed!|Failed!" "$OUT/shard$i.log" | tail -1); echo "-- shard $i: $line"
  t=$(echo "$line" | sed -n 's/.*Total: *\([0-9]*\).*/\1/p'); total=$((total + ${t:-0}))
  grep -q "Failed!" "$OUT/shard$i.log" && grep -E "\[FAIL\]|Assert|Expected|Actual" "$OUT/shard$i.log" | head -30
done
echo "== total tests: $total"
[ "$rc" -eq 0 ] || echo "TESTS FAILED"; exit "$rc"
