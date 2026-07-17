using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.MediaLibrary.Tests.Fakes;

/// <summary>
/// Returns a configurable EnergyPoints value (or null) without touching the filesystem.
/// Can be configured to throw to test failure-isolation paths.
/// Mirrors FakeCueAnalyzer exactly — same seam, same API shape.
/// </summary>
sealed class FakeEnergyAnalyzer : IEnergyAnalyzer
{
    EnergyPoints? returnValue;
    Exception? throwOn;

    public int Calls { get; private set; }
    public string? LastPath { get; private set; }
    public double? LastCueInSec { get; private set; }
    public double? LastCueOutSec { get; private set; }

    public void Returns(EnergyPoints? value)
    {
        returnValue = value;
        throwOn = null;
    }

    public void Throws(Exception ex)
    {
        throwOn = ex;
    }

    public Task<EnergyPoints?> AnalyzeAsync(string path, double? cueInSec, double? cueOutSec, CancellationToken ct)
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
