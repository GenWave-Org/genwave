using GenWave.VoiceOff.Speech;
using GenWave.VoiceOff.Timing;

namespace GenWave.VoiceOff.Render;

/// <summary>What one engine's turn in the run produced: its gate row, and (when it was rendered) its records, peaks and whether it was given up on.</summary>
internal sealed record EngineRunResult(
    GateEntry Gates,
    IReadOnlyList<TimingRecord> Records,
    ResourceUsage Usage,
    bool StoppedAnswering);
