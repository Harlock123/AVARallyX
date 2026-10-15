using AVARallyX.Engine;

namespace AVARallyX.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avarallyx-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A round in play on an open grid of streets, with nothing on it, no pursuers and no fuel burned.</summary>
    private static GameEngine Game()
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.Go(1);
        g.OpenCourse();
        g.PutFlag(29, 41);
        return g;
    }

    /// <summary>The same, but burning fuel.</summary>
    private static GameEngine Real()
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(1);
        g.OpenCourse();
        g.PutFlag(29, 41);
        g.SetLives(99);
        return g;
    }

    private static Controls Press(Dir d, bool fire = false)
    {
        var (dx, dy) = Dirs.Vec(d);
        return new Controls(dx, dy, fire);
    }

    // ───────────────────────────── the course ─────────────────────────────

    [Fact]
    public void Every_course_is_all_joined_up_with_ten_flags_all_reachable_past_the_rocks()
    {
        for (int round = 1; round <= 8; round++)
        {
            var g = new GameEngine(round, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.Go(round);
            var k = g.Course;
            var reach = k.Distances(GameEngine.StartX, GameEngine.StartY, k.Open);
            Assert.All(k.Roads(), p => Assert.True(reach[p.C, p.R] >= 0));
            for (int c = 0; c < Field.Cols; c++) Assert.False(k.Road[c, 0] || k.Road[c, Field.Rows - 1]);
            for (int r = 0; r < Field.Rows; r++) Assert.False(k.Road[0, r] || k.Road[Field.Cols - 1, r]);
            Assert.Equal(GameEngine.Flags, g.FlagList.Count);
            Assert.Single(g.FlagList, f => f.Kind == FlagKind.Double);
            Assert.Single(g.FlagList, f => f.Kind == FlagKind.Fuel);
            Assert.Equal(GameEngine.RocksIn(round), g.Rocks.Count);
            var past = k.Distances(GameEngine.StartX, GameEngine.StartY, g.Passable);
            Assert.All(g.FlagList, f => Assert.True(past[f.C, f.R] >= 0));
            Assert.Equal(GameEngine.PursuersIn(round), g.Pursuers.Count);
        }
    }

    // ───────────────────────────── the car ─────────────────────────────

    [Fact]
    public void The_car_never_stops_and_takes_the_turn_the_stick_points_at_the_next_junction()
    {
        var g = Game();
        g.PutCar(5, 5, Dir.Right);
        g.Idle(60);
        Assert.Equal(5 + GameEngine.CarSpeed, g.Car.X, 1);
        g.PutCar(4, 5, Dir.Right);
        g.Run(40, Press(Dir.Down));
        Assert.Equal(5, g.Car.X);
        Assert.True(g.Car.Y > 5);
    }

    [Fact]
    public void At_a_bend_the_car_turns_itself_and_pushing_back_turns_it_round_at_once()
    {
        var g = Game();
        g.PutCar(1, 3, Dir.Up);
        g.Idle(40);
        Assert.Equal(1, g.Car.Y);
        Assert.True(g.Car.X > 1);
        g.PutCar(5, 5, Dir.Right);
        g.Idle(5);
        g.Run(1, Press(Dir.Left));
        Assert.Equal(Dir.Left, g.Car.Dir);
    }

    // ───────────────────────────── flags and fuel ─────────────────────────────

    [Fact]
    public void Each_flag_is_worth_more_than_the_last_the_double_flag_doubles_the_rest_and_the_fuel_flag_fills_the_tank()
    {
        var g = Game();
        g.PutFlag(7, 5);
        g.PutFlag(9, 5, FlagKind.Double);
        g.PutFlag(11, 5);
        g.PutFlag(13, 5, FlagKind.Fuel);
        g.SetFuel(30);
        g.PutCar(5, 5, Dir.Right);
        g.Idle(150);
        Assert.Equal(100 + 200 + 600 + 800, g.Score);
        Assert.True(g.Doubled);
        Assert.Equal(GameEngine.FullTank, g.Fuel);
    }

    [Fact]
    public void The_last_flag_clears_the_round_and_the_fuel_left_counts_up_as_a_bonus()
    {
        var g = Game();
        g.FlagList.Clear();
        g.PutFlag(7, 5);
        g.SetFuel(50);
        g.PutCar(5, 5, Dir.Right);
        g.Idle(40);
        Assert.Equal(Phase.Clear, g.Phase);
        g.Idle(60 * 4);
        Assert.Equal(2, g.Round);
        Assert.Equal(GameEngine.FlagPoints + 50 * GameEngine.FuelBonus, g.Score);
        Assert.Equal(GameEngine.RocksIn(2), g.Rocks.Count);
    }

    [Fact]
    public void Fuel_burns_away_and_an_empty_tank_slows_the_car()
    {
        var g = Real();
        g.PutCar(5, 5, Dir.Right);
        g.Idle(120);
        Assert.Equal(GameEngine.FullTank - 2 * GameEngine.Burn, g.Fuel, 1);
        g.SetFuel(0);
        g.PutCar(5, 5, Dir.Right);
        g.Idle(60);
        Assert.Equal(5 + GameEngine.EmptySpeed, g.Car.X, 1);
    }

    // ───────────────────────────── smoke and pursuers ─────────────────────────────

    [Fact]
    public void Smoke_costs_fuel_and_spins_out_a_pursuer_that_drives_into_it()
    {
        var g = Real();
        // The smoke lands by the next junction, where the pursuer will be in a moment.
        g.PutCar(14, 5, Dir.Right);
        var p = g.PutPursuer(12, 5, Dir.Right);
        g.Run(1, new Controls(Fire: true));
        Assert.Single(g.Smokes);
        Assert.True(g.Fuel < GameEngine.FullTank - GameEngine.SmokeCost + .1);
        g.Idle(100);
        Assert.True(p.Stunned > 0);
        Assert.True(g.Car.Alive);
    }

    [Fact]
    public void A_pursuer_hunts_the_car_down_and_a_crash_starts_the_next_car_from_the_start_with_a_full_tank()
    {
        var g = Game();
        g.SetLives(3);
        // Head on, between junctions.
        g.PutCar(4, 5, Dir.Right);
        g.PutPursuer(6, 5, Dir.Left);
        for (int t = 0; t < 600 && g.Phase == Phase.Play; t++) g.Tick(default);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal("CAUGHT", g.Cause);
        Assert.Equal(2, g.Lives);
        g.SetFuel(10);
        g.Idle((int)(60 * GameEngine.DyingSeconds) + 5);
        Assert.True(g.Car.Alive);
        Assert.Equal((GameEngine.StartX, GameEngine.StartY), (g.Car.Cx, g.Car.Cy));
        Assert.Equal(GameEngine.FullTank, g.Fuel);
    }

    [Fact]
    public void Pursuers_close_in_on_the_car()
    {
        var g = Game();
        g.PutCar(25, 37, Dir.Up);
        var p = g.PutPursuer(3, 3, Dir.Down);
        double before = Math.Abs(p.X - g.Car.X) + Math.Abs(p.Y - g.Car.Y);
        g.Idle(60 * 4);
        Assert.True(Math.Abs(p.X - g.Car.X) + Math.Abs(p.Y - g.Car.Y) < before - 6);
    }

    [Fact]
    public void A_rock_wrecks_the_car_and_losing_the_last_car_ends_the_game()
    {
        var g = Game();
        g.SetLives(1);
        g.PutRock(9, 5);
        g.PutCar(5, 5, Dir.Right);
        g.Idle(60);
        Assert.Equal("ROCK", g.Cause);
        g.Idle((int)(60 * GameEngine.DyingSeconds) + 10);
        Assert.Equal(GameState.GameOver, g.State);
    }

    [Fact]
    public void An_extra_car_comes_at_20000()
    {
        var g = Game();
        int lives = g.Lives;
        g.SetScore(GameEngine.FirstExtraLife - 50);
        g.PutFlag(7, 5);
        g.PutCar(5, 5, Dir.Right);
        g.Idle(40);
        Assert.Equal(lives + 1, g.Lives);
    }

    // ───────────────────────────── around the game ─────────────────────────────

    [Fact]
    public void The_computer_driver_clears_the_first_rounds()
    {
        var g = new GameEngine(1, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.StartGame();
        g.SetLives(9);
        for (int t = 0; t < 60 * 200 && g.State == GameState.Playing && g.Round < 3; t++) g.Tick(g.Pilot.Decide(g));
        Assert.Equal(GameState.Playing, g.State);
        Assert.True(g.Round >= 3, $"round {g.Round}");
    }

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores_then_back()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle(60 * 25);
        Assert.True(g.Score > 0);
        g.Idle((int)(60 * (GameEngine.AttractSeconds - 25)) + 5);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle((int)(60 * GameEngine.HighScoreSeconds) + 5);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        static (int, double, double, int) Play(int seed)
        {
            var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.StartGame();
            for (int t = 0; t < 60 * 40; t++) g.Tick(g.Pilot.Decide(g));
            return (g.Score, g.Car.X, g.Car.Y, g.Round);
        }
        Assert.Equal(Play(12), Play(12));
    }

    [Fact]
    public void A_high_score_takes_initials_and_is_kept_in_the_table_file()
    {
        string folder = TempFolder();
        var g = new GameEngine(7, HighScoreTable.Load(folder)) { Silent = true, Calm = true };
        g.Go(3);
        g.OpenCourse();
        g.SetScore(99999);
        g.SetLives(1);
        g.PutRock(9, 5);
        g.PutCar(5, 5, Dir.Right);
        g.Idle((int)(60 * (GameEngine.DyingSeconds + 6)));
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "RLY") g.Initials!.Type(ch);
        g.Initials!.Finish();
        g.Idle(2);
        Assert.Equal(GameState.HighScores, g.State);
        var again = HighScoreTable.Load(folder);
        Assert.Equal(("RLY", 99999, 3), (again.Entries[0].Initials, again.Entries[0].Score, again.Entries[0].Stage));
        Directory.Delete(folder, true);
    }
}
