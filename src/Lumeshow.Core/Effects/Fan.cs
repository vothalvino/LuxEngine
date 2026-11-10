namespace Lumeshow.Core.Effects;

public static class Fan
{
    /// <summary>
    /// <paramref name="count"/> values spread evenly from <paramref name="from"/> to <paramref name="to"/>.
    /// A single light gets the middle.
    /// </summary>
    public static double[] Spread(int count, double from, double to)
    {
        var values = new double[Math.Max(count, 0)];
        for (int i = 0; i < values.Length; i++)
            values[i] = values.Length == 1 ? (from + to) / 2 : from + (to - from) * i / (values.Length - 1);
        return values;
    }
}
