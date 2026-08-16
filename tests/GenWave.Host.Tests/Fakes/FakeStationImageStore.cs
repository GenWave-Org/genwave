using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Tests.Fakes;

/// <summary>
/// In-memory, seedable <see cref="IStationImageStore"/> double — Story335's own T298 route facts use
/// <see cref="Seed"/> to arrange (or omit) an owner-customized station image ahead of the dj-token
/// fallback read in <c>SpectatorArtworkController</c>, through <c>WebApplicationFactory&lt;Program&gt;</c>,
/// with no live Postgres fixture. Story339's own T307 write-path facts additionally drive
/// <see cref="UpsertAsync"/>/<see cref="DeleteAsync"/> through the real production
/// <c>StationImageController</c> route — this double now supports both. Mirrors
/// <see cref="FakePersonaAvatarStore"/>'s own minimal-contract idiom, INCLUDING its own
/// <see cref="GetCallCount"/>/<see cref="ThrowOnCallNumber"/>/<see cref="Gate"/> instrumentation
/// (Story339's own PLAN T307 rider: <c>StationImageCache</c>'s memo-TTL and cancellation-poisoning
/// facts need the identical scriptable-store shape <c>FakePersonaAvatarStore</c> already gives
/// Story336's own equivalent facts). Every OTHER call site in this test project constructs this in
/// its default (no throw, no gate) mode, so none of that instrumentation changes their behavior.
/// </summary>
sealed class FakeStationImageStore : IStationImageStore
{
    StationImage? current;

    /// <summary>Bumped on every <see cref="GetAsync"/> call — Story339's own hot-path-stays-cold and
    /// cancellation-poisoning facts (PLAN T307 rider, mirroring Story336's own
    /// <c>GetTokenByPersonaIdCallCount</c>) use this to prove <c>StationImageCache</c>'s ≤30s TTL
    /// memo issues exactly one store read per staleness window regardless of call volume.</summary>
    public int GetCallCount { get; private set; }

    /// <summary>1-based call number <see cref="GetAsync"/> throws on, when set — models a transient
    /// store outage. Mirrors <see cref="FakePersonaAvatarStore.ThrowOnCallNumber"/>'s own idiom. Null
    /// (the default) never throws.</summary>
    public int? ThrowOnCallNumber { get; set; }

    /// <summary>When set, <see cref="GetAsync"/> awaits and returns THIS instead of answering
    /// immediately from the seeded row — lets a fact interleave a caller's own cancellation with the
    /// shared fetch's own completion (mirrors <see cref="FakePersonaAvatarStore.Gate"/>'s own idiom).
    /// Null (the default) answers immediately, as before.</summary>
    public TaskCompletionSource<StationImage?>? Gate { get; set; }

    /// <summary>Arranges the row this double's <see cref="GetAsync"/> reports — <see langword="null"/>
    /// (the default) mirrors a station that has never customized its image.</summary>
    public void Seed(StationImage? image) => current = image;

    public Task<StationImage?> GetAsync(CancellationToken ct)
    {
        GetCallCount++;
        if (ThrowOnCallNumber == GetCallCount)
            throw new InvalidOperationException($"FakeStationImageStore: simulated failure on call {GetCallCount}");
        if (Gate is not null)
            return Gate.Task.WaitAsync(ct);
        return Task.FromResult(current);
    }

    /// <summary>The token-only projection (PLAN T307 fix round) — reads straight off the seeded row,
    /// same as the real <c>StationImageRepository.GetTokenAsync</c>'s own agreement with its whole-row
    /// sibling; does NOT bump <see cref="GetCallCount"/> (that counter is scoped to <see cref="GetAsync"/>
    /// alone — <c>StationImageCache</c>'s own TTL proofs; this method's one caller,
    /// <c>AuthController.Stations</c>, never goes through that cache).</summary>
    public Task<string?> GetTokenAsync(CancellationToken ct) => Task.FromResult(current?.Token);

    public Task UpsertAsync(StationImageInput image, CancellationToken ct)
    {
        current = new StationImage(image.Bytes, image.ByteSize, image.Sha256, image.Token, DateTime.UtcNow);
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(CancellationToken ct)
    {
        var existed = current is not null;
        current = null;
        return Task.FromResult(existed);
    }
}
