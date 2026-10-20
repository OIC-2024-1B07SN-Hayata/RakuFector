namespace ChordDetection.Core.Chords;

/// <summary>
/// Rule catalog of chord types. Adding a chord type = adding one entry.
/// Named "Catalog" to avoid confusion with the C# Dictionary type.
/// </summary>
public sealed class ChordCatalog
{
    private readonly Dictionary<string, ChordType> _bySymbol;

    public ChordCatalog(IEnumerable<ChordType> types)
    {
        Types = types.ToList();
        if (Types.Count == 0) throw new ArgumentException("Catalog is empty.", nameof(types));
        _bySymbol = Types.ToDictionary(t => t.Symbol, StringComparer.Ordinal);

        foreach (var t in Types)
        {
            if (!t.Contains(0))
                throw new ArgumentException($"'{t.Symbol}' must contain the root (0).");
            if (t.SimplifiesTo is { } target && !_bySymbol.ContainsKey(target))
                throw new ArgumentException($"'{t.Symbol}' simplifies to unknown symbol '{target}'.");
        }
    }

    public IReadOnlyList<ChordType> Types { get; }

    public int MaxLevel => Types.Max(t => t.Level);

    public ChordType this[string symbol] => _bySymbol[symbol];

    public bool TryGet(string symbol, out ChordType type) => _bySymbol.TryGetValue(symbol, out type!);

    /// <summary>
    /// Stage 1 + 2 catalog of the design document (9 types) plus the power chord "5".
    /// Level 1: major, minor, power chord. Level 2: 7, M7, m7, sus2, sus4, dim, aug, m7b5, 6, add9.
    /// </summary>
    public static ChordCatalog Default { get; } = new(
    [
        // Level 1
        new ChordType("", [0, 4, 7], 1),
        new ChordType("m", [0, 3, 7], 1),
        new ChordType("5", [0, 7], 1),

        // Level 2 (SimplifiesTo = what is shown at level 1)
        new ChordType("7", [0, 4, 7, 10], 2, SimplifiesTo: "", OptionalFifth: true),
        new ChordType("M7", [0, 4, 7, 11], 2, SimplifiesTo: "", OptionalFifth: true),
        new ChordType("m7", [0, 3, 7, 10], 2, SimplifiesTo: "m", OptionalFifth: true),
        new ChordType("sus2", [0, 2, 7], 2, SimplifiesTo: ""),
        new ChordType("sus4", [0, 5, 7], 2, SimplifiesTo: ""),
        new ChordType("dim", [0, 3, 6], 2, SimplifiesTo: "m"),
        new ChordType("aug", [0, 4, 8], 2, SimplifiesTo: ""),
        new ChordType("m7b5", [0, 3, 6, 10], 2, SimplifiesTo: "m"),
        // Same pitch classes as m7 a minor 3rd lower (C6 = Am7): the bass decides.
        new ChordType("6", [0, 4, 7, 9], 2, SimplifiesTo: ""),
        // The 9th is pitch class 2 (sus2 + 3rd).
        new ChordType("add9", [0, 2, 4, 7], 2, SimplifiesTo: ""),
    ]);
}
