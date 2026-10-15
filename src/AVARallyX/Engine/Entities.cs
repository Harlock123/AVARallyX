namespace AVARallyX.Engine;

/// <summary>The stick (four ways) and the smokescreen button.</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Fire = false);

public enum Dir { None, Up, Down, Left, Right }

public static class Dirs
{
    public static readonly Dir[] All = [Dir.Up, Dir.Left, Dir.Down, Dir.Right];

    public static (int X, int Y) Vec(Dir d) => d switch { Dir.Up => (0, -1), Dir.Down => (0, 1), Dir.Left => (-1, 0), Dir.Right => (1, 0), _ => (0, 0) };

    public static Dir Opposite(Dir d) => d switch { Dir.Up => Dir.Down, Dir.Down => Dir.Up, Dir.Left => Dir.Right, Dir.Right => Dir.Left, _ => Dir.None };

    /// <summary>The way to the right of (clockwise from) a heading.</summary>
    public static Dir Right(Dir d) => d switch { Dir.Up => Dir.Right, Dir.Right => Dir.Down, Dir.Down => Dir.Left, Dir.Left => Dir.Up, _ => Dir.None };

    public static Dir Left(Dir d) => Opposite(Right(d));

    public static bool Horizontal(Dir d) => d is Dir.Left or Dir.Right;

    /// <summary>A heading as an angle, clockwise from up.</summary>
    public static double Angle(Dir d) => d switch { Dir.Right => Math.PI / 2, Dir.Down => Math.PI, Dir.Left => -Math.PI / 2, _ => 0 };
}

/// <summary>
/// The screen: the view round the car on the left and the panel on the right. The course is a grid of cells,
/// each a stretch of road or part of a city block; a cell's centre is at whole numbers.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const double ViewLeft = 0, ViewRight = 760, ViewTop = 64, ViewBottom = 768;
    public const double ViewW = ViewRight - ViewLeft, ViewH = ViewBottom - ViewTop;
    public const double ViewX = (ViewLeft + ViewRight) / 2, ViewY = (ViewTop + ViewBottom) / 2;
    public const double Cell = 48;
    public const int Cols = 33, Rows = 45;
}

/// <summary>Anything that drives the roads, from cell centre to cell centre.</summary>
public class Mover
{
    public double X, Y;
    /// <summary>The cell it is heading for: its own cell while it stands.</summary>
    public int Tx, Ty;
    public Dir Dir;
    /// <summary>The way it's pointing, for drawing; it swings round smoothly to the way it's going.</summary>
    public double Angle;
    public Dir Facing = Dir.Up;
    public int Cx => (int)Math.Round(X);
    public int Cy => (int)Math.Round(Y);
    public bool AtCentre => X == Tx && Y == Ty;

    public void Place(int c, int r, Dir facing)
    {
        (X, Y, Tx, Ty, Dir, Facing) = (c, r, c, r, Dir.None, facing);
        Angle = Dirs.Angle(facing);
    }

    public void Head(Dir d)
    {
        var (dx, dy) = Dirs.Vec(d);
        (Tx, Ty, Dir, Facing) = (Cx + dx, Cy + dy, d, d);
    }

    /// <summary>Moves up to <paramref name="dist"/> cells towards its target; returns what is left over if it gets there.</summary>
    public double Glide(double dist)
    {
        double left = Math.Abs(Tx - X) + Math.Abs(Ty - Y);
        if (dist >= left)
        {
            (X, Y, Dir) = (Tx, Ty, Dir.None);
            return dist - left;
        }
        var (dx, dy) = Dirs.Vec(Dir);
        X += dx * dist;
        Y += dy * dist;
        return 0;
    }

    /// <summary>Swings the drawn angle round towards the way it faces.</summary>
    public void Steer(double dt, double rate = 14)
    {
        double want = Dirs.Angle(Facing), err = Math.Atan2(Math.Sin(want - Angle), Math.Cos(want - Angle));
        Angle += Math.Clamp(err, -rate * dt, rate * dt);
    }
}

public sealed class Car : Mover
{
    public bool Alive = true;
    public double SmokeCool;
}

/// <summary>A red pursuit car. Caught in smoke it spins out for a while, harmless.</summary>
public sealed class Pursuer : Mover
{
    public double Stunned, Spin;
    public int HomeX, HomeY;
}

public enum FlagKind
{
    Normal,
    /// <summary>Doubles what every flag after it is worth, for the rest of the round.</summary>
    Double,
    /// <summary>Fills the tank.</summary>
    Fuel,
}

public sealed class Flag
{
    public int C, R;
    public FlagKind Kind;
    public bool Taken;
}

/// <summary>A puff of smokescreen left on the road.</summary>
public sealed class Smoke
{
    public double X, Y, Age;
}

public enum EffectKind { Score, Burst, Word, Puff }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.7, Size = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
