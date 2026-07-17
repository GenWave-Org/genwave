namespace GenWave.Core.Domain;

/// <summary>
/// Start and end trim points for a media file (PRD §4.3). Measured once at library scan via silence
/// detection; applied by the playout engine to skip leading/trailing silence.
/// </summary>
/// <param name="CueInSec">Seconds from the start of the file at which audio begins.</param>
/// <param name="CueOutSec">Seconds from the start of the file at which audio ends.</param>
/// <exception cref="ArgumentException">Thrown when <paramref name="CueInSec"/> exceeds <paramref name="CueOutSec"/>.</exception>
public sealed record CuePoints(double CueInSec, double CueOutSec)
{
    public double CueInSec { get; init; } = CueInSec <= CueOutSec
        ? CueInSec
        : throw new ArgumentException(
            $"CueInSec ({CueInSec}) must not exceed CueOutSec ({CueOutSec}).",
            nameof(CueInSec));
}
