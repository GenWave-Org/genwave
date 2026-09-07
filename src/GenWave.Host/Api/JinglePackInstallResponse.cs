namespace GenWave.Host.Api;

/// <summary>
/// <see cref="JinglePackController.Install"/>'s own 200 body (SPEC F165.5, STORY-399, PLAN T414) —
/// mirrors <see cref="VoicePackInstallResponse"/>'s own shape: just enough for the caller to confirm
/// what actually landed, never a re-serialization of the whole manifest (that stays server-side, in
/// <c>station.jingle_pack.definition</c>).
/// </summary>
/// <param name="Slug">The catalog entry slug this pack was installed from.</param>
/// <param name="PackName">The manifest's own display name.</param>
/// <param name="Assets">Every asset this install wrote, in manifest order.</param>
public sealed record JinglePackInstallResponse(string Slug, string PackName, IReadOnlyList<JinglePackInstalledAssetResponse> Assets);

/// <summary>One asset named in a <see cref="JinglePackInstallResponse"/>.</summary>
/// <param name="File">The manifest-declared file name.</param>
/// <param name="Role">The manifest-declared jingle role (<c>bed</c>/<c>sting</c>/<c>station_id</c>).</param>
/// <param name="MediaId">The <c>library.media</c> row id this asset landed as.</param>
public sealed record JinglePackInstalledAssetResponse(string File, string Role, long MediaId);
