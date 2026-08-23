using System.Text.Json;
using GenWave.MediaLibrary.Station;

namespace GenWave.Host.Auth;

/// <summary>
/// <see cref="IAnnounceTokenStore"/> backed by <see cref="StationSettingsRepository"/> against the
/// same <c>station.settings</c> table <see cref="GenWave.Host.Configuration.StationSettingsStore"/>
/// writes to, reached through a separate, narrower seam so the two keys below can never be
/// allowlisted by accident — the exact <c>SafeLoopSeedMarkerStore</c> precedent (F27.10) applied to a
/// second machine-owned key family (SPEC F145.3/.4, STORY-360, PLAN T340).
///
/// <b>Two keys, not one.</b> <see cref="HashKey"/> and <see cref="LastUsedAtKey"/> are independent
/// rows: stamping last-used never re-reads or re-writes the hash, so a last-used write racing a
/// concurrent regenerate/revoke can never clobber it (and vice versa) — each settings row's own
/// upsert is already atomic, but only because these are two separate rows, not one JSON document
/// carrying both fields.
///
/// <b>No caching, no <see cref="Microsoft.Extensions.Options.IOptionsMonitor{T}"/>.</b> Unlike every
/// allowlisted key (which rides the configuration overlay's reload-on-write signal),
/// <see cref="ReadHashAsync"/> opens a fresh connection and reads the row directly on every call —
/// the carry-forward requirement that <c>AnnounceTokenAuthenticationHandler</c> see the CURRENT hash
/// per request, live, not boot-frozen. This key is deliberately outside the overlay entirely (never
/// allowlisted), so there is no reload signal to ride even if this class wanted one.
///
/// Constructing this type opens no connection (mirrors <see cref="StationSettingsRepository"/>'s own
/// "connection-per-call" contract) — the composition-root snapshot this repo's SEAMS.md generator
/// takes must never trip a socket merely by resolving the singleton.
/// </summary>
public sealed class AnnounceTokenStore(string connectionString) : IAnnounceTokenStore
{
    readonly StationSettingsRepository repository = new(connectionString);

    /// <summary>
    /// The hash key. Lives outside the <c>Station:*</c>/<c>Announcements:*</c> allowlisted namespace
    /// by construction — see <see cref="IAnnounceTokenStore"/>'s own remarks for why it must never be
    /// allowlisted. Matches the SPEC F145.3 wire name (<c>Announcements:TokenHash</c>) — the allowlist
    /// exclusion, not the key's own spelling, is what keeps it out of <c>GET /api/settings</c>.
    /// </summary>
    public const string HashKey = "Announcements:TokenHash";

    /// <summary>The last-successful-use timestamp key — same exclusion, own row.</summary>
    public const string LastUsedAtKey = "Announcements:TokenLastUsedAt";

    /// <inheritdoc/>
    public async Task<string?> ReadHashAsync(CancellationToken ct)
    {
        var stored = await repository.ReadValueAsync(HashKey, ct);
        if (stored is null)
            return null;

        // Stored via WriteAsync, which JSON-serializes its value — a plain string round-trips as a
        // JSON string literal (quoted), so this undoes that one layer of encoding (the same
        // scalar-unwrap StationSettingsConfigurationProvider.ExtractScalar performs for the
        // allowlisted overlay). A malformed row can never have been written by this class itself —
        // treated as "no usable hash" (fail-closed) rather than letting a JsonException surface as a
        // 500 on the auth path.
        try
        {
            return JsonSerializer.Deserialize<string>(stored);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    ///
    /// <remarks>
    /// Clears <see cref="LastUsedAtKey"/> too (T344 review finding F2) — <see cref="ReadLastUsedAsync"/>'s
    /// own doc comment promises "the CURRENT token", and a regenerate installs a brand-new plaintext
    /// no request has authenticated with yet; leaving the prior token's stamp in place would misreport
    /// a token that has never been used as already used.
    /// </remarks>
    public async Task SetHashAsync(string hash, CancellationToken ct)
    {
        await repository.WriteAsync(HashKey, hash, ct);
        await repository.DeleteAsync(LastUsedAtKey, ct);
    }

    /// <inheritdoc/>
    ///
    /// <remarks>
    /// Clears <see cref="LastUsedAtKey"/> too (T344 review finding F2) — <see cref="GenWave.Host.Api.AnnounceTokenStatusDto"/>'s
    /// own doc comment scopes <c>LastUsedAt</c> to "the current token (if any)"; once revoked there is
    /// no current token, so a stale stamp from the revoked one must not survive to misreport recency
    /// for whatever token (if any) is generated next.
    /// </remarks>
    public async Task RevokeAsync(CancellationToken ct)
    {
        await repository.DeleteAsync(HashKey, ct);
        await repository.DeleteAsync(LastUsedAtKey, ct);
    }

    /// <inheritdoc/>
    public Task StampLastUsedAsync(CancellationToken ct) =>
        repository.WriteAsync(LastUsedAtKey, DateTimeOffset.UtcNow, ct);

    /// <inheritdoc/>
    public async Task<DateTimeOffset?> ReadLastUsedAsync(CancellationToken ct)
    {
        var stored = await repository.ReadValueAsync(LastUsedAtKey, ct);
        if (stored is null)
            return null;

        // Same "undo the one layer of JSON-string encoding WriteAsync applied, fail closed on a
        // malformed row" posture as ReadHashAsync's own remarks — a malformed row can never have
        // been written by this class itself.
        try
        {
            return JsonSerializer.Deserialize<DateTimeOffset?>(stored);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
