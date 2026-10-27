namespace LuxEngine.Core.Engine;

/// <summary>Identifies one channel of one patched fixture, e.g. (Par 1, "Red").</summary>
public readonly record struct ChannelKey(Guid FixtureId, string Channel);
