using GenWave.Core.Abstractions;
using LoudnessMeasurement = GenWave.Core.Domain.Loudness;

namespace GenWave.VoiceOff.Tests.Support;

/// <summary>An analyzer that reports a fixed loudness, to drive the verify step without real audio quirks.</summary>
internal sealed class FixedLoudnessAnalyzer(double lufs, bool measurable = true) : ILoudnessAnalyzer
{
    public Task<LoudnessMeasurement> AnalyzeAsync(string path, CancellationToken ct) => Task.FromResult(new LoudnessMeasurement(lufs, -3.0, measurable));
}
