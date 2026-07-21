namespace GenWave.Tts.Tests.Fakes;

using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Builds a real <see cref="SpeechCorrectionProvider"/> configured with no operator corrections —
/// what every render/cache spec that isn't itself exercising corrections needs just to satisfy
/// <see cref="TtsSegmentSource"/>'s constructor (its cache key folds in
/// <see cref="SpeechCorrectionProvider.ContentHash"/>, SPEC F68.5). A real provider rather than a
/// hand-rolled test double: it is a small, side-effect-free class and using the genuine type here
/// keeps every one of these specs on the same code path Story185's spec exercises end to end.
/// </summary>
public static class NoCorrections
{
    public static SpeechCorrectionProvider Provider() =>
        new(
            new TestOptionsMonitor<TtsCorrectionsOptions>(new TtsCorrectionsOptions()),
            NullLogger<SpeechCorrectionProvider>.Instance);
}
