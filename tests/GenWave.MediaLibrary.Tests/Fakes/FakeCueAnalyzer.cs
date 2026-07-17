using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.MediaLibrary.Tests.Fakes;

/// <summary>
/// Returns a configurable CuePoints value (or null) without touching the filesystem.
/// Can be configured to throw to test failure-isolation paths.
/// </summary>
sealed class FakeCueAnalyzer : ICueAnalyzer
{
    CuePoints? returnValue;
    Exception? throwOn;

    public int Calls { get; private set; }
    public string? LastPath { get; private set; }

    public void Returns(CuePoints? value)
    {
        returnValue = value;
        throwOn = null;
    }

    public void Throws(Exception ex)
    {
        throwOn = ex;
    }

    public Task<CuePoints?> AnalyzeAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Calls++;
        LastPath = path;

        if (throwOn is not null)
            throw throwOn;

        return Task.FromResult(returnValue);
    }
}
