namespace GenWave.Core.Domain;

/// <summary>
/// A bed (jingle/music bed) file to mix under a voice clip, optionally cue-trimmed to its own row's
/// cue points so silence never enters the loop (F27.4).
/// </summary>
/// <param name="Path">Absolute path to the bed audio file.</param>
/// <param name="CueInSec">Seconds into the bed at which usable audio begins; null plays from the file start.</param>
/// <param name="CueOutSec">Seconds into the bed at which usable audio ends; null plays to the file end.</param>
/// <exception cref="ArgumentException">
/// Thrown when both <paramref name="CueInSec"/> and <paramref name="CueOutSec"/> are present and
/// <paramref name="CueInSec"/> exceeds <paramref name="CueOutSec"/>.
/// </exception>
public sealed record BedSpec(string Path, double? CueInSec, double? CueOutSec)
{
    public double? CueInSec { get; init; } = CueInSec is null || CueOutSec is null || CueInSec <= CueOutSec
        ? CueInSec
        : throw new ArgumentException(
            $"CueInSec ({CueInSec}) must not exceed CueOutSec ({CueOutSec}).",
            nameof(CueInSec));
}
