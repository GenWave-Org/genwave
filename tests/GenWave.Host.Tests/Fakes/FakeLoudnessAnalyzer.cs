using GenWave.Core.Abstractions;

namespace GenWave.Host.Tests.Fakes;

/// <summary>
/// Returns a fixed loudness value without touching the filesystem.
/// Used in §0.1 gate tests where loudness measurement behaviour is irrelevant — the
/// §0.2 gate (T016) covers real measurement.
/// </summary>
sealed class FakeLoudnessAnalyzer : ILoudnessAnalyzer
{
    public Core.Domain.Loudness Fixed { get; set; } = new Core.Domain.Loudness(-16.0, -1.0, true);

    public Task<Core.Domain.Loudness> AnalyzeAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Fixed);
    }
}
