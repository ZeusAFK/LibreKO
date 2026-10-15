namespace LibreKO.Game.World;

public static class NpcPathfinder
{
    private const int MaxSearchNodes = 256; // limit search to prevent lag on unreachable targets
    private static readonly (int dx, int dz, float cost)[] Neighbors =
    [
        (-1, 0, 1.0f), (1, 0, 1.0f), (0, -1, 1.0f), (0, 1, 1.0f),     // cardinal
        (-1, -1, 1.414f), (-1, 1, 1.414f), (1, -1, 1.414f), (1, 1, 1.414f) // diagonal
    ];

    public static (float x, float z)? FindNextStep(SmdFile map, float startX, float startZ, float targetX, float targetZ)
    {
        float unit = map.UnitDistance;
        int sx = (int)(startX / unit);
        int sz = (int)(startZ / unit);
        int tx = (int)(targetX / unit);
        int tz = (int)(targetZ / unit);

        // If already at target tile, return target directly
        if (sx == tx && sz == tz)
            return (targetX, targetZ);

        var path = FindPath(map, sx, sz, tx, tz);
        if (path == null || path.Count < 2)
        {
            if (IsLineClear(map, sx, sz, tx, tz))
                return (targetX, targetZ);

            return null; // no path or already at target
        }

        // Return the world position of the next tile in the path (index 1, since 0 is current)
        var (x, z) = path[1];
        return ((x + 0.5f) * unit, (z + 0.5f) * unit);
    }

    private static bool IsLineClear(SmdFile map, int x0, int z0, int x1, int z1)
    {
        int dx = Math.Abs(x1 - x0);
        int dz = Math.Abs(z1 - z0);
        int sx = x0 < x1 ? 1 : -1;
        int sz = z0 < z1 ? 1 : -1;
        int err = dx - dz;

        while (true)
        {
            if (!IsWalkable(map, x0, z0))
                return false;

            if (x0 == x1 && z0 == z1)
                return true;

            int e2 = 2 * err;
            if (e2 > -dz) { err -= dz; x0 += sx; }
            if (e2 < dx) { err += dx; z0 += sz; }
        }
    }

    private static bool IsWalkable(SmdFile map, int x, int z)
    {
        var evt = map.GetEventId(x, z);
        if (evt is 0 or 30 or 31 or 32 or 35) return true;
        if (map.MapSize == 257 && map.UnitDistance == 4 && evt == 1)
        {
            float h = map.GetHeight(x * 4, z * 4);
            return h is >= 75f and <= 81f && x >= 30 && z >= 42;
        }
        return false;
    }

    private static List<(int x, int z)>? FindPath(SmdFile map, int sx, int sz, int tx, int tz)
    {
        int mapSize = map.MapSize;

        // If start tile is blocked, find closest walkable adjacent tile
        if (!IsWalkable(map, sx, sz))
        {
            var bestDist = float.MaxValue;
            int bestSx = -1, bestSz = -1;
            foreach (var (dx, dz, _) in Neighbors)
            {
                int nx = sx + dx;
                int nz = sz + dz;
                if (nx >= 0 && nx < mapSize && nz >= 0 && nz < mapSize && IsWalkable(map, nx, nz))
                {
                    float d = Heuristic(nx, nz, tx, tz);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        bestSx = nx;
                        bestSz = nz;
                    }
                }
            }
            if (bestSx != -1)
            {
                sx = bestSx;
                sz = bestSz;
            }
        }

        // Open set as a priority queue (min-heap by fScore)
        var openSet = new PriorityQueue<long, float>();
        var gScore = new Dictionary<long, float>();
        var cameFrom = new Dictionary<long, long>();

        long startKey = PackKey(sx, sz);
        long goalKey = PackKey(tx, tz);

        gScore[startKey] = 0;
        openSet.Enqueue(startKey, Heuristic(sx, sz, tx, tz));

        int nodesExpanded = 0;

        while (openSet.Count > 0 && nodesExpanded < MaxSearchNodes)
        {
            var currentKey = openSet.Dequeue();
            nodesExpanded++;

            if (currentKey == goalKey)
                return ReconstructPath(cameFrom, currentKey);

            int cx = (int)(currentKey >> 16);
            int cz = (int)(currentKey & 0xFFFF);

            float currentG = gScore[currentKey];

            foreach (var (dx, dz, cost) in Neighbors)
            {
                int nx = cx + dx;
                int nz = cz + dz;

                if (nx < 0 || nx >= mapSize || nz < 0 || nz >= mapSize)
                    continue;

                if (!IsWalkable(map, nx, nz))
                    continue; // blocked tile

                // For diagonal moves, check that both adjacent cardinal tiles are also walkable
                if (dx != 0 && dz != 0)
                {
                    if (!IsWalkable(map, cx + dx, cz) || !IsWalkable(map, cx, cz + dz))
                        continue; // can't cut corners
                }

                long neighborKey = PackKey(nx, nz);
                float tentativeG = currentG + cost;

                if (!gScore.TryGetValue(neighborKey, out var bestG) || tentativeG < bestG)
                {
                    gScore[neighborKey] = tentativeG;
                    cameFrom[neighborKey] = currentKey;
                    float f = tentativeG + Heuristic(nx, nz, tx, tz);
                    openSet.Enqueue(neighborKey, f);
                }
            }
        }

        // If we hit the search limit, return the path to the closest node we found
        if (nodesExpanded >= MaxSearchNodes && cameFrom.Count > 0)
        {
            // Find the explored node closest to goal
            long bestKey = startKey;
            float bestDist = float.MaxValue;
            foreach (var key in gScore.Keys)
            {
                int kx = (int)(key >> 16);
                int kz = (int)(key & 0xFFFF);
                float d = Heuristic(kx, kz, tx, tz);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestKey = key;
                }
            }
            if (bestKey != startKey)
                return ReconstructPath(cameFrom, bestKey);
        }

        return null; // no path found
    }

    private static float Heuristic(int x1, int z1, int x2, int z2)
    {
        // Octile distance (consistent with 8-directional movement)
        int dx = Math.Abs(x2 - x1);
        int dz = Math.Abs(z2 - z1);
        return Math.Max(dx, dz) + 0.414f * Math.Min(dx, dz);
    }

    private static long PackKey(int x, int z) => ((long)x << 16) | (uint)(z & 0xFFFF);

    private static List<(int x, int z)> ReconstructPath(Dictionary<long, long> cameFrom, long current)
    {
        var path = new List<(int x, int z)>();
        while (true)
        {
            int x = (int)(current >> 16);
            int z = (int)(current & 0xFFFF);
            path.Add((x, z));

            if (!cameFrom.TryGetValue(current, out var prev))
                break;
            current = prev;
        }
        path.Reverse();
        return path;
    }
}
