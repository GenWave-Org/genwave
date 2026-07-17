namespace GenWave.Host.Configuration;

/// <summary>
/// Writes operator-supplied settings to <c>station.settings</c> and signals the configuration
/// provider to reload so <see cref="Microsoft.Extensions.Options.IOptionsMonitor{T}"/> re-binds
/// without an API restart.
///
/// Only keys present in <see cref="StationSettingsAllowlist"/> may be written. Attempting to
/// write a disallowed key (including any secret) is rejected at this boundary.
/// </summary>
public interface IStationSettingsStore
{
    /// <summary>
    /// Persists <paramref name="value"/> under <paramref name="key"/> and triggers a live
    /// configuration reload.
    /// </summary>
    /// <param name="key">Configuration key (must be in <see cref="StationSettingsAllowlist"/>).</param>
    /// <param name="value">
    /// The JSON-serialisable value. Stored as JSONB in <c>station.settings</c>.
    /// </param>
    /// <param name="cancellationToken">Propagated to the DB write.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="key"/> is not on the allowlist.
    /// </exception>
    Task WriteAsync(string key, object value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all currently stored settings that are on the allowlist, keyed by configuration key.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> ReadAllAsync(CancellationToken cancellationToken = default);
}
