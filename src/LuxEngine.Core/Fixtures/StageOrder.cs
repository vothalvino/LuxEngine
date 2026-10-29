namespace LuxEngine.Core.Fixtures;

/// <summary>The order lights stand in on stage: left to right, then top to bottom.</summary>
public static class StageOrder
{
    public static IOrderedEnumerable<T> OrderByStage<T>(this IEnumerable<T> items, Func<T, FixtureInstance> instance) =>
        items.OrderBy(i => Math.Round(instance(i).StageX, 3)).ThenBy(i => Math.Round(instance(i).StageY, 3));

    public static IOrderedEnumerable<FixtureInstance> OrderByStage(this IEnumerable<FixtureInstance> fixtures) =>
        fixtures.OrderByStage(f => f);
}
