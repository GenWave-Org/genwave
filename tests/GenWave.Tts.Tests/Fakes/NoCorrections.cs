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

    /// <summary>
    /// Sibling of <see cref="Provider"/> on the card side of the F71.7 merge: a real
    /// <see cref="ActivePersonaCorrectionsCache"/> over an accessor with no active persona at all —
    /// what every render/cache spec that isn't itself exercising the persona-card cache needs just
    /// to satisfy <see cref="TtsSegmentSource"/>'s constructor (its cache key also folds in
    /// <see cref="ActivePersonaCorrectionsCache.ContentHash"/>). <see cref="TimeProvider.System"/> is
    /// fine here: with no card ever returned, every refresh folds to the same stable
    /// "no-card-corrections" sentinel regardless of when it runs.
    /// </summary>
    public static ActivePersonaCorrectionsCache PersonaCache() =>
        new(new FakeActivePersonaAccessor(), TimeProvider.System);
}
