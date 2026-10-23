namespace GenWave.VoiceOff.Tests.Support;

/// <summary>
/// What a <see cref="PairScenario"/> run folder holds. <paramref name="FailedRenders"/> and <paramref name="CorruptClips"/> (rendered ok, but the wav is unreadable) are "engine/lane/lineNumber" (lane is plain or markup, lines count from 1);
/// <paramref name="Tones"/> writes real tones of known lengths (see <see cref="ToneClips"/>) instead of silent clips. <paramref name="LinesByChallenger"/> caps how many lines a challenger renders (the rest are failed renders). A failed container or voiceSelect engine still has clips, so only the gate filter can keep it out of the pairs.
/// </summary>
internal sealed record PairFixtureOptions(
    int Lines = 10,
    IReadOnlyList<string>? Challengers = null,
    IReadOnlyList<string>? FailedRenders = null,
    IReadOnlyList<string>? FailedContainers = null,
    IReadOnlyList<string>? FailedVoiceSelects = null,
    string? RunHashOverride = null,
    int Seed = 7,
    IReadOnlyList<string>? CorruptClips = null,
    IReadOnlyDictionary<string, int>? LinesByChallenger = null,
    bool Tones = false);
