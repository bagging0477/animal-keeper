using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 바닥 칸 집합의 외곽선(바닥 칸과 바닥 아닌 칸이 맞닿는 타일 경계)을 닫힌 폴리곤들로 추적한다.
/// 바닥끼리 맞닿은 경계(방과 방 사이 실제로 지나다닐 수 있는 통로)에는 선이 생기지 않고, 바닥과 맵 바깥(경계
/// 충돌체가 놓이는 이동 불가 칸) 사이에만 선이 생긴다. 꼭짓점은 항상 정수 칸 모서리라 타일 경계와 정확히 일치한다.
/// 같은 방향으로 이어지는 칸 단위 선분은 하나로 합쳐서, 이어진 벽 구간마다 끊김 없는 폴리곤 하나가 된다.
/// </summary>
public static class FloorOutline
{
    // 바닥을 항상 왼쪽에 두고 도는 방향(바깥 외곽선은 반시계, 안쪽 구멍은 시계 방향)으로 칸 경계 선분을 만든다.
    private static readonly Vector2Int[] Directions = { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };

    /// <summary>각 루프는 칸 모서리 좌표의 꼭짓점 목록(마지막 → 처음으로 닫힌다). 일직선 위의 중간 꼭짓점은 없다.</summary>
    public static List<List<Vector2Int>> TraceLoops(HashSet<Vector2Int> floor)
    {
        // 시작 꼭짓점 → 나가는 방향(Directions 인덱스) 목록. 대각선으로만 맞닿은 바닥 두 칸의 공유 꼭짓점에서는 나가는
        // 선분이 두 개가 된다.
        Dictionary<Vector2Int, List<int>> outgoing = new Dictionary<Vector2Int, List<int>>();
        void AddEdge(Vector2Int from, int dir)
        {
            if (!outgoing.TryGetValue(from, out List<int> list)) outgoing[from] = list = new List<int>(1);
            list.Add(dir);
        }

        int edgeCount = 0;
        foreach (Vector2Int c in floor)
        {
            if (!floor.Contains(c + Vector2Int.down)) { AddEdge(new Vector2Int(c.x, c.y), 0); edgeCount++; }
            if (!floor.Contains(c + Vector2Int.right)) { AddEdge(new Vector2Int(c.x + 1, c.y), 1); edgeCount++; }
            if (!floor.Contains(c + Vector2Int.up)) { AddEdge(new Vector2Int(c.x + 1, c.y + 1), 2); edgeCount++; }
            if (!floor.Contains(c + Vector2Int.left)) { AddEdge(new Vector2Int(c.x, c.y + 1), 3); edgeCount++; }
        }

        // 시작점을 정렬해서 고르면 같은 바닥에서 항상 같은 루프(같은 시작 꼭짓점)가 나온다.
        List<Vector2Int> starts = new List<Vector2Int>(outgoing.Keys);
        starts.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));

        List<List<Vector2Int>> loops = new List<List<Vector2Int>>();
        int used = 0;
        foreach (Vector2Int start in starts)
        {
            while (outgoing.TryGetValue(start, out List<int> startDirs) && startDirs.Count > 0)
            {
                List<Vector2Int> corners = new List<Vector2Int>();
                Vector2Int v = start;
                int dir = startDirs[0];
                startDirs.RemoveAt(0);
                int firstDir = dir;
                corners.Add(v);

                while (true)
                {
                    used++;
                    v += Directions[dir];
                    outgoing.TryGetValue(v, out List<int> dirs);
                    if (v == start)
                    {
                        // 시작점으로 돌아왔을 때, 처음 출발한 선분이 아직 남아 있다고 치고 왼쪽 우선 규칙으로 골랐을 때 그 선분이
                        // 뽑히면 루프가 닫힌 것이다. 다른 선분이 뽑히면(시작점이 대각선 접점인데 같은 덩어리가 그쪽으로 이어지는
                        // 경우) 계속 돈다.
                        List<int> withFirst = dirs != null ? new List<int>(dirs) : new List<int>();
                        withFirst.Add(firstDir);
                        if (TurnLeftFirst(dir, withFirst) == firstDir) break;
                    }
                    if (dirs == null || dirs.Count == 0)
                    {
                        Debug.LogError($"FloorOutline: 외곽선이 {v}에서 끊겼다 - 닫히지 않은 루프를 버린다.");
                        corners = null;
                        break;
                    }
                    int next = TurnLeftFirst(dir, dirs);
                    dirs.Remove(next);
                    if (next != dir) corners.Add(v);
                    dir = next;
                }

                if (corners == null) continue;
                // 시작 꼭짓점이 일직선 중간이면 빼서, 모든 꼭짓점이 실제 꺾이는 모서리가 되게 한다.
                if (corners.Count > 2 && dir == firstDir) corners.RemoveAt(0);
                loops.Add(corners);
            }
        }

        if (used != edgeCount)
        {
            Debug.LogError($"FloorOutline: 경계 선분 {edgeCount}개 중 {used}개만 루프에 들어갔다.");
        }
        return loops;
    }

    // 대각선으로만 맞닿은 바닥 칸은 이어지지 않은 것으로 본다(그 사이 두 칸에는 경계 충돌체가 있어 지나갈 수 없다) -
    // 바닥을 왼쪽에 둔 채 왼쪽 → 직진 → 오른쪽 순으로 고르면 같은 바닥 덩어리를 따라 돌게 된다.
    private static int TurnLeftFirst(int dir, List<int> candidates)
    {
        int left = (dir + 1) % 4, right = (dir + 3) % 4;
        if (candidates.Contains(left)) return left;
        if (candidates.Contains(dir)) return dir;
        if (candidates.Contains(right)) return right;
        return -1;
    }
}
