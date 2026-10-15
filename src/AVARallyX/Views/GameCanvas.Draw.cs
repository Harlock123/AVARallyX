using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVARallyX.Engine;
using Effect = AVARallyX.Engine.Effect;

namespace AVARallyX.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVARallyX/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F4FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF9AA4B8, Blue = 0xFF3A8CFF, Green = 0xFF4AE06A;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);

    /// <summary>Where a point on the course (in cells) is on the screen, seen from the car.</summary>
    private Point View(double x, double y)
    {
        var k = Engine.Car;
        return new Point(Field.ViewX + (x - k.X) * Field.Cell, Field.ViewY + (y - k.Y) * Field.Cell);
    }

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF0A0C10), new Rect(0, 0, Field.Width, Field.Height));
        bool inGame = g.State is GameState.Playing or GameState.Attract or GameState.Paused or GameState.GameOver;
        if (inGame)
        {
            using (dc.PushClip(new Rect(Field.ViewLeft, Field.ViewTop, Field.ViewW, Field.ViewH)))
            {
                DrawCourse(dc);
                foreach (var e in g.Effects) DrawEffect(dc, e);
                DrawMessages(dc);
            }
            DrawPanel(dc);
        }
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.ViewX, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.ViewX, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(Field.ViewX, 330), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER", new Point(Field.ViewX, Field.Height - 18), 26, Yellow, center: true);
    }

    // ───────────────────────────── the course ─────────────────────────────

    private void DrawCourse(DrawingContext dc)
    {
        var g = Engine;
        var k = g.Car;
        double cell = Field.Cell;
        int c0 = (int)Math.Floor(k.X - Field.ViewW / 2 / cell) - 1, c1 = (int)Math.Ceiling(k.X + Field.ViewW / 2 / cell) + 1;
        int r0 = (int)Math.Floor(k.Y - Field.ViewH / 2 / cell) - 1, r1 = (int)Math.Ceiling(k.Y + Field.ViewH / 2 / cell) + 1;
        dc.FillRectangle(Brush(0xFF1A1C22), new Rect(Field.ViewLeft, Field.ViewTop, Field.ViewW, Field.ViewH));
        for (int c = c0; c <= c1; c++)
            for (int r = r0; r <= r1; r++)
            {
                var p = View(c, r);
                var rect = new Rect(p.X - cell / 2, p.Y - cell / 2, cell, cell);
                if (g.Course.Open(c, r))
                {
                    // Lane markings down the middle of each road.
                    bool across = g.Course.Open(c - 1, r) || g.Course.Open(c + 1, r), down = g.Course.Open(c, r - 1) || g.Course.Open(c, r + 1);
                    if (across && !down) dc.DrawLine(Pen(0x60FFE14A, 2), new Point(rect.X + 10, p.Y), new Point(rect.Right - 22, p.Y));
                    if (down && !across) dc.DrawLine(Pen(0x60FFE14A, 2), new Point(p.X, rect.Y + 10), new Point(p.X, rect.Bottom - 22));
                    continue;
                }
                DrawBlock(dc, c, r, rect);
            }
        foreach (var f in g.FlagList.Where(f => !f.Taken)) DrawFlag(dc, View(f.C, f.R), f.Kind, 1);
        foreach (var (c, r) in g.Rocks) DrawRock(dc, View(c, r), c, r);
        foreach (var s in g.Smokes)
        {
            double t = s.Age / GameEngine.SmokeLife;
            var p = View(s.X, s.Y);
            for (int i = 0; i < 4; i++)
            {
                double a = i * 1.7 + s.Age, rr = 10 + 16 * Math.Sqrt(t);
                dc.DrawEllipse(Brush(Alpha(0xFFC8C8D0, .55 * (1 - t))), null, new Point(p.X + Math.Cos(a) * 8, p.Y + Math.Sin(a) * 8), rr, rr);
            }
        }
        foreach (var pu in g.Pursuers) DrawCar(dc, View(pu.X, pu.Y), pu.Angle, 0xFFE02A2A, 1, pu.Stunned > 0);
        if (k.Alive) DrawCar(dc, View(k.X, k.Y), k.Angle, Blue, 1, false);
    }

    /// <summary>A city block: a park with trees, or a building with a lit window or two.</summary>
    private void DrawBlock(DrawingContext dc, int c, int r, Rect rect)
    {
        uint h = Hash(c, r, 3);
        bool park = h % 3 == 0;
        var inner = rect.Deflate(2);
        if (park)
        {
            dc.FillRectangle(Brush(0xFF2A6A30), inner, 4);
            for (int i = 0; i < 2; i++)
            {
                var t = new Point(inner.X + 10 + (h >> (i * 4)) % 26, inner.Y + 10 + (h >> (i * 4 + 8)) % 26);
                dc.DrawEllipse(Brush(0xFF3A9A40), Pen(0xFF1A4A20, 1), t, 7, 7);
            }
        }
        else
        {
            uint[] roofs = [0xFF5A5A6A, 0xFF6A5048, 0xFF4A5A70, 0xFF6A6A58];
            uint roof = roofs[h % 4];
            dc.FillRectangle(Brush(roof), inner, 3);
            dc.DrawRectangle(null, Pen(0xFF8A8A9A, 1.2), inner.Deflate(4), 2);
            if (h % 5 < 2) dc.FillRectangle(Brush(0xFFFFE08A), new Rect(inner.X + 12, inner.Y + 12, 6, 6));
        }
    }

    private static void DrawFlag(DrawingContext dc, Point p, FlagKind kind, double scale)
    {
        double k = scale;
        uint colour = kind switch { FlagKind.Double => 0xFFFF4A6A, FlagKind.Fuel => Green, _ => Yellow };
        dc.DrawLine(Pen(0xFFE0E0E0, 2), new Point(p.X - 8 * k, p.Y + 14 * k), new Point(p.X - 8 * k, p.Y - 14 * k));
        Poly(dc, colour, 0xFF202020, 1, new Point(p.X - 7 * k, p.Y - 14 * k), new Point(p.X + 12 * k, p.Y - 8 * k), new Point(p.X - 7 * k, p.Y - 1 * k));
        string mark = kind switch { FlagKind.Double => "x2", FlagKind.Fuel => "F", _ => "" };
        if (mark != "") Text(dc, mark, new Point(p.X + 1 * k, p.Y - 7 * k), 15 * k, 0xFF101010, center: true, shadow: false);
    }

    private static void DrawRock(DrawingContext dc, Point p, int c, int r)
    {
        var pts = Enumerable.Range(0, 8).Select(i =>
        {
            double a = i * Math.PI / 4, rr = 14 + Hash(c + i, r, 7) % 6;
            return new Point(p.X + Math.Cos(a) * rr, p.Y + Math.Sin(a) * rr);
        }).ToArray();
        Poly(dc, 0xFF6A6460, 0xFF2A2624, 2, pts);
        dc.DrawEllipse(Brush(0xFF8A847E), null, new Point(p.X - 4, p.Y - 5), 5, 3.5);
    }

    /// <summary>A rally car seen from above, nose to its heading. A spun-out one turns round and round with stars over it.</summary>
    private void DrawCar(DrawingContext dc, Point at, double angle, uint colour, double scale, bool dazed)
    {
        double k = scale, sn = Math.Sin(angle), cs = Math.Cos(angle);
        Point P(double x, double y) => new(at.X + (x * cs - y * sn) * k, at.Y + (x * sn + y * cs) * k);
        // Wheels.
        foreach (var (wx, wy) in new[] { (-10.0, -10.0), (10.0, -10.0), (-10.0, 10.0), (10.0, 10.0) })
            Poly(dc, 0xFF181818, null, 0, P(wx - 3, wy - 5), P(wx + 3, wy - 5), P(wx + 3, wy + 5), P(wx - 3, wy + 5));
        Poly(dc, colour, 0xFF101010, 1.5, P(-8, -17), P(8, -17), P(10, -8), P(10, 15), P(-10, 15), P(-10, -8));
        Poly(dc, 0xFFA8D8FF, null, 0, P(-7, -9), P(7, -9), P(6, -3), P(-6, -3));
        Poly(dc, 0xFF203040, null, 0, P(-6, 6), P(6, 6), P(7, 10), P(-7, 10));
        dc.DrawLine(Pen(0xFFFFFFFF, 2), P(0, -16), P(0, 14));
        dc.DrawEllipse(Brush(0xFFFFF4C0), null, P(-6, -16), 2 * k, 1.5 * k);
        dc.DrawEllipse(Brush(0xFFFFF4C0), null, P(6, -16), 2 * k, 1.5 * k);
        if (dazed)
            for (int i = 0; i < 3; i++)
            {
                double a = _clock * 6 + i * 2.1;
                dc.DrawEllipse(Brush(Yellow), null, new Point(at.X + Math.Cos(a) * 18, at.Y - 18 + Math.Sin(a) * 5), 3, 3);
            }
    }

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        var c = View(e.X, e.Y);
        switch (e.Kind)
        {
            case EffectKind.Burst:
            {
                double r = (10 + 50 * t) * e.Size;
                dc.DrawEllipse(Brush(Alpha(0xFFFFE080, (1 - t) * .5)), null, c, r * .6, r * .6);
                dc.DrawEllipse(null, Pen(Alpha(0xFFFF8A2A, 1 - t), 3), c, r, r);
                for (int k = 0; k < 10; k++)
                {
                    double a = k * Math.PI / 5 + Hash(k, (int)(e.X * 10), 1) % 10 * .06, d = r * (.7 + Hash(k, 2, 3) % 10 * .05);
                    dc.FillRectangle(Brush(Alpha(k % 2 == 0 ? 0xFFFFFFFF : e.Colour, 1 - t)), new Rect(c.X + Math.Cos(a) * d - 2.5, c.Y + Math.Sin(a) * d - 2.5, 5, 5));
                }
                break;
            }
            case EffectKind.Score:
                Text(dc, e.Text, new Point(c.X, c.Y - 20 - t * 30), 30, e.Colour, center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 30), 36, e.Colour, center: true);
                break;
        }
    }

    // ───────────────────────────── the panel and the screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        Text(dc, "SCORE", new Point(16, 18), 22, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(16, 48), 36, Yellow);
        Text(dc, "HI", new Point(Field.Width - 16, 18), 22, Dim, right: true);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(Field.Width - 16, 48), 30, Dim, right: true);
        if (g.State is GameState.Playing or GameState.Attract or GameState.Paused)
            Text(dc, $"ROUND {g.Round}", new Point(Field.ViewX, 36), 38, White, center: true);
    }

    /// <summary>The panel: the radar with every flag and car on it, the fuel gauge, the flags to come and the cars left.</summary>
    private void DrawPanel(DrawingContext dc)
    {
        var g = Engine;
        double x0 = Field.ViewRight, w = Field.Width - x0, cxp = x0 + w / 2;
        dc.FillRectangle(Brush(0xFF0C0E16), new Rect(x0, Field.ViewTop, w, Field.ViewH));
        dc.DrawLine(Pen(0xFF2A3450, 2), new Point(x0, Field.ViewTop), new Point(x0, Field.ViewBottom));
        // The radar.
        double k = 5.6, rw = Field.Cols * k, rh = Field.Rows * k, rx = cxp - rw / 2, ry = 82;
        dc.FillRectangle(Brush(0xFF04120A), new Rect(rx - 4, ry - 4, rw + 8, rh + 8));
        for (int c = 0; c < Field.Cols; c++)
            for (int r = 0; r < Field.Rows; r++)
                if (g.Course.Road[c, r]) dc.FillRectangle(Brush(0xFF1A3A26), new Rect(rx + c * k, ry + r * k, k, k));
        dc.DrawRectangle(null, Pen(0xFF2A8A50, 2), new Rect(rx - 4, ry - 4, rw + 8, rh + 8));
        bool blink = (int)(_clock * 4) % 2 == 0;
        foreach (var f in g.FlagList.Where(f => !f.Taken))
            dc.FillRectangle(Brush(f.Kind == FlagKind.Double ? 0xFFFF4A6A : f.Kind == FlagKind.Fuel ? Green : Yellow), new Rect(rx + f.C * k - 1, ry + f.R * k - 1, k + 2, k + 2));
        foreach (var p in g.Pursuers)
            dc.FillRectangle(Brush(Red), new Rect(rx + p.X * k, ry + p.Y * k, k, k));
        if (blink || !g.Car.Alive) dc.FillRectangle(Brush(0xFFFFFFFF), new Rect(rx + g.Car.X * k - 1, ry + g.Car.Y * k - 1, k + 2, k + 2));
        // Fuel.
        double fy = ry + rh + 30;
        Text(dc, "FUEL", new Point(rx, fy), 26, Dim);
        var bar = new Rect(rx + 52, fy - 10, rw - 52, 20);
        double frac = g.Fuel / GameEngine.FullTank;
        uint fc = frac > .35 ? Green : frac > .15 ? Yellow : Red;
        dc.FillRectangle(Brush(0xFF1A1A22), bar);
        if (!(frac < .15 && !blink)) dc.FillRectangle(Brush(fc), new Rect(bar.X, bar.Y, bar.Width * frac, bar.Height));
        dc.DrawRectangle(null, Pen(Dim, 1.5), bar);
        // The flags still to take, and what the next is worth.
        double gy = fy + 40;
        int left = g.FlagList.Count(f => !f.Taken);
        for (int i = 0; i < left; i++) DrawFlag(dc, new Point(rx + 12 + i % 5 * 38, gy + i / 5 * 36), FlagKind.Normal, .75);
        Text(dc, $"NEXT FLAG {g.NextFlagPoints}", new Point(cxp, gy + 84), 24, g.Doubled ? 0xFFFF6A8A : White, center: true);
        // Cars left.
        int spare = Math.Max(0, g.Lives - (g.Car.Alive ? 1 : 0));
        for (int i = 0; i < Math.Min(spare, 6); i++) DrawCar(dc, new Point(rx + 16 + i * 34, gy + 126), 0, Blue, .8, false);
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.ViewX, cy = Field.ViewY - 150;
        var (w, h) = (Math.Min(Field.ViewW - 40, Math.Max(msg.Length * 28, (g.SubMessage?.Length ?? 0) * 15) + 60), g.SubMessage != null ? 110.0 : 74.0);
        dc.FillRectangle(Brush(0xC0080A10), new Rect(cx - w / 2, cy - 48, w, h), 6);
        uint colour = msg.Contains("FUEL") && !msg.Contains("FLAG") ? Red : g.Phase == Phase.Dying ? Red : Yellow;
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 14 ? 48 : 56, colour, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 36), 28, White, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        Text(dc, "AVA RALLY-X", new Point(cx, 114), 116, 0xFF4AB0FF, center: true);
        Text(dc, "AFTER THE 1980 ARCADE CLASSIC", new Point(cx, 184), 28, Dim, center: true);
        DrawCar(dc, new Point(cx - 210, 270), Math.PI / 2, Blue, 2, false);
        for (int i = 0; i < 4; i++)
        {
            double a = _clock * 3 + i * 1.6;
            dc.DrawEllipse(Brush(Alpha(0xFFC8C8D0, .5)), null, new Point(cx - 270 - i * 22 + Math.Cos(a) * 4, 270 + Math.Sin(a) * 6), 12 + i * 3, 12 + i * 3);
        }
        DrawCar(dc, new Point(cx - 80, 270), Math.PI / 2, 0xFFE02A2A, 2, true);
        DrawFlag(dc, new Point(cx + 110, 270), FlagKind.Normal, 2);
        DrawFlag(dc, new Point(cx + 210, 270), FlagKind.Double, 2);
        DrawFlag(dc, new Point(cx + 310, 270), FlagKind.Fuel, 2);
        string[] lines =
        [
            "COLLECT ALL TEN FLAGS - EACH IS WORTH MORE THAN THE LAST",
            "THE x2 FLAG DOUBLES THE REST; THE F FLAG FILLS THE TANK",
            "LAY SMOKE TO SPIN OUT THE PURSUIT CARS - IT COSTS FUEL",
            "MIND THE ROCKS, AND DON'T RUN DRY",
        ];
        for (int i = 0; i < lines.Length; i++) Text(dc, lines[i], new Point(cx, 352 + i * 32), 25, White, center: true);
        Text(dc, "FLAGS 100, 200, 300 ...   FUEL LEFT AT THE FINISH x 20", new Point(cx, 500), 25, Yellow, center: true);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER OR START", new Point(cx, 590), 46, Yellow, center: true);
        Text(dc, "ARROWS: STEER    SPACE: SMOKESCREEN", new Point(cx, 660), 26, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "THE FASTEST DRIVERS", new Point(cx, 130), 64, 0xFF4AB0FF, center: true);
        Text(dc, "RANK NAME    SCORE   ROUND", new Point(cx, 196), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4} ", new Point(cx, 240 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 700), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
