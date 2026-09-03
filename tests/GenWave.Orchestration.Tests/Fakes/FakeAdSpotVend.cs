using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Orchestration.Tests.Fakes;

/// <summary>Scriptable <see cref="IAdSpotVend"/> — returns a fixed answer, or throws a fixed
/// exception, and counts calls (mirrors <c>GenWave.Ads.Tests.Fakes.FakeAdSpotSource</c>'s own shape
/// one project over — this project cannot reference that one, GenWave.Orchestration.Tests owning its
/// own copy of the same single-method-fake idiom).</summary>
sealed class FakeAdSpotVend : IAdSpotVend
{
    public MediaItem? Answer { get; set; }
    public Exception? ThrowOnNextCall { get; set; }
    public int CallCount { get; private set; }

    public Task<MediaItem?> GetNextSpotAsync(CancellationToken ct)
    {
        CallCount++;
        if (ThrowOnNextCall is { } ex)
        {
            ThrowOnNextCall = null;
            throw ex;
        }

        return Task.FromResult(Answer);
    }
}
