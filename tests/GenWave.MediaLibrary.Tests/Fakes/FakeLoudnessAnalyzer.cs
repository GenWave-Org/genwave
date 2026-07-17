using GenWave.Core.Abstractions;

namespace GenWave.MediaLibrary.Tests.Fakes;

/// <summary>
/// Returns a configurable loudness value without touching the filesystem.
/// Can be configured to return an unmeasurable result or to throw, to exercise
/// the loudness-failure isolation path in EnrichmentService.
/// </summary>
sealed class FakeLoudnessAnalyzer : ILoudnessAnalyzer
{
    public GenWave.Core.Domain.Loudness Fixed { get; set; } = new GenWave.Core.Domain.Loudness(-16.0, -1.0, true);
    public int Calls { get; private set; }
    public string? LastPath { get; private set; }

    Exception? throwOn;

    public void ReturnsUnmeasurable()
    {
        Fixed = new GenWave.Core.Domain.Loudness(0.0, 0.0, false);
        throwOn = null;
    }

    public void Throws(Exception ex)
    {
        throwOn = ex;
    }

    public Task<GenWave.Core.Domain.Loudness> AnalyzeAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Calls++;
        LastPath = path;

        if (throwOn is not null)
            throw throwOn;

        return Task.FromResult(Fixed);
    }
}
