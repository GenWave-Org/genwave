namespace GenWave.Host.Api;

/// <summary>
/// Public shape for the on-air show identity carried on <c>GET /spectator/api/now-playing</c>'s
/// on-air shapes (SPEC F116.4, F115.3; STORY-311, PLAN T251): the resolver-sourced
/// <see cref="GenWave.Core.Domain.ShowSummary"/>, narrowed to exactly the two fields SPEC F115.3
/// pins PUBLIC. <see cref="GenWave.Core.Domain.ShowSummary.Flavor"/> — prompt-only, private
/// forever — has NO member here at all: a public payload can never forward it because there is
/// nothing on this type to forward it THROUGH (F62.9/F115.3 disclosure-by-construction), matching
/// how <see cref="SpectatorTrackNowPlaying"/> already excludes media id/gain/loudness by the same
/// discipline.
/// </summary>
/// <param name="Name">The show's display name.</param>
/// <param name="Tagline">
/// Public, broadcast-shaped (SPEC F115.3) — safe for the spectator surface. <see langword="null"/>
/// when the show carries none.
/// </param>
public sealed record SpectatorShow(string Name, string? Tagline);
