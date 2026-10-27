using LuxEngine.Core.Fixtures;

namespace LuxEngine.Core.Shows;

public sealed record PatchConflict(FixtureInstance First, FixtureInstance Second);

public static class PatchHelper
{
    public const int UniverseSize = 512;

    /// <summary>Every pair of fixtures whose DMX ranges overlap.</summary>
    public static IReadOnlyList<PatchConflict> FindConflicts(Show show)
    {
        var conflicts = new List<PatchConflict>();
        var patched = show.Patched().ToList();
        for (int i = 0; i < patched.Count; i++)
        for (int j = i + 1; j < patched.Count; j++)
        {
            var a = patched[i];
            var b = patched[j];
            if (a.Instance.Universe == b.Instance.Universe &&
                a.Instance.Address <= b.LastAddress && b.Instance.Address <= a.LastAddress)
                conflicts.Add(new PatchConflict(a.Instance, b.Instance));
        }
        return conflicts;
    }

    public static bool FitsInUniverse(int address, int channelCount) =>
        address >= 1 && address + channelCount - 1 <= UniverseSize;

    /// <summary>The first address in the universe where a block of channels fits without overlapping
    /// existing fixtures, or null when the universe is full.</summary>
    public static int? NextFreeAddress(Show show, int universe, int channelCount, int startAt = 1)
    {
        var used = new bool[UniverseSize + 2];
        foreach (var p in show.Patched().Where(p => p.Instance.Universe == universe))
            for (int a = p.Instance.Address; a <= Math.Min(p.LastAddress, UniverseSize); a++)
                used[a] = true;

        for (int start = Math.Max(1, startAt); start + channelCount - 1 <= UniverseSize; start++)
        {
            bool free = true;
            for (int a = start; a < start + channelCount && free; a++) free = !used[a];
            if (free) return start;
        }
        return null;
    }

    /// <summary>Adds <paramref name="count"/> fixtures one after another starting at the given (or first free) address.
    /// Fixtures that do not fit move on to the next universe.</summary>
    public static IReadOnlyList<FixtureInstance> AddFixtures(Show show, FixtureDefinition definition, string modeName,
        int count, int universe, int? address, string? baseName = null)
    {
        var mode = definition.FindMode(modeName) ?? throw new ArgumentException("The fixture has no modes.");
        if (mode.ChannelCount > UniverseSize) throw new ArgumentException("The mode has more than 512 channels.");
        if (!show.FixtureDefinitions.ContainsKey(definition.Id)) show.FixtureDefinitions[definition.Id] = definition;

        baseName ??= definition.Model;
        int existing = show.Fixtures.Count(f => f.DefinitionId == definition.Id);
        var added = new List<FixtureInstance>();
        int? next = address;

        for (int i = 0; i < count; i++)
        {
            int? start = next is int n && FitsInUniverse(n, mode.ChannelCount) ? n : NextFreeAddress(show, universe, mode.ChannelCount);
            while (start is null)
            {
                universe++;
                start = NextFreeAddress(show, universe, mode.ChannelCount);
            }

            var fixture = new FixtureInstance
            {
                Name = $"{baseName} {existing + i + 1}",
                DefinitionId = definition.Id,
                ModeName = mode.Name,
                Universe = universe,
                Address = start.Value,
                StageX = count == 1 ? 0.5 : 0.1 + 0.8 * i / Math.Max(1, count - 1),
                StageY = 0.3,
            };
            show.Fixtures.Add(fixture);
            added.Add(fixture);
            next = start.Value + mode.ChannelCount;
        }
        return added;
    }
}
