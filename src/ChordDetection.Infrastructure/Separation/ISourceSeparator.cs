using ChordDetection.Infrastructure.Audio;

namespace ChordDetection.Infrastructure.Separation;

/// <summary>
/// Optional step before stage 2: splits a mixed song into instrument sources
/// so that only the guitar (or "other") source is analysed.
/// </summary>
public interface ISourceSeparator : IDisposable
{
    /// <summary>Names of the sources the model produces (e.g. "guitar", "bass").</summary>
    IReadOnlyList<string> Sources { get; }

    /// <summary>
    /// Separates <paramref name="mix"/> and yields <paramref name="source"/> piece by piece, in order,
    /// as soon as each part is final (streaming). The pieces together cover the whole mix.
    /// </summary>
    IEnumerable<AudioData> Separate(AudioData mix, string source, CancellationToken cancellationToken = default);
}
