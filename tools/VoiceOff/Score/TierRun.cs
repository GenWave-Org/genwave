using GenWave.VoiceOff.Listening;
using GenWave.VoiceOff.Render;
using GenWave.VoiceOff.Speech;
using GenWave.VoiceOff.Timing;

namespace GenWave.VoiceOff.Score;

/// <summary>Everything the scorer reads from one run folder, already loaded. <see cref="Votes"/> and <see cref="Winces"/> are in file order.</summary>
internal sealed record TierRun(
    string RunId,
    EngineTier Tier,
    string Host,
    DateOnly Date,
    string GauntletHash,
    IReadOnlyList<GateEntry> Gates,
    IReadOnlyList<TimingRecord> Timing,
    PairManifest Manifest,
    IReadOnlyList<Vote> Votes,
    IReadOnlyList<WinceEntry> Winces);
