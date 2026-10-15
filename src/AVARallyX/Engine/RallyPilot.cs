namespace AVARallyX.Engine;

/// <summary>
/// The computer driver, for the demonstration (and the tests). It plans the cheapest way to each flag left,
/// where roads near a pursuit car cost dear (and the cars' own cells cost dearer still), and drives the first
/// step of the best. When a pursuer gets close behind it lays smoke.
/// </summary>
public sealed class RallyPilot
{
    /// <summary>What it is up to, for the probe.</summary>
    public string Doing { get; private set; } = "";

    public Controls Decide(GameEngine g)
    {
        var k = g.Car;
        if (!k.Alive || g.Phase is not (Phase.Play or Phase.Start)) return default;
        var course = g.Course;
        int cols = Field.Cols, rows = Field.Rows;
        // The cell it's heading into is where it plans from.
        int sc = k.Tx, sr = k.Ty;
        var danger = new double[cols, rows];
        foreach (var p in g.Pursuers)
        {
            if (p.Stunned > .6) continue;
            foreach (var (c, r) in new[] { (p.Cx, p.Cy), (p.Tx, p.Ty) })
                for (int dc = -3; dc <= 3; dc++)
                    for (int dr = -3; dr <= 3; dr++)
                    {
                        int x = c + dc, y = r + dr, m = Math.Abs(dc) + Math.Abs(dr);
                        if (m > 3 || !Course.Inside(x, y)) continue;
                        danger[x, y] = Math.Max(danger[x, y], m switch { 0 => 400, 1 => 120, 2 => 30, _ => 8 });
                    }
        }
        // Cheapest ways out from where it is (it can't turn straight back unless it must).
        var cost = new double[cols, rows];
        var first = new Dir[cols, rows];
        for (int x = 0; x < cols; x++)
            for (int y = 0; y < rows; y++)
                cost[x, y] = double.MaxValue;
        var open = new PriorityQueue<(int C, int R), double>();
        cost[sc, sr] = 0;
        open.Enqueue((sc, sr), 0);
        while (open.TryDequeue(out var at, out double d))
        {
            if (d > cost[at.C, at.R]) continue;
            foreach (var w in Dirs.All)
            {
                if (at == (sc, sr) && w == Dirs.Opposite(k.Facing) && !k.AtCentre) continue;
                var (dx, dy) = Dirs.Vec(w);
                int nx = at.C + dx, ny = at.R + dy;
                if (!g.Passable(nx, ny)) continue;
                double nd = d + 1 + danger[nx, ny];
                if (nd >= cost[nx, ny]) continue;
                cost[nx, ny] = nd;
                first[nx, ny] = at == (sc, sr) ? w : first[at.C, at.R];
                open.Enqueue((nx, ny), nd);
            }
        }
        Flag? target = null;
        double best = double.MaxValue;
        foreach (var f in g.FlagList)
        {
            if (f.Taken || cost[f.C, f.R] == double.MaxValue) continue;
            // The doubling flag early is worth a detour.
            double c = cost[f.C, f.R] - (f.Kind == FlagKind.Double && !g.Doubled ? 8 : 0) - (f.Kind == FlagKind.Fuel && g.Fuel < 35 ? 12 : 0);
            if (c < best) { best = c; target = f; }
        }
        Dir go = Dir.None;
        if (target != null)
        {
            go = (target.C, target.R) == (sc, sr) ? k.Facing : first[target.C, target.R];
            Doing = best > 100 ? "risky" : "flag";
        }
        else
        {
            // Nowhere safe: the way with the least danger near.
            Doing = "evade";
            double low = double.MaxValue;
            foreach (var w in Dirs.All)
            {
                var (dx, dy) = Dirs.Vec(w);
                if (g.Passable(sc + dx, sr + dy) && danger[sc + dx, sr + dy] < low) { low = danger[sc + dx, sr + dy]; go = w; }
            }
        }
        // Smoke for anything close behind.
        var (fx, fy) = Dirs.Vec(k.Facing);
        bool chased = g.Pursuers.Any(p => p.Stunned <= 0 && Math.Abs(p.X - k.X) + Math.Abs(p.Y - k.Y) < 3.2 && ((p.X - k.X) * fx + (p.Y - k.Y) * fy) < -.3);
        var (gx, gy) = Dirs.Vec(go);
        return new Controls(gx, gy, chased && g.Fuel > 15);
    }
}
