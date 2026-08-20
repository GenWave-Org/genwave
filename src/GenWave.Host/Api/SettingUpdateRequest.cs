namespace GenWave.Host.Api;

/// <summary>
/// A single key/value pair supplied in the body of <c>PUT /api/settings</c>.
/// </summary>
/// <param name="Key">Configuration key.</param>
/// <param name="Value">The new value, wire-encoded per the key's <see cref="GenWave.Host.Configuration.SettingKind"/>.</param>
/// <param name="ExpectedVersion">
/// Optimistic-concurrency guard (gh-#486): when present, <paramref name="Key"/> is written only if
/// its currently stored version still matches — read off <see cref="SettingDto.Version"/> from a
/// prior <c>GET /api/settings</c>. A mismatch rejects the WHOLE batch with 409 before any later
/// entry in the request is attempted (entries earlier in the batch may already have committed — the
/// same non-transactional, sequential-write posture this endpoint already had before gh-#486).
/// <see langword="null"/> (the default) skips the guard entirely — an unconditional last-write-wins
/// write, unchanged pre-gh-#486 behavior for every key that doesn't opt in.
/// </param>
public sealed record SettingUpdateRequest(string Key, string Value, long? ExpectedVersion = null);
