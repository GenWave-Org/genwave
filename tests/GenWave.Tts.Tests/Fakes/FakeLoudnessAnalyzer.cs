namespace GenWave.Tts.Tests.Fakes;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

public sealed class FakeLoudnessAnalyzer : ILoudnessAnalyzer
{
    public Loudness Loudness { get; set; } = new Loudness(-16.0, -1.0, true);

    /// <summary>Path passed to the most recent AnalyzeAsync call.</summary>
    public string? LastPath { get; private set; }

    /// <summary>When non-null, the next call to AnalyzeAsync will throw this exception.</summary>
    public Exception? ThrowOnNextCall { get; set; }

    public Task<Loudness> AnalyzeAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        LastPath = path;

        if (ThrowOnNextCall is { } ex)
        {
            ThrowOnNextCall = null;
            throw ex;
        }

        return Task.FromResult(Loudness);
    }
}
