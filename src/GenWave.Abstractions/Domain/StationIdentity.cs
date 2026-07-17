namespace GenWave.Core.Domain;

/// <summary>
/// Station identity needed to run one station. Config-bound for v1; becomes a DB entity in
/// the multi-tenancy phase (T009 forward).
///
/// <para>
/// Retires <see cref="Abstractions.IStationIdentityProvider"/>'s predecessor, the boot-frozen
/// <c>StationContext</c> singleton (SPEC F44.1, gitea-#196): <c>Station:Name</c> and <c>Station:Voice</c>
/// are advertised <c>Live</c> in the settings allowlist, so a value built once at boot and never
/// re-read is the same class of bug F30/gitea-#211 fixed for scope and cadence. Consumers read identity
/// through <see cref="Abstractions.IStationIdentityProvider.Current"/> instead, at use time, never
/// caching this record in a field.
/// </para>
///
/// <para>
/// Carries no library scope (SPEC F30.1, STORY-102): the rotation scope is read live via
/// <see cref="Abstractions.IStationScopeProvider"/> instead.
/// </para>
///
/// <para>
/// Carries no cadence either, for the identical reason (gitea-#211 — F30.1's precedent applied to
/// cadence): the live cadence is read via <see cref="Abstractions.ICadenceProvider"/> instead.
/// </para>
/// </summary>
/// <param name="Id">Stable machine identifier for this station.</param>
/// <param name="Name">Human-readable name used in TTS templates.</param>
/// <param name="Voice">Default TTS voice identifier for this station.</param>
public sealed record StationIdentity(
    string Id,
    string Name,
    string Voice);
