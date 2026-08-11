namespace GenWave.Host.Api;

/// <summary>
/// The show identity nested under <see cref="SpectatorUpNext"/> (SPEC F116.4; STORY-311, PLAN
/// T251) — deliberately a DIFFERENT, narrower type from <see cref="SpectatorShow"/> rather than
/// the same shape with a nulled <c>Tagline</c>: F116.4 pins <c>upNext.show</c> as NAME ONLY, so
/// this type simply has no <c>Tagline</c> member (F62.9 disclosure-by-construction) — the same
/// discipline <see cref="SpectatorUpNext"/>'s own <c>Dj</c> already applies to the upcoming
/// segment's persona (a name, never the full identity).
/// </summary>
/// <param name="Name">The upcoming segment's show display name.</param>
public sealed record SpectatorUpNextShow(string Name);
