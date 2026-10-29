namespace LuxEngine.Core.Cues;

public enum CueButtonMode
{
    /// <summary>Click to turn on, click again to turn off.</summary>
    Toggle,

    /// <summary>On only while the button is held down.</summary>
    Flash,
}

/// <summary>
/// A cue that lives on a button: it plays its presets on top of the cue list while it is on.
/// Buttons in touching cells form a group in which only one can be on at a time.
/// </summary>
public sealed class CueButton
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#3B82F6";
    public List<Guid> PresetIds { get; set; } = new();
    public int Row { get; set; }
    public int Column { get; set; }
    public CueButtonMode Mode { get; set; } = CueButtonMode.Toggle;

    /// <summary>Keyboard key that works this button (a VirtualKey name such as "Number1" or "Q"), or null.</summary>
    public string? Key { get; set; }

    /// <summary>Seconds to fade in when switched on.</summary>
    public double FadeIn { get; set; } = 0.5;

    /// <summary>Seconds to fade out when switched off.</summary>
    public double FadeOut { get; set; } = 0.5;
}

/// <summary>The grid of cue buttons. It has no fixed size: buttons can sit at any row and column, even negative ones.</summary>
public sealed class ButtonPage
{
    /// <summary>Empty squares shown around the outermost buttons, so there is always room to grow.</summary>
    public const int Margin = 2;

    /// <summary>The smallest area shown, starting at row 0 / column 0, even when there are few buttons.</summary>
    public int Rows { get; set; } = 4;
    public int Columns { get; set; } = 8;
    public List<CueButton> Buttons { get; set; } = new();

    /// <summary>The rows and columns to show (inclusive): the minimum area plus a margin around every button.</summary>
    public (int RowFrom, int RowTo, int ColumnFrom, int ColumnTo) Extent()
    {
        int rowFrom = 0, rowTo = Rows - 1, colFrom = 0, colTo = Columns - 1;
        foreach (var b in Buttons)
        {
            rowFrom = Math.Min(rowFrom, b.Row - Margin);
            rowTo = Math.Max(rowTo, b.Row + Margin);
            colFrom = Math.Min(colFrom, b.Column - Margin);
            colTo = Math.Max(colTo, b.Column + Margin);
        }
        return (rowFrom, rowTo, colFrom, colTo);
    }

    public CueButton? At(int row, int column) => Buttons.FirstOrDefault(b => b.Row == row && b.Column == column);

    public CueButton? Find(Guid id) => Buttons.FirstOrDefault(b => b.Id == id);

    /// <summary>The button plus every button connected to it through touching cells (not diagonals).</summary>
    public IReadOnlyCollection<CueButton> GroupOf(CueButton button)
    {
        var cells = Buttons.ToDictionary(b => (b.Row, b.Column));
        var group = new HashSet<CueButton> { button };
        var queue = new Queue<CueButton>(group);
        while (queue.Count > 0)
        {
            var b = queue.Dequeue();
            foreach (var (dr, dc) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
                if (cells.TryGetValue((b.Row + dr, b.Column + dc), out var n) && group.Add(n))
                    queue.Enqueue(n);
        }
        return group;
    }
}
