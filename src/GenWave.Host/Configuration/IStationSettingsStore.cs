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

    /// <summary>
    /// Optimistic-concurrency write (gh-#486): persists <paramref name="value"/> under
    /// <paramref name="key"/> only if the key's currently stored version still matches
    /// <paramref name="expectedVersion"/> — the guard against a whole-array/document write (e.g.
    /// <c>Tts:Pronunciations</c>, <c>Tts:Corrections</c>) silently clobbering a concurrent editor's
    /// save (probed at T144: DELETE || PUT both 2xx, one edit vanished). <paramref name="expectedVersion"/>
    /// of <c>0</c> means "no row existed when I read this key" — a real row's version starts at 1 and
    /// only grows, so 0 can never collide with one.
    ///
    /// <para>
    /// Default-implemented as an unconditional <see cref="WriteAsync"/> so this addition to a
    /// published contract stays strictly additive: every pre-gh-#486 implementer/test double keeps
    /// compiling unchanged and simply always succeeds, exactly matching last-write-wins — the
    /// pre-gh-#486 behavior every one of them already tests. Only <c>StationSettingsStore</c>
    /// overrides this with the real version-checked write.
    /// </para>
    /// </summary>
    Task<SettingsWriteOutcome> WriteIfVersionMatchesAsync(
        string key, object value, long expectedVersion, CancellationToken cancellationToken = default) =>
        DefaultWriteIfVersionMatchesAsync(key, value, cancellationToken);

    /// <summary>Backing body for <see cref="WriteIfVersionMatchesAsync"/>'s default implementation —
    /// an interface default member can't itself carry the <see langword="async"/> modifier on an
    /// expression-bodied declaration, so the awaiting logic lives here instead.</summary>
    private async Task<SettingsWriteOutcome> DefaultWriteIfVersionMatchesAsync(
        string key, object value, CancellationToken cancellationToken)
    {
        await WriteAsync(key, value, cancellationToken);
        return SettingsWriteOutcome.Written;
    }

    /// <summary>
    /// Every currently stored key's version (gh-#486), the read half of the optimistic-concurrency
    /// guard <see cref="WriteIfVersionMatchesAsync"/> checks against — a key absent from the result
    /// has no row yet (expected version 0, <see cref="WriteIfVersionMatchesAsync"/>'s own "no row"
    /// sentinel).
    ///
    /// <para>
    /// Default-implemented as empty, the same additive-contract reasoning
    /// <see cref="WriteIfVersionMatchesAsync"/>'s own remarks give: a caller reading no version for a
    /// key it wants to guard falls back to an unconditional write for that key, so a pre-gh-#486 test
    /// double needs no changes to keep behaving exactly as it did before this addition.
    /// </para>
    /// </summary>
    Task<IReadOnlyDictionary<string, long>> ReadVersionsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, long>>(new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase));
}
