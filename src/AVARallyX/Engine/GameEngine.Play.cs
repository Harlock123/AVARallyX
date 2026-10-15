namespace AVARallyX.Engine;

public sealed partial class GameEngine
{
    /// <summary>Speeds in cells a second.</summary>
    public const double CarSpeed = 4.2, EmptySpeed = 2.2;
    public const double FullTank = 100, Burn = 1.25, SmokeCost = 2, SmokeEvery = .35, SmokeLife = 2.5, SpinSeconds = 3;
    public const double StartSeconds = 2.6, DyingSeconds = 2.2, ClearPause = 1.6;
    public const int Flags = 10, FlagPoints = 100, FuelBonus = 20;
    public const int FirstExtraLife = 20000, ExtraLifeEvery = 50000;
    public const int StartX = 17, StartY = 43;

    public int Round { get; private set; } = 1;
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public Course Course { get; private set; } = new();
    public Car Car { get; } = new();
    public List<Pursuer> Pursuers { get; } = [];
    public List<Flag> FlagList { get; } = [];
    public List<(int C, int R)> Rocks { get; } = [];
    public List<Smoke> Smokes { get; } = [];
    public double Fuel { get; private set; } = FullTank;
    /// <summary>Whether the doubling flag has been taken this round.</summary>
    public bool Doubled { get; private set; }
    public int FlagsTaken { get; private set; }
    public RallyPilot Pilot { get; } = new();
    public string Cause { get; private set; } = "";
    public double RoundTime { get; private set; }
    /// <summary>For tests: no pursuit cars and no fuel used, so a test can set up just what it wants.</summary>
    internal bool Calm { get; set; }

    private int _nextExtra, _bonusLeft;
    private bool _firePrev, _lowWarned;
    private double _bonusTick;
    private int[,] _toCar = new int[Field.Cols, Field.Rows];

    private static readonly (int C, int R)[] Homes = [(17, 1), (9, 1), (25, 1), (1, 9), (31, 9), (5, 5), (29, 5), (17, 9)];

    public double Pace => Math.Min(1.4, 1 + .05 * (Round - 1));
    public static int PursuersIn(int round) => Math.Min(7, 2 + round);
    public static int RocksIn(int round) => Math.Min(10, 3 + round);
    public double PursuerSpeed => Math.Min(4.6, 3.3 + .12 * (Round - 1));
    private double Keenness => Math.Min(.92, .72 + .03 * (Round - 1));
    public double Speed => Fuel > 0 ? CarSpeed : EmptySpeed;

    /// <summary>What the next flag will be worth.</summary>
    public int NextFlagPoints => FlagPoints * (FlagsTaken + 1) * (Doubled ? 2 : 1);

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Lives = 3;
        Round = 1;
        _nextExtra = FirstExtraLife;
        LayOut();
        SetPhase(Phase.Start);
    }

    private void BeginRound(int round)
    {
        Round = round;
        LayOut();
        SetPhase(Phase.Start);
        Say($"ROUND {round}", "COLLECT ALL TEN FLAGS", StartSeconds);
        Play(Sfx.RoundStart);
    }

    /// <summary>A new course: its flags (one doubling, one filling the tank) and rocks, the car at the bottom and the pursuers at the top.</summary>
    private void LayOut()
    {
        Course = Course.Build(_rng);
        FlagList.Clear();
        Rocks.Clear();
        Smokes.Clear();
        Effects.Clear();
        Fuel = FullTank;
        Doubled = false;
        FlagsTaken = 0;
        RoundTime = 0;
        _lowWarned = false;
        _bonusLeft = 0;
        var roads = Course.Roads().ToList();
        // Flags well away from the start and from each other.
        Shuffle(roads);
        foreach (var (c, r) in roads)
        {
            if (FlagList.Count == Flags) break;
            if (Math.Abs(c - StartX) + Math.Abs(r - StartY) < 9) continue;
            if (FlagList.Any(f => Math.Abs(f.C - c) + Math.Abs(f.R - r) < 6)) continue;
            FlagList.Add(new Flag { C = c, R = r });
        }
        FlagList[_rng.Next(FlagList.Count)].Kind = FlagKind.Double;
        var plain = FlagList.Where(f => f.Kind == FlagKind.Normal).ToList();
        plain[_rng.Next(plain.Count)].Kind = FlagKind.Fuel;
        // Rocks: never on a flag or near the start, and never cutting a flag off.
        foreach (var (c, r) in roads)
        {
            if (Rocks.Count == RocksIn(Round)) break;
            if (Math.Abs(c - StartX) + Math.Abs(r - StartY) < 6 || FlagList.Any(f => f.C == c && f.R == r) || Homes.Contains((c, r))) continue;
            Rocks.Add((c, r));
            var reach = Course.Distances(StartX, StartY, Passable);
            if (FlagList.Any(f => reach[f.C, f.R] < 0)) Rocks.RemoveAt(Rocks.Count - 1);
        }
        Pursuers.Clear();
        if (!Calm)
            for (int i = 0; i < PursuersIn(Round); i++)
            {
                var (hx, hy) = Homes[i % Homes.Length];
                var p = new Pursuer { HomeX = hx, HomeY = hy };
                p.Place(hx, hy, Dir.Down);
                Pursuers.Add(p);
            }
        PlaceCar();
    }

    private void PlaceCar()
    {
        Car.Place(StartX, StartY, Dir.Up);
        Car.Alive = true;
        Car.SmokeCool = 0;
    }

    private void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>Road with no rock on it.</summary>
    public bool Passable(int c, int r) => Course.Open(c, r) && !Rocks.Contains((c, r));

    // ───────────────────────────── a step of play ─────────────────────────────

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        foreach (var s in Smokes) s.Age += dt;
        Smokes.RemoveAll(s => s.Age > SmokeLife);
        switch (Phase)
        {
            case Phase.Start:
                _firePrev = c.Fire;
                if (PhaseTime > StartSeconds)
                {
                    SetPhase(Phase.Play);
                    if (State == GameState.Playing) Music(true);
                }
                break;
            case Phase.Play:
                RoundTime += dt;
                Burning(dt);
                Drive(c, dt);
                // The last flag ends the round there and then: nothing can crash into it after.
                if (Phase != Phase.Play) break;
                _toCar = Course.Distances(Car.Cx, Car.Cy, Passable);
                Chase(dt);
                Meetings();
                break;
            case Phase.Dying:
                if (PhaseTime > DyingSeconds)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    PlaceCar();
                    // A new car comes with a full tank.
                    Fuel = FullTank;
                    _lowWarned = false;
                    foreach (var p in Pursuers) { p.Place(p.HomeX, p.HomeY, Dir.Down); p.Stunned = 0; }
                    Smokes.Clear();
                    SetPhase(Phase.Start);
                    Say("READY", null, StartSeconds);
                }
                break;
            case Phase.Clear:
                // The fuel left counts up into the score.
                if (_bonusLeft > 0)
                {
                    int step = Math.Min(_bonusLeft, 40);
                    _bonusLeft -= step;
                    Fuel = Math.Max(0, Fuel - step / (double)FuelBonus);
                    AddScore(step);
                    _bonusTick -= dt;
                    if (_bonusTick <= 0) { Play(Sfx.Bonus); _bonusTick = .08; }
                    PhaseTime = 0;
                }
                else if (PhaseTime > ClearPause) BeginRound(Round + 1);
                break;
        }
    }

    private void Burning(double dt)
    {
        if (Calm) return;
        double was = Fuel;
        Fuel = Math.Max(0, Fuel - Burn * dt);
        if (!_lowWarned && Fuel < 20) { _lowWarned = true; Say("LOW FUEL", null, 1.5); Play(Sfx.LowFuel); }
        if (was > 0 && Fuel == 0) { Say("OUT OF FUEL", null, 1.5); Play(Sfx.Empty); }
    }

    // ───────────────────────────── the car ─────────────────────────────

    /// <summary>
    /// The car never stops. At each junction it takes the way the stick points if it can; otherwise it carries
    /// on, and at a bend or a wall it turns whichever way the road goes. Pushing back the way it came turns it
    /// round at once. The button leaves a puff of smoke behind.
    /// </summary>
    private void Drive(Controls c, double dt)
    {
        var k = Car;
        if (!k.Alive) return;
        Dir want = c.Dx != 0 && c.Dy != 0
            ? (Dirs.Horizontal(k.Facing) ? (c.Dy < 0 ? Dir.Up : Dir.Down) : (c.Dx < 0 ? Dir.Left : Dir.Right))
            : c.Dx < 0 ? Dir.Left : c.Dx > 0 ? Dir.Right : c.Dy < 0 ? Dir.Up : c.Dy > 0 ? Dir.Down : Dir.None;
        double dist = Speed * dt;
        for (int guard = 0; guard < 3 && dist > 0; guard++)
        {
            if (k.AtCentre)
            {
                // Rocks don't turn the car: it drives straight into them.
                var d = NextWay(k.Cx, k.Cy, k.Facing, want, Course.Open);
                if (d == Dir.None) break;
                k.Head(d);
            }
            else if (want != Dir.None && want == Dirs.Opposite(k.Dir))
            {
                var (dx, dy) = Dirs.Vec(want);
                (k.Tx, k.Ty) = (k.Tx + dx, k.Ty + dy);
                k.Dir = k.Facing = want;
            }
            dist = k.Glide(dist);
        }
        k.Steer(dt);
        // The smokescreen.
        k.SmokeCool -= dt;
        bool pressed = c.Fire && !_firePrev;
        _firePrev = c.Fire;
        if ((pressed || (c.Fire && k.SmokeCool <= 0)) && k.SmokeCool <= 0 && Fuel > SmokeCost)
        {
            k.SmokeCool = SmokeEvery;
            if (!Calm) Fuel -= SmokeCost;
            var (bx, by) = Dirs.Vec(k.Facing);
            Smokes.Add(new Smoke { X = k.X - bx * .45, Y = k.Y - by * .45 });
            Play(Sfx.Smoke);
        }
        // Flags.
        foreach (var f in FlagList)
        {
            if (f.Taken || Math.Abs(f.C - k.X) + Math.Abs(f.R - k.Y) > .5) continue;
            Take(f);
        }
    }

    /// <summary>Which way a car goes on from a cell: the way wanted, else straight on, else round the bend (right, then left), else back.</summary>
    public static Dir NextWay(int c, int r, Dir facing, Dir want, Func<int, int, bool> pass)
    {
        bool Can(Dir d) { var (dx, dy) = Dirs.Vec(d); return d != Dir.None && pass(c + dx, r + dy); }
        if (want != Dir.None && Can(want)) return want;
        if (Can(facing)) return facing;
        if (Can(Dirs.Right(facing))) return Dirs.Right(facing);
        if (Can(Dirs.Left(facing))) return Dirs.Left(facing);
        return Can(Dirs.Opposite(facing)) ? Dirs.Opposite(facing) : Dir.None;
    }

    private void Take(Flag f)
    {
        f.Taken = true;
        int points = NextFlagPoints;
        FlagsTaken++;
        var (x, y) = (f.C, f.R);
        AddScore(points, x, y);
        switch (f.Kind)
        {
            case FlagKind.Double:
                Doubled = true;
                Say("DOUBLE FLAGS", "EVERY FLAG NOW COUNTS TWICE", 1.8);
                Play(Sfx.Special);
                break;
            case FlagKind.Fuel:
                Fuel = FullTank;
                _lowWarned = false;
                Say("FUEL FLAG", "A FULL TANK", 1.8);
                Play(Sfx.Lucky);
                break;
            default:
                Play(Sfx.Flag);
                break;
        }
        if (FlagsTaken == FlagList.Count)
        {
            SetPhase(Phase.Clear);
            Music(false);
            _bonusLeft = (int)Math.Round(Fuel) * FuelBonus;
            Say("ROUND CLEAR", $"FUEL BONUS {_bonusLeft}", 3);
            Play(Sfx.Clear);
        }
    }

    // ───────────────────────────── the pursuers ─────────────────────────────

    private void Chase(double dt)
    {
        foreach (var p in Pursuers)
        {
            if (p.Stunned > 0)
            {
                p.Stunned -= dt;
                p.Spin += dt * 12;
                p.Angle += dt * 12;
                if (p.Stunned <= 0) p.Angle = Dirs.Angle(p.Facing);
                continue;
            }
            double dist = PursuerSpeed * dt;
            for (int guard = 0; guard < 3 && dist > 0; guard++)
            {
                if (p.AtCentre)
                {
                    var d = Pick(p);
                    if (d == Dir.None) break;
                    p.Head(d);
                }
                dist = p.Glide(dist);
            }
            p.Steer(dt);
            // Into a cloud of smoke: spun out.
            if (Smokes.Any(s => Math.Abs(s.X - p.X) + Math.Abs(s.Y - p.Y) < .75))
            {
                p.Stunned = SpinSeconds;
                Play(Sfx.Spin);
            }
        }
    }

    /// <summary>A pursuer's way on from a junction: usually the way that brings it nearest the car, now and then any way at all.</summary>
    private Dir Pick(Pursuer p)
    {
        var opts = Dirs.All.Where(d => { var (dx, dy) = Dirs.Vec(d); return Passable(p.Cx + dx, p.Cy + dy); }).ToList();
        if (opts.Count > 1) opts.Remove(Dirs.Opposite(p.Facing));
        if (opts.Count == 0) return Dir.None;
        if (_rng.NextDouble() < Keenness)
        {
            Dir best = Dir.None;
            int bd = int.MaxValue;
            foreach (var d in opts)
            {
                var (dx, dy) = Dirs.Vec(d);
                int v = _toCar[p.Cx + dx, p.Cy + dy];
                if (v >= 0 && v < bd) { bd = v; best = d; }
            }
            if (best != Dir.None) return best;
        }
        return opts[_rng.Next(opts.Count)];
    }

    // ───────────────────────────── crashes ─────────────────────────────

    private void Meetings()
    {
        var k = Car;
        if (!k.Alive) return;
        foreach (var p in Pursuers)
            if (p.Stunned <= 0 && Math.Abs(p.X - k.X) + Math.Abs(p.Y - k.Y) < .75) { Crash("CAUGHT"); return; }
        foreach (var (c, r) in Rocks)
            if (Math.Abs(c - k.X) + Math.Abs(r - k.Y) < .6) { Crash("ROCK"); return; }
    }

    private void Crash(string cause)
    {
        Cause = cause;
        Car.Alive = false;
        Lives--;
        Effects.Add(new Effect { Kind = EffectKind.Burst, X = Car.X, Y = Car.Y, Life = 1.4, Size = 1.4, Colour = 0xFFFFE14A });
        SetPhase(Phase.Dying);
        Music(false);
        Play(Sfx.Crash);
    }

    private void AddScore(int points, double? x = null, double? y = null)
    {
        Score += points;
        if (x is { } px && y is { } py)
            Effects.Add(new Effect { Kind = EffectKind.Score, X = px, Y = py, Text = points.ToString(), Colour = points >= 1000 ? 0xFFFFE14A : 0xFFFFFFFF, Life = 1.2 });
        while (Score >= _nextExtra)
        {
            _nextExtra += ExtraLifeEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into the play of a round.</summary>
    internal void Go(int round = 1)
    {
        StartGame();
        BeginRound(round);
        SetPhase(Phase.Play);
        Message = SubMessage = null;
    }

    /// <summary>An open grid of streets (every odd row and column a road), with no flags, rocks or pursuers.</summary>
    internal void OpenCourse()
    {
        Course = new Course();
        for (int c = 1; c < Field.Cols - 1; c++)
            for (int r = 1; r < Field.Rows - 1; r++)
                Course.Road[c, r] = c % 2 == 1 || r % 2 == 1;
        FlagList.Clear();
        Rocks.Clear();
        Pursuers.Clear();
        Smokes.Clear();
    }

    internal void PutCar(int c, int r, Dir facing)
    {
        Car.Place(c, r, facing);
        Car.Alive = true;
    }

    internal Pursuer PutPursuer(int c, int r, Dir facing)
    {
        var p = new Pursuer { HomeX = c, HomeY = r };
        p.Place(c, r, facing);
        Pursuers.Add(p);
        return p;
    }

    internal Flag PutFlag(int c, int r, FlagKind kind = FlagKind.Normal)
    {
        var f = new Flag { C = c, R = r, Kind = kind };
        FlagList.Add(f);
        return f;
    }

    internal void PutRock(int c, int r) => Rocks.Add((c, r));
    internal void SetFuel(double f) => Fuel = f;
    internal void SetScore(int s) => Score = s;
    internal void SetLives(int n) => Lives = n;
}
