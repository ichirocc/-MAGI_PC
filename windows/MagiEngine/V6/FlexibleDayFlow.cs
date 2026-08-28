namespace MagiEngine.V6;

/// <summary>
/// 1日分の「職員→シフト」割当を、シフト人数を固定せずに最小費用で解く。
///
/// staffShiftCost[i][k]: 職員iをシフトkへ置く費用。到達不能はINF。
/// shiftMarginalCost[k][q]: シフトkの(q+1)人目を置く限界費用。
///
/// source -&gt; staff(cap=1) -&gt; shift(cap=1) -&gt; sink(cap=1の並列辺×S)
///
/// 必要人数を埋める辺は負費用になり得るため、各増加路をSPFAで厳密に解く。
/// 職員数S、シフト数Kに対し、実データ規模では概ねO(S^2*K)〜O(S^3*K)。
/// </summary>
internal static class FlexibleDayFlow
{
    public const long INF = 1_000_000_000_000L;

    public sealed record Result(int[] Assignment, long Cost);

    /// <summary>
    /// 残余グラフの1辺。<b>reference型（class）で持つ必要がある</b>＝Kotlin原本の <c>var cap: Int</c> は
    /// JVMの参照セマンティクスで <c>graph[v][ei]</c> を通じ同一インスタンスを指す前提のミュータブル
    /// フィールド。C#の <c>record struct</c>/値型で置換すると <c>e.Cap--</c> がローカルコピーにしか
    /// 効かず、残余グラフの更新が消える（正しさに直結する差＝重要）。
    /// </summary>
    private sealed class Edge
    {
        public readonly int To;
        public readonly int Rev;
        public int Cap;
        public readonly long Cost;

        public Edge(int to, int rev, int cap, long cost)
        {
            To = to;
            Rev = rev;
            Cap = cap;
            Cost = cost;
        }
    }

    public static Result? Solve(long[][] staffShiftCost, long[][] shiftMarginalCost, long inf = INF)
    {
        int staffN = staffShiftCost.Length;
        if (staffN == 0) return new Result(Array.Empty<int>(), 0L);
        int shiftN = staffShiftCost[0].Length;
        if (shiftN == 0 || staffShiftCost.Any(row => row.Length != shiftN)) return null;
        if (shiftMarginalCost.Length != shiftN || shiftMarginalCost.Any(row => row.Length < staffN)) return null;

        int source = 0;
        int staffBase = 1;
        int shiftBase = staffBase + staffN;
        int sink = shiftBase + shiftN;
        int nodeN = sink + 1;
        var graph = new List<Edge>[nodeN];
        for (int i = 0; i < nodeN; i++) graph[i] = new List<Edge>();

        int AddEdge(int from, int to, int cap, long cost)
        {
            int index = graph[from].Count;
            int reverseIndex = graph[to].Count;
            graph[from].Add(new Edge(to, reverseIndex, cap, cost));
            graph[to].Add(new Edge(from, index, 0, -cost));
            return index;
        }

        var staffShiftEdge = new int[staffN][];
        for (int i = 0; i < staffN; i++)
        {
            staffShiftEdge[i] = new int[shiftN];
            Array.Fill(staffShiftEdge[i], -1);
        }
        for (int i = 0; i < staffN; i++)
        {
            AddEdge(source, staffBase + i, 1, 0L);
            for (int k = 0; k < shiftN; k++)
            {
                long c = staffShiftCost[i][k];
                if (c >= inf / 2) continue;
                staffShiftEdge[i][k] = AddEdge(staffBase + i, shiftBase + k, 1, c);
            }
        }
        for (int k = 0; k < shiftN; k++)
        {
            for (int q = 0; q < staffN; q++) AddEdge(shiftBase + k, sink, 1, shiftMarginalCost[k][q]);
        }

        int flow = 0;
        long totalCost = 0L;
        var dist = new long[nodeN];
        var prevV = new int[nodeN];
        var prevE = new int[nodeN];
        var inQueue = new bool[nodeN];
        // inQueueにより同時在籍は最大nodeN。リングのfull/empty衝突を避ける余裕を持たせる。
        var queue = new int[nodeN * 4 + 8];

        while (flow < staffN)
        {
            Array.Fill(dist, inf);
            Array.Fill(prevV, -1);
            Array.Fill(prevE, -1);
            Array.Fill(inQueue, false);
            int head = 0;
            int tail = 0;
            dist[source] = 0L;
            queue[tail++] = source;
            inQueue[source] = true;

            while (head != tail)
            {
                int v = queue[head];
                head = (head + 1) % queue.Length;
                inQueue[v] = false;
                for (int ei = 0; ei < graph[v].Count; ei++)
                {
                    var e = graph[v][ei];
                    if (e.Cap <= 0 || dist[v] >= inf / 2) continue;
                    long nd = dist[v] + e.Cost;
                    if (nd >= dist[e.To]) continue;
                    dist[e.To] = nd;
                    prevV[e.To] = v;
                    prevE[e.To] = ei;
                    if (!inQueue[e.To])
                    {
                        queue[tail] = e.To;
                        tail = (tail + 1) % queue.Length;
                        inQueue[e.To] = true;
                    }
                }
            }
            if (dist[sink] >= inf / 2) return null;

            int cur = sink;
            while (cur != source)
            {
                int pv = prevV[cur];
                int pe = prevE[cur];
                if (pv < 0 || pe < 0) return null;
                var e = graph[pv][pe];
                e.Cap--;
                graph[cur][e.Rev].Cap++;
                cur = pv;
            }
            flow++;
            totalCost += dist[sink];
        }

        var assignment = new int[staffN];
        Array.Fill(assignment, -1);
        for (int i = 0; i < staffN; i++)
        {
            int node = staffBase + i;
            for (int k = 0; k < shiftN; k++)
            {
                int ei = staffShiftEdge[i][k];
                if (ei >= 0 && graph[node][ei].Cap == 0)
                {
                    assignment[i] = k;
                    break;
                }
            }
            if (assignment[i] < 0) return null;
        }
        return new Result(assignment, totalCost);
    }
}
