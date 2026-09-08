namespace GenWave.Core.Domain;

/// <summary>
/// One already-installed voice id colliding with a candidate a new voice-pack install is about to
/// write (SPEC F166.4, PLAN T413) — the result shape for
/// <see cref="Abstractions.IVoicePackStore.FindInstalledVoiceIdsAsync"/>'s pre-write advisory check,
/// run BEFORE any asset is fetched so a doomed install fails fast without touching disk.
/// </summary>
/// <param name="VoiceId">The colliding voice id, already installed under a different pack.</param>
/// <param name="OwnerSlug">The slug of the pack that already owns <paramref name="VoiceId"/>.</param>
public sealed record VoicePackVoiceOwner(string VoiceId, string OwnerSlug);
