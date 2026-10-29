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
