namespace GenWave.Host.Api;

/// <summary>
/// <see cref="VoicePackController.Install"/>'s own 200 body (SPEC F164.5, STORY-395, PLAN T413) —
/// mirrors <see cref="AvatarPackInstallResponse"/>/<see cref="FontPackInstallResponse"/>'s own shape:
/// just enough for the shelf to confirm what landed, never a re-serialization of the whole manifest.
/// </summary>
/// <param name="Slug">The catalog entry slug this pack was installed from.</param>
/// <param name="PackName">The manifest's own display name.</param>
/// <param name="VoiceIds">Every voice id this install wrote, in manifest order.</param>
public sealed record VoicePackInstallResponse(string Slug, string PackName, IReadOnlyList<string> VoiceIds);
