using LuxEngine.Core.Effects;
using LuxEngine.Core.Engine;
using LuxEngine.Core.Fixtures;
using LuxEngine.Core.Shows;

namespace LuxEngine.Core.Tests;

public class MovementTests
{
    private static (Show Show, FixtureInstance Head) MovingHead()
    {
        var show = new Show();
        var def = FixtureLibrary.CreateGeneric("Head", ChannelType.Pan, ChannelType.Tilt, ChannelType.Intensity);
        return (show, PatchHelper.AddFixtures(show, def, def.Modes[0].Name, 1, 1, 1)[0]);
    }

    [Fact]
    public void Invert_flips_only_the_dmx_output()
    {
        var (show, head) = MovingHead();
        var engine = new LightingEngine(show);
        engine.SetValue(head.Id, "Pan", 50);
        engine.SetValue(head.Id, "Tilt", 200);

        head.InvertPan = true;
        var u = engine.ComputeFrame(0).Universe(1);
        Assert.Equal(205, u[0]);                                  // pan flipped
        Assert.Equal(200, u[1]);                                  // tilt untouched
        Assert.Equal(50, engine.GetOutput(head.Id, "Pan"));      // the app still sees the normal value

        head.InvertTilt = true;
        Assert.Equal(55, engine.ComputeFrame(0).Universe(1)[1]);
    }

    [Fact]
    public void Pan_sweep_moves_only_pan_and_a_new_movement_replaces_the_old()
    {
        var (show, head) = MovingHead();
        var engine = new LightingEngine(show);
        engine.SetValue(head.Id, "Pan", 128);
        engine.SetValue(head.Id, "Tilt", 128);
        engine.AddEffect(new Effect { Type = EffectType.Circle, Speed = 1, FixtureIds = { head.Id } });
        engine.AddEffect(new Effect { Type = EffectType.PanSweep, Speed = 1, Size = 1, FixtureIds = { head.Id } });

        Assert.Single(engine.Programmer.Effects);
        var u = engine.ComputeFrame(0.25).Universe(1);           // a quarter cycle: furthest to one side
        Assert.Equal(192, u[0]);
        Assert.Equal(128, u[1]);
    }
}

public class ShapeAndAreaTests
{
    private static (Show Show, FixtureInstance Head, LightingEngine Engine) Setup()
    {
        var show = new Show();
        var def = FixtureLibrary.CreateGeneric("Head", ChannelType.Pan, ChannelType.Tilt);
        var head = PatchHelper.AddFixtures(show, def, def.Modes[0].Name, 1, 1, 1)[0];
        return (show, head, new LightingEngine(show));
    }

    [Fact]
    public void Shapes_move_at_even_speed_along_their_points()
    {
        var square = MovementShape.Templates().First(s => s.Name == "Square");
        Assert.Equal((-1.0, -1.0), square.At(0));
        Assert.Equal((1.0, -1.0), square.At(0.25));
        Assert.Equal((0.0, 1.0), square.At(0.625));

        var line = new MovementShape { Closed = false, Smooth = false, Points = { new(-1, 0), new(1, 0) } };
        Assert.Equal((1.0, 0.0), line.At(0.5));    // open shapes go there…
        Assert.Equal((-1.0, 0.0), line.At(0.99999), new ToleranceComparer());   // …and back
    }

    [Fact]
    public void Movement_in_an_area_stays_inside_each_heads_box()
    {
        var (show, head, engine) = Setup();
        var area = new MovementArea { Name = "Wall" };
        area.Bounds[head.Id] = new AreaBounds { PanMin = 100, PanMax = 140, TiltMin = 20, TiltMax = 40 };
        show.Areas.Add(area);
        var shape = MovementShape.Templates().First(s => s.Name == "Square");
        show.Shapes.Add(shape);
        engine.SetValue(head.Id, "Pan", 0);    // aimed far away: the area still decides
        engine.AddEffect(new Effect { Type = EffectType.Shape, ShapeId = shape.Id, AreaId = area.Id, Speed = 1, Size = 1, FixtureIds = { head.Id } });

        for (double t = 0; t < 1; t += 0.05)
        {
            var u = engine.ComputeFrame(t).Universe(1);
            Assert.InRange(u[0], 100, 140);
            Assert.InRange(u[1], 20, 40);
        }
        var corner = engine.ComputeFrame(0).Universe(1);
        Assert.Equal((100, 20), ((int)corner[0], (int)corner[1]));   // the square's corner is the box's corner
    }

    [Fact]
    public void Width_and_height_shrink_the_figure_and_reverse_runs_backwards()
    {
        var (show, head, engine) = Setup();
        var area = new MovementArea();
        area.Bounds[head.Id] = new AreaBounds { PanMin = 0, PanMax = 200, TiltMin = 0, TiltMax = 200 };
        show.Areas.Add(area);
        engine.AddEffect(new Effect { Type = EffectType.Circle, AreaId = area.Id, Speed = 1, Size = 1, Width = 0.5, Height = 0, FixtureIds = { head.Id } });
        var u = engine.ComputeFrame(0).Universe(1);
        Assert.Equal(150, u[0]);   // centre 100 + half of the 100 half-width
        Assert.Equal(100, u[1]);   // no height: tilt stays centred
    }

    private sealed class ToleranceComparer : IEqualityComparer<(double, double)>
    {
        public bool Equals((double, double) a, (double, double) b) => Math.Abs(a.Item1 - b.Item1) < 1e-3 && Math.Abs(a.Item2 - b.Item2) < 1e-3;
        public int GetHashCode((double, double) obj) => 0;
    }
}

public class MovementSaveTests
{
    [Fact]
    public void Shapes_areas_and_movement_settings_survive_saving()
    {
        var t = new TestShow();
        var shape = MovementShape.Templates()[2].Clone("My square");
        var area = new MovementArea { Name = "Wall" };
        area.Bounds[t.Par1.Id] = new AreaBounds { PanMin = 10, PanMax = 20, TiltMin = 30, TiltMax = 40 };
        t.Show.Shapes.Add(shape);
        t.Show.Areas.Add(area);
        var preset = t.AddPreset("Move");
        preset.Effects.Add(new Effect { Type = EffectType.Shape, ShapeId = shape.Id, AreaId = area.Id, Width = 0.3, Reverse = true, FixtureIds = { t.Par1.Id } });

        var loaded = ShowSerializer.Deserialize(ShowSerializer.Serialize(t.Show));

        Assert.Equal("My square", loaded.Shapes.Single().Name);
        Assert.Equal(4, loaded.Shapes.Single().Points.Count);
        Assert.Equal(30, loaded.Areas.Single().Bounds[t.Par1.Id].TiltMin);
        var effect = loaded.PresetRoot.Presets.Single().Effects.Single();
        Assert.Equal((shape.Id, area.Id, 0.3, true), (effect.ShapeId, effect.AreaId, effect.Width, effect.Reverse));
    }
}
