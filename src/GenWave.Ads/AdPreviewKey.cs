namespace GenWave.Ads;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GenWave.Core.Domain;

/// <summary>
/// The preview render's own staleness key (SPEC F174.4; STORY-424; PLAN T442) — a lowercase, 64-char
/// SHA256 hex digest over every input a preview render actually reads, so a reader can tell "this
/// spot's rendered preview still matches its current inputs" without re-rendering to find out (a
/// stored key that no longer matches a fresh <see cref="Compute"/> call is exactly what "stale"
/// means). Reuses <see cref="AdDeterministicSeed.Canonical"/>'s own newline-join (the SAME canonical
/// string shape this project already uses for its deterministic cast/bed seeding, SPEC F167.2) rather
/// than inventing a second one; unlike <see cref="AdDeterministicSeed.FromTerms"/> (a 4-byte RNG
/// seed), this hashes the FULL digest — a preview key must be collision-resistant across every
/// sponsor/spot combination a station ever renders, not merely well-distributed for an RNG.
/// </summary>
public static class AdPreviewKey
{
    /// <summary>
    /// Hashes every input a preview render reads, newline-joined in order: the spot's own script,
    /// voice plan (raw json, or empty), bed media id (or empty), spot length in seconds; the
    /// sponsor's Name, Tagline, About, Phone, Address, Website, Tone (each, or empty); then the LIVE
    /// render knobs — <paramref name="live"/>'s own AnnouncerVoice, CastVoices (comma-joined),
    /// BedFadeMs, and <paramref name="bedDuckDb"/>. A change to any one of these — an edit to the
    /// spot's own script/cast/bed, a sponsor detail update, or an operator changing a Live Ads
    /// setting — changes this digest.
    /// </summary>
    public static string Compute(AdSpot spot, Sponsor sponsor, AdLiveSettings live)
    {
        var canonical = AdDeterministicSeed.Canonical(
            spot.Script ?? "",
            spot.VoicePlan ?? "",
            spot.BedMediaId?.ToString(CultureInfo.InvariantCulture) ?? "",
            spot.SpotSeconds.ToString(CultureInfo.InvariantCulture),
            sponsor.Name,
            sponsor.Tagline ?? "",
            sponsor.About ?? "",
            sponsor.Phone ?? "",
            sponsor.Address ?? "",
            sponsor.Website ?? "",
            sponsor.Tone ?? "",
            live.AnnouncerVoice,
            string.Join(',', live.CastVoices),
            live.BedFadeMs.ToString(CultureInfo.InvariantCulture),
            live.BedDuckDb.ToString(CultureInfo.InvariantCulture));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexStringLower(hash);
    }
}
