using GenWave.Core.Abstractions;

namespace GenWave.MediaLibrary.Tests.Fakes;

/// <summary>
/// Returns a configurable nullable BPM value without touching the filesystem or invoking the real
/// aubio binary (SPEC F46.5 — unit tests must never invoke it). Can be configured to throw to test
/// failure-isolation paths. Mirrors FakeEnergyAnalyzer exactly — same seam, same API shape.
/// </summary>
sealed class FakeBpmAnalyzer : IBpmAnalyzer
{
    double? returnValue;
    Exception? throwOn;

    public int Calls { get; private set; }
    public string? LastPath { get; private set; }
    public double? LastCueInSec { get; private set; }
    public double? LastCueOutSec { get; private set; }

    public void Returns(double? value)
    {
        returnValue = value;
        throwOn = null;
    }

    public void Throws(Exception ex)
    {
        throwOn = ex;
    }

    public Task<double?> AnalyzeAsync(string path, double? cueInSec, double? cueOutSec, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Calls++;
        LastPath = path;
        LastCueInSec = cueInSec;
        LastCueOutSec = cueOutSec;

        if (throwOn is not null)
            throw throwOn;

        return Task.FromResult(returnValue);
    }
}
