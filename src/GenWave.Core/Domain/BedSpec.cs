namespace GenWave.Core.Domain;

/// <summary>
/// A bed (jingle/music bed) file to mix under a voice clip, optionally cue-trimmed to its own row's
/// cue points so silence never enters the loop (F27.4).
/// </summary>
/// <param name="Path">Absolute path to the bed audio file.</param>
/// <param name="CueInSec">Seconds into the bed at which usable audio begins; null plays from the file start.</param>
/// <param name="CueOutSec">Seconds into the bed at which usable audio ends; null plays to the file end.</param>
/// <param name="IntegratedLufs">The bed's already-measured integrated loudness (the catalog row's own
/// <c>integrated_lufs</c>, stamped at enrichment/install) so the mixer can place the bed a known number
/// of dB under the voice without a second measurement; null = unmeasured, the mixer measures the file
/// itself (gh-#746).</param>
/// <exception cref="ArgumentException">
/// Thrown when both <paramref name="CueInSec"/> and <paramref name="CueOutSec"/> are present and
/// <paramref name="CueInSec"/> exceeds <paramref name="CueOutSec"/>.
/// </exception>
public sealed record BedSpec(string Path, double? CueInSec, double? CueOutSec, double? IntegratedLufs = null)
{
    public double? CueInSec { get; init; } = CueInSec is null || CueOutSec is null || CueInSec <= CueOutSec
        ? CueInSec
        : throw new ArgumentException(
            $"CueInSec ({CueInSec}) must not exceed CueOutSec ({CueOutSec}).",
            nameof(CueInSec));
}
