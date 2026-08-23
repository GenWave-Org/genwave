using GenWave.Host.Auth;

namespace GenWave.Host.Tests.Fakes;

/// <summary>
/// Stateful double for <see cref="IAnnounceTokenStore"/> (STORY-360, PLAN T340) — mirrors
/// <c>FakeAnnouncementStore</c>'s own "an in-memory stand-in for the real Postgres-backed store, no
/// live DB reached" shape one seam over. <see cref="Hash"/> starts <see langword="null"/> (the SPEC
/// F145.4 "no hash row" fail-closed state) exactly like a fresh station's <c>station.settings</c>
/// table would.
/// </summary>
sealed class FakeAnnounceTokenStore : IAnnounceTokenStore
{
    public string? Hash { get; set; }

    public DateTimeOffset? LastUsedAt { get; private set; }

    /// <summary>How many times a Bearer authentication actually succeeded against this store —
    /// what <c>StampLastUsedAsync</c> call count proves without a real clock dependency.</summary>
    public int StampCalls { get; private set; }

    public Task<string?> ReadHashAsync(CancellationToken ct) => Task.FromResult(Hash);

    public Task SetHashAsync(string hash, CancellationToken ct)
    {
        Hash = hash;
        return Task.CompletedTask;
    }

    public Task RevokeAsync(CancellationToken ct)
    {
        Hash = null;
        return Task.CompletedTask;
    }

    public Task StampLastUsedAsync(CancellationToken ct)
    {
        StampCalls++;
        LastUsedAt = DateTimeOffset.UtcNow;
        return Task.CompletedTask;
    }
}
