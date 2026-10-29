using LuxEngine.Core.Fixtures;

namespace LuxEngine.Core.Tests;

public class StrobeTests
{
    private static ChannelDefinition Shutter(params (int From, int To, string Label)[] ranges) => new()
    {
        Name = "Shutter", Type = ChannelType.Shutter,
        Capabilities = ranges.Select(r => new ChannelCapability { From = r.From, To = r.To, Label = r.Label }).ToList(),
    };

    [Fact]
    public void Named_ranges_decide_whether_and_how_fast_it_flashes()
    {
        var ch = Shutter((0, 9, "Closed"), (10, 19, "Open"), (20, 119, "Strobe slow → fast"), (120, 219, "Strobe fast → slow"), (220, 255, "Random strobe"));
        Assert.Null(Strobe.Rate(ch, 5));
        Assert.Null(Strobe.Rate(ch, 15));
        Assert.Equal(Strobe.SlowestHz, Strobe.Rate(ch, 20)!.Value.FlashesPerSecond, 3);
        Assert.Equal(Strobe.FastestHz, Strobe.Rate(ch, 119)!.Value.FlashesPerSecond, 3);
        Assert.Equal(Strobe.FastestHz, Strobe.Rate(ch, 120)!.Value.FlashesPerSecond, 3);   // fast → slow starts fast
        Assert.True(Strobe.Rate(ch, 230)!.Value.Random);
    }

    [Fact]
    public void Strobe_channel_without_ranges_is_off_near_zero_and_faster_towards_full()
    {
        var ch = new ChannelDefinition { Name = "Strobe", Type = ChannelType.Strobe };
        Assert.Null(Strobe.Rate(ch, 0));
        var slow = Strobe.Rate(ch, 30)!.Value.FlashesPerSecond;
        var fast = Strobe.Rate(ch, 250)!.Value.FlashesPerSecond;
        Assert.True(fast > slow);
        Assert.Null(Strobe.Rate(new ChannelDefinition { Type = ChannelType.Shutter }, 200));   // unknown shutter values: no guess
    }

    [Fact]
    public void Flashes_are_short_and_repeat_at_the_rate()
    {
        var rate = new StrobeRate(10, false);
        Assert.Equal(1, Strobe.Flash(rate, 0.01, Guid.Empty));
        Assert.Equal(0, Strobe.Flash(rate, 0.06, Guid.Empty));
        Assert.Equal(1, Strobe.Flash(rate, 0.11, Guid.Empty));
    }
}
