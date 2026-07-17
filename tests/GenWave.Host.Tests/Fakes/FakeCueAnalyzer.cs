using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Tests.Fakes;

/// <summary>
/// Returns null cue points without touching the filesystem.
/// Used in integration gate tests where cue measurement behaviour is irrelevant.
/// </summary>
sealed class FakeCueAnalyzer : ICueAnalyzer
{
    public Task<CuePoints?> AnalyzeAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<CuePoints?>(null);
    }
}
