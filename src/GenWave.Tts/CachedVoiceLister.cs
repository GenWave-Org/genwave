namespace GenWave.Tts;

using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;

/// <summary>
/// TTL-memoizing decorator over <see cref="ITtsVoiceLister"/> (SPEC F29.4, STORY-097 AC2). Serves
/// the last successful upstream response for <paramref name="ttl"/> without a Kokoro round-trip; on
/// a cold/expired cache it calls through, and a failure there propagates so the caller (the voices
/// endpoint) can translate it to 502 ProblemDetails (STORY-097 AC3) — this decorator never swallows
/// an upstream fault.
///
/// A plain lock-guarded timestamped field, not <c>IMemoryCache</c> — the host is a singleton
/// process holding exactly one cached value, so a dependency for generalized eviction/expiration
/// policy would be unused weight (YAGNI).
///
/// The cached entry is stamped with the <c>Tts:Endpoint</c> it was fetched from (SPEC F36.4): a live
/// PUT that repoints the endpoint mid-TTL must not keep serving the OLD endpoint's voice list for up
/// to <paramref name="ttl"/> longer — the very next <c>GET /api/voices</c> after a repoint is a
/// deliberate cache miss that re-fetches from the new endpoint, even though the TTL window hasn't
/// elapsed.
/// </summary>
public sealed class CachedVoiceLister(
    ITtsVoiceLister inner, IOptionsMonitor<TtsOptions> optionsMonitor, TimeSpan ttl) : ITtsVoiceLister
{
    readonly object gate = new();
    IReadOnlyList<string>? cached;
    DateTimeOffset cachedAt;
    string? cachedEndpoint;

    public async Task<IReadOnlyList<string>> ListVoicesAsync(CancellationToken ct)
    {
        var currentEndpoint = optionsMonitor.CurrentValue.Endpoint;
        if (TryGetFresh(currentEndpoint, out var fresh))
            return fresh;

        var voices = await inner.ListVoicesAsync(ct);

        lock (gate)
        {
            cached = voices;
            cachedAt = DateTimeOffset.UtcNow;
            cachedEndpoint = currentEndpoint;
        }

        return voices;
    }

    bool TryGetFresh(string currentEndpoint, out IReadOnlyList<string> voices)
    {
        lock (gate)
        {
            if (cached is { } value
                && cachedEndpoint == currentEndpoint
                && DateTimeOffset.UtcNow - cachedAt < ttl)
            {
                voices = value;
                return true;
            }
        }

        voices = [];
        return false;
    }
}
