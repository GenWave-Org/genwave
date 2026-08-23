namespace GenWave.Host.Auth;

/// <summary>
/// Reads and writes the House Voice announce-token hash in <c>station.settings</c> (SPEC F145.3/.4,
/// STORY-360, PLAN T340). Deliberately NOT <see cref="GenWave.Host.Configuration.IStationSettingsStore"/>
/// — that seam enforces <see cref="GenWave.Host.Configuration.StationSettingsAllowlist"/> on every
/// read and write, and the hash MUST NOT be allowlisted so <c>GET/PUT /api/settings</c> can never
/// read or write it (the plaintext-never-in-a-settings-read-back rule, SPEC F145.3). This seam talks
/// to the same <c>station.settings</c> table directly, scoped to keys no other component ever reads
/// or writes — the same isolation <see cref="GenWave.Host.Seeding.ISafeLoopSeedMarkerStore"/>
/// established for the boot-seed marker (F27.10); this is that precedent's second application.
/// </summary>
public interface IAnnounceTokenStore
{
    /// <summary>
    /// The currently configured hash (hex-encoded SHA-256 of the plaintext token), or
    /// <see langword="null"/> when no token has ever been generated or the token has been revoked —
    /// the fail-closed "no hash row" state <see cref="GenWave.Host.Auth.AnnounceTokenAuthenticationHandler"/>
    /// treats as an automatic refusal regardless of what a caller presents. Read fresh from Postgres
    /// on every call — never cached, never boot-frozen — so a regenerate or revoke takes effect on
    /// the very next Bearer request with no api restart.
    /// </summary>
    Task<string?> ReadHashAsync(CancellationToken ct);

    /// <summary>
    /// Persists <paramref name="hash"/> as the current token hash, replacing whatever was there
    /// before (a regenerate implicitly invalidates the prior plaintext — its hash no longer matches
    /// anything stored).
    /// </summary>
    Task SetHashAsync(string hash, CancellationToken ct);

    /// <summary>
    /// Deletes the hash row outright — the honest "no hash row" state
    /// <see cref="ReadHashAsync"/> reads back as <see langword="null"/>, never an empty-string
    /// sentinel a comparison could accidentally match. Every previously issued plaintext is refused
    /// on its very next request.
    /// </summary>
    Task RevokeAsync(CancellationToken ct);

    /// <summary>
    /// Stamps the current instant as the token's last-successful-use time, for operator
    /// diagnosability (visible only via a direct <c>psql</c> query — never through the settings API,
    /// the same posture <c>SafeLoopSeedMarkerStore.MarkCompletedAsync</c>'s own remarks describe).
    /// Called once per successful Bearer authentication.
    /// </summary>
    Task StampLastUsedAsync(CancellationToken ct);
}
