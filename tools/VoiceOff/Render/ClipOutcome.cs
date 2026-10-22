using GenWave.VoiceOff.Speech;

namespace GenWave.VoiceOff.Render;

/// <summary>
/// How one clip went. <paramref name="ErrorKind"/> is set when an engine call failed, so the run can tell a dead
/// engine from a clip it simply could not use. <paramref name="Linear"/> is the loudness match's decision, when it ran.
/// </summary>
internal sealed record ClipOutcome(
    bool Ok,
    string? Error,
    SpeechErrorKind? ErrorKind,
    double WallMs,
    double AudioMs,
    bool? Linear)
{
    internal static ClipOutcome Failed(string error, double wallMs, SpeechErrorKind? kind = null) =>
        new(false, error, kind, wallMs, 0, null);
}
