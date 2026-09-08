using GenWave.Core.Abstractions;

namespace GenWave.Ads.Tests.Fakes;

/// <summary>
/// <see cref="IAdBedPool"/> double (SPEC F168.1; STORY-403; PLAN T416) — an in-memory pool keyed by
/// library id, the SAME "seed only what a scenario needs, count every call" shape
/// <see cref="FakeAdsLibraryStore"/> already keeps one file over. A library id nothing ever seeded
/// returns an empty pool — SPEC F168.2's own honest "no pack installed yet" answer is the default,
/// not an exceptional case a scenario must opt into.
/// </summary>
public sealed class FakeAdBedPool : IAdBedPool
{
    readonly Dictionary<long, IReadOnlyList<long>> poolsByLibraryId = [];

    public int CallCount { get; private set; }
    public long? LastLibraryId { get; private set; }

    /// <summary>When non-null, the next call to <see cref="ListReadyBedIdsAsync"/> throws this
    /// exception instead of returning a pool — the SAME single-shot throw shape
    /// <c>GenWave.Tts.Tests.Fakes.FakeAudioMixer.ThrowOnNextCall</c> already keeps, PLAN T416 review
    /// O2's own guardian-net fact (a bed-pool failure must leave the spot stuck
    /// <see cref="GenWave.Core.Domain.AdState.Rendering"/>, never silently degrade to "no pack
    /// installed").</summary>
    public Exception? ThrowOnNextCall { get; set; }

    /// <summary>Seeds the exact ordered pool <paramref name="libraryId"/> returns from then on — a
    /// scenario asserting on <see cref="GenWave.Ads.AdBedPicker.Pick"/>'s own deterministic index into
    /// it needs an exact, known ordering, not whatever a `params` array's own default ordering would
    /// otherwise imply.</summary>
    public FakeAdBedPool Seed(long libraryId, params long[] orderedBedIds)
    {
        poolsByLibraryId[libraryId] = orderedBedIds;
        return this;
    }

    public Task<IReadOnlyList<long>> ListReadyBedIdsAsync(long libraryId, CancellationToken ct)
    {
        CallCount++;
        LastLibraryId = libraryId;

        if (ThrowOnNextCall is { } ex)
        {
            ThrowOnNextCall = null;
            throw ex;
        }

        var pool = poolsByLibraryId.TryGetValue(libraryId, out var seeded) ? seeded : [];
        return Task.FromResult(pool);
    }
}
