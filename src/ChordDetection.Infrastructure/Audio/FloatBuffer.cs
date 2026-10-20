namespace ChordDetection.Infrastructure.Audio;

/// <summary>Growable float array used by the streaming stages.</summary>
internal sealed class FloatBuffer
{
    private float[] _data = new float[4096];

    public int Count { get; private set; }

    public float this[int index] => _data[index];

    public ReadOnlySpan<float> Span => _data.AsSpan(0, Count);

    public void Append(ReadOnlySpan<float> values)
    {
        if (Count + values.Length > _data.Length)
            Array.Resize(ref _data, Math.Max(_data.Length * 2, Count + values.Length));
        values.CopyTo(_data.AsSpan(Count));
        Count += values.Length;
    }

    /// <summary>Keeps only the first <paramref name="count"/> values.</summary>
    public void Truncate(int count) => Count = Math.Clamp(count, 0, Count);

    /// <summary>Removes the first <paramref name="count"/> values.</summary>
    public void RemoveFront(int count)
    {
        if (count <= 0) return;
        _data.AsSpan(count, Count - count).CopyTo(_data);
        Count -= count;
    }

    /// <summary>Backing array; valid up to <see cref="Count"/>.</summary>
    public float[] RawArray => _data;
}
