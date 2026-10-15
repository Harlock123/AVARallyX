using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVARallyX.Engine;
using AVARallyX.Views;

namespace AVARallyX.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set RALLY_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avarallyx-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("RALLY_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVARallyX-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer driver play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            g.Tick(g.Pilot.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("ACE", 84250, 7);
        g.HighScores.Insert("AVA", 52800, 5);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void On_the_road()
    {
        var (w, c, g) = Open(2);
        g.StartGame();
        g.SetLives(9);
        g.Idle(60);
        c.Advance(.5);
        Shot(w, c, "round-start");
        bool Near(GameEngine g, double d) => g.Pursuers.Count(p => Math.Abs(p.X - g.Car.X) + Math.Abs(p.Y - g.Car.Y) < d) >= 2;
        Assert.True(PlayUntil(g, c, g => g.Car.Alive && Near(g, 7) && g.FlagList.Any(f => !f.Taken && Math.Abs(f.C - g.Car.X) + Math.Abs(f.R - g.Car.Y) < 6), 200));
        Shot(w, c, "chase");
        Assert.True(PlayUntil(g, c, g => g.Pursuers.Any(p => p.Stunned > 1.5 && Math.Abs(p.X - g.Car.X) + Math.Abs(p.Y - g.Car.Y) < 5) && g.Smokes.Count > 0, 200));
        Shot(w, c, "smoke");
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Clear && g.Message != null, 300));
        Shot(w, c, "clear");
    }

    [AvaloniaFact]
    public void A_crash()
    {
        var (w, c, g) = Open(4);
        g.Go(3);
        g.SetLives(3);
        Assert.True(PlayUntil(g, c, g => g.RoundTime > 5 && g.Car.Alive && g.Rocks.Any(r => Math.Abs(r.C - g.Car.X) + Math.Abs(r.R - g.Car.Y) < 6)));
        var p = g.Pursuers[0];
        p.Place(g.Car.Tx, g.Car.Ty, Dirs.Opposite(g.Car.Facing));
        for (int t = 0; t < 30; t++) { g.Tick(default); c.Advance(1 / 60.0); }
        Shot(w, c, "crash");
    }
}
