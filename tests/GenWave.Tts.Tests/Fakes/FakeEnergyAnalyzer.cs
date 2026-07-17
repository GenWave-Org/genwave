namespace GenWave.Tts.Tests.Fakes;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>Mirrors FakeCueAnalyzer exactly — same seam, same API shape.</summary>
public sealed class FakeEnergyAnalyzer : IEnergyAnalyzer
{
    public int Calls { get; private set; }
    public string? LastPath { get; private set; }
    public double? LastCueInSec { get; private set; }
    public double? LastCueOutSec { get; private set; }
    public EnergyPoints? Result { get; set; } = new EnergyPoints(0.4, 0.6);
    public Exception? ThrowOnNextCall { get; set; }

    public FakeEnergyAnalyzer Returns(EnergyPoints? value)
    {
        Result = value;
        return this;
    }

    public FakeEnergyAnalyzer Throws(Exception ex)
    {
        ThrowOnNextCall = ex;
        return this;
    }

    public Task<EnergyPoints?> AnalyzeAsync(string path, double? cueInSec, double? cueOutSec, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        LastPath = path;
        LastCueInSec = cueInSec;
        LastCueOutSec = cueOutSec;

        if (ThrowOnNextCall is { } ex)
        {
            ThrowOnNextCall = null;
            throw ex;
        }

        Calls++;
        return Task.FromResult(Result);
    }
}
