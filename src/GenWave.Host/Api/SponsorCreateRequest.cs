namespace GenWave.Host.Api;

/// <summary>
/// <c>POST /api/sponsors</c>'s own request body (SPEC F171.3; STORY-408; PLAN T434) — always creates
/// an OWNER sponsor. <see cref="PackSlug"/> exists on this shape ONLY so a caller who supplies it can
/// be refused with a field-named 400 (<c>sponsor_pack_slug_forbidden</c>, STORY-408 AC3) rather than
/// the property being silently ignored — a pack-owned sponsor is created only by installing a pack
/// (<c>Abstractions.ISponsorStore.UpsertPackSponsorsAsync</c>), never through this admin-facing POST.
/// See <see cref="SponsorsController.Create"/>'s own remarks for why a plain nullable property is how
/// this codebase detects "the caller supplied this field at all" here — no
/// <c>[JsonExtensionData]</c>/strict-unmapped-member precedent exists anywhere in this project to hang
/// a different mechanism off of.
/// </summary>
/// <param name="Name">The sponsor's display name — required.</param>
/// <param name="PackSlug">MUST be omitted/null — any non-null value is refused, see the remarks
/// above.</param>
/// <param name="Tagline">A short pitch line, or <see langword="null"/>.</param>
/// <param name="About">A longer description, or <see langword="null"/>.</param>
/// <param name="Phone">A contact phone number, or <see langword="null"/>.</param>
/// <param name="Address">A physical address, or <see langword="null"/>.</param>
/// <param name="Website">A <c>http(s)://</c> URL, or <see langword="null"/>.</param>
/// <param name="Tone">A house tone hint, or <see langword="null"/>.</param>
public sealed record SponsorCreateRequest(
    string? Name,
    string? PackSlug,
    string? Tagline,
    string? About,
    string? Phone,
    string? Address,
    string? Website,
    string? Tone);
