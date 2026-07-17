namespace GenWave.Tts.Tests.Fakes;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

public sealed class FakeCueAnalyzer : ICueAnalyzer
{
    public int Calls { get; private set; }
    public string? LastPath { get; private set; }
    public CuePoints? Result { get; set; } = new CuePoints(0.5, 10.0);
    public Exception? ThrowOnNextCall { get; set; }

    public FakeCueAnalyzer Returns(CuePoints? value)
    {
        Result = value;
        return this;
    }

    public FakeCueAnalyzer Throws(Exception ex)
    {
        ThrowOnNextCall = ex;
        return this;
    }

    public Task<CuePoints?> AnalyzeAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        LastPath = path;

        if (ThrowOnNextCall is { } ex)
        {
            ThrowOnNextCall = null;
            throw ex;
        }

        Calls++;
        return Task.FromResult(Result);
    }
}
