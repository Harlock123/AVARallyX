namespace AVARallyX.Engine;

/// <summary>
/// A round's course: roads one cell wide between city blocks, cut as a maze and then opened up with plenty of
/// cross streets so there's always another way round. The edge of the course is all blocks.
/// </summary>
public sealed class Course
{
    public const int Cols = Field.Cols, Rows = Field.Rows;
    public readonly bool[,] Road = new bool[Cols, Rows];

    public static bool Inside(int c, int r) => c >= 0 && c < Cols && r >= 0 && r < Rows;
    public bool Open(int c, int r) => Inside(c, r) && Road[c, r];

    public IEnumerable<(int C, int R)> Roads()
    {
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                if (Road[c, r]) yield return (c, r);
    }

    /// <summary>Steps from a cell to every other through cells <paramref name="pass"/> allows (-1 where it can't get).</summary>
    public int[,] Distances(int c, int r, Func<int, int, bool> pass)
    {
        var d = new int[Cols, Rows];
        for (int x = 0; x < Cols; x++)
            for (int y = 0; y < Rows; y++)
                d[x, y] = -1;
        if (!Inside(c, r)) return d;
        var q = new Queue<(int, int)>();
        d[c, r] = 0;
        q.Enqueue((c, r));
        while (q.Count > 0)
        {
            var (x, y) = q.Dequeue();
            foreach (var w in Dirs.All)
            {
                var (dx, dy) = Dirs.Vec(w);
                int nx = x + dx, ny = y + dy;
                if (!Inside(nx, ny) || d[nx, ny] >= 0 || !pass(nx, ny)) continue;
                d[nx, ny] = d[x, y] + 1;
                q.Enqueue((nx, ny));
            }
        }
        return d;
    }

    public static Course Build(Random rng)
    {
        var k = new Course();
        // A maze through the cells at odd places.
        var stack = new Stack<(int C, int R)>();
        k.Road[1, 1] = true;
        stack.Push((1, 1));
        while (stack.Count > 0)
        {
            var (c, r) = stack.Peek();
            var ways = Dirs.All.Select(Dirs.Vec).Where(v => c + 2 * v.X > 0 && c + 2 * v.X < Cols - 1 && r + 2 * v.Y > 0 && r + 2 * v.Y < Rows - 1 && !k.Road[c + 2 * v.X, r + 2 * v.Y]).ToList();
            if (ways.Count == 0) { stack.Pop(); continue; }
            var (dx, dy) = ways[rng.Next(ways.Count)];
            k.Road[c + dx, r + dy] = true;
            k.Road[c + 2 * dx, r + 2 * dy] = true;
            stack.Push((c + 2 * dx, r + 2 * dy));
        }
        // Cross streets: knock through a good share of the walls between roads.
        for (int c = 1; c < Cols - 1; c++)
            for (int r = 1; r < Rows - 1; r++)
            {
                if (k.Road[c, r] || (c % 2 == 0) == (r % 2 == 0)) continue;
                if (rng.NextDouble() < .32) k.Road[c, r] = true;
            }
        // No dead ends.
        for (int pass = 0; pass < 3; pass++)
            for (int c = 1; c < Cols - 1; c += 2)
                for (int r = 1; r < Rows - 1; r += 2)
                {
                    int open = Dirs.All.Count(w => { var (dx, dy) = Dirs.Vec(w); return k.Road[c + dx, r + dy]; });
                    if (open >= 2) continue;
                    var walls = Dirs.All.Select(Dirs.Vec).Where(v => !k.Road[c + v.X, r + v.Y] && c + 2 * v.X > 0 && c + 2 * v.X < Cols - 1 && r + 2 * v.Y > 0 && r + 2 * v.Y < Rows - 1).ToList();
                    if (walls.Count == 0) continue;
                    var (wx, wy) = walls[rng.Next(walls.Count)];
                    k.Road[c + wx, r + wy] = true;
                }
        return k;
    }
}
