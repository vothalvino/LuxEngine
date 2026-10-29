namespace LuxEngine.Core.Effects;

/// <summary>A point of a movement shape: X is pan, Y is tilt, both from -1 to 1 (Y grows downwards like on the XY pad).</summary>
public sealed record ShapePoint(double X, double Y);

/// <summary>A figure that moving heads trace, drawn by the user or made from a template.</summary>
public sealed class MovementShape
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New shape";
    public List<ShapePoint> Points { get; set; } = new();

    /// <summary>Closed shapes loop back to the first point; open ones go to the end and back.</summary>
    public bool Closed { get; set; } = true;

    /// <summary>Rounds the corners into a smooth curve through the points.</summary>
    public bool Smooth { get; set; } = true;

    public MovementShape Clone(string name) => new()
    {
        Name = name, Points = Points.ToList(), Closed = Closed, Smooth = Smooth,
    };

    /// <summary>Where on the shape the head is at <paramref name="phase"/> (0–1), moving at an even speed along the path.</summary>
    public (double X, double Y) At(double phase)
    {
        if (Points.Count == 0) return (0, 0);
        if (Points.Count == 1) return (Points[0].X, Points[0].Y);
        phase -= Math.Floor(phase);
        if (!Closed) phase = phase < 0.5 ? phase * 2 : (1 - phase) * 2;   // there and back

        var path = Path();
        var lengths = new double[path.Count];
        for (int i = 1; i < path.Count; i++)
            lengths[i] = lengths[i - 1] + Distance(path[i - 1], path[i]);
        double target = phase * lengths[^1];
        for (int i = 1; i < path.Count; i++)
        {
            if (lengths[i] < target) continue;
            double segment = lengths[i] - lengths[i - 1];
            double t = segment <= 0 ? 0 : (target - lengths[i - 1]) / segment;
            return (path[i - 1].X + (path[i].X - path[i - 1].X) * t, path[i - 1].Y + (path[i].Y - path[i - 1].Y) * t);
        }
        return (path[^1].X, path[^1].Y);
    }

    /// <summary>The path as straight pieces; smooth shapes are sampled from a Catmull-Rom curve.</summary>
    public List<ShapePoint> Path()
    {
        var pts = Points;
        var result = new List<ShapePoint>();
        int segments = Closed ? pts.Count : pts.Count - 1;
        if (!Smooth || pts.Count < 3)
        {
            result.AddRange(pts);
            if (Closed) result.Add(pts[0]);
            return result;
        }
        ShapePoint P(int i) => Closed ? pts[((i % pts.Count) + pts.Count) % pts.Count] : pts[Math.Clamp(i, 0, pts.Count - 1)];
        const int steps = 12;
        for (int s = 0; s < segments; s++)
        {
            var (p0, p1, p2, p3) = (P(s - 1), P(s), P(s + 1), P(s + 2));
            for (int k = 0; k < steps; k++)
            {
                double t = (double)k / steps, t2 = t * t, t3 = t2 * t;
                double x = 0.5 * (2 * p1.X + (-p0.X + p2.X) * t + (2 * p0.X - 5 * p1.X + 4 * p2.X - p3.X) * t2 + (-p0.X + 3 * p1.X - 3 * p2.X + p3.X) * t3);
                double y = 0.5 * (2 * p1.Y + (-p0.Y + p2.Y) * t + (2 * p0.Y - 5 * p1.Y + 4 * p2.Y - p3.Y) * t2 + (-p0.Y + 3 * p1.Y - 3 * p2.Y + p3.Y) * t3);
                result.Add(new ShapePoint(x, y));
            }
        }
        result.Add(Closed ? pts[0] : pts[^1]);
        return result;
    }

    private static double Distance(ShapePoint a, ShapePoint b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>Starting points for new shapes.</summary>
    public static IReadOnlyList<MovementShape> Templates() => new[]
    {
        Ring("Circle", 16, (a) => (Math.Cos(a), Math.Sin(a))),
        Ring("Figure 8", 16, (a) => (Math.Sin(a), 0.5 * Math.Sin(2 * a))),
        new MovementShape { Name = "Square", Smooth = false, Points = { new(-1, -1), new(1, -1), new(1, 1), new(-1, 1) } },
        new MovementShape { Name = "Triangle", Smooth = false, Points = { new(0, -1), new(1, 1), new(-1, 1) } },
        new MovementShape { Name = "Line left-right", Closed = false, Smooth = false, Points = { new(-1, 0), new(1, 0) } },
        new MovementShape { Name = "Line up-down", Closed = false, Smooth = false, Points = { new(0, -1), new(0, 1) } },
        new MovementShape { Name = "Zig-zag", Closed = false, Smooth = false, Points = { new(-1, 1), new(-0.5, -1), new(0, 1), new(0.5, -1), new(1, 1) } },
    };

    private static MovementShape Ring(string name, int count, Func<double, (double, double)> f)
    {
        var shape = new MovementShape { Name = name };
        for (int i = 0; i < count; i++)
        {
            var (x, y) = f(2 * Math.PI * i / count);
            shape.Points.Add(new ShapePoint(Math.Round(x, 3), Math.Round(y, 3)));
        }
        return shape;
    }
}

/// <summary>The pan/tilt range one head may use inside an area (DMX values 0–255).</summary>
public sealed class AreaBounds
{
    public int PanMin { get; set; }
    public int PanMax { get; set; } = 255;
    public int TiltMin { get; set; }
    public int TiltMax { get; set; } = 255;

    public AreaBounds Normalized() => new()
    {
        PanMin = Math.Clamp(Math.Min(PanMin, PanMax), 0, 255), PanMax = Math.Clamp(Math.Max(PanMin, PanMax), 0, 255),
        TiltMin = Math.Clamp(Math.Min(TiltMin, TiltMax), 0, 255), TiltMax = Math.Clamp(Math.Max(TiltMin, TiltMax), 0, 255),
    };
}

/// <summary>
/// A place on stage such as "Wall" or "Stage floor". Each moving head has its own pan/tilt box for it,
/// because heads hung in different places need different angles to hit the same spot. A movement in
/// an area is centred in each head's box and never leaves it.
/// </summary>
public sealed class MovementArea
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New area";
    public Dictionary<Guid, AreaBounds> Bounds { get; set; } = new();
}
