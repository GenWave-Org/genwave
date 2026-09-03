using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Ads.Tests.Fakes;

/// <summary>Scriptable <see cref="IAdSpotSource"/> — returns a fixed answer, or throws a fixed
/// exception, and counts calls (the <c>FakeContextProvider</c>/<c>FakeTtsSynthesizer</c> shape this
/// codebase already uses for a single-method fake).</summary>
public sealed class FakeAdSpotSource : IAdSpotSource
{
    public MediaItem? Answer { get; set; }
    public Exception? ThrowOnNextCall { get; set; }
    public int CallCount { get; private set; }

    public ValueTask<MediaItem?> GetNextSpotAsync(CancellationToken ct)
    {
        CallCount++;
        if (ThrowOnNextCall is { } ex)
            throw ex;

        return ValueTask.FromResult(Answer);
    }
}
