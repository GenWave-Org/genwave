namespace GenWave.Host.Api;

/// <summary>
/// 200 response body for <c>GET /api/catalog/entries/{slug}</c> (SPEC F90.2, F90.3, F90.4a) once a
/// real entry is being served. <see cref="Card"/>/<see cref="Meta"/> stay the RAW hash-verified
/// JSON text — the same <c>&lt;slug&gt;.persona.json</c>/<c>&lt;slug&gt;.meta.json</c> bytes a
/// hand-downloaded copy would carry (SPEC F90.5: the review-then-import flow deserializes the card
/// itself at import time through the EXISTING F79 import endpoint — this endpoint never duplicates
/// that parsing).
///
/// <para>
/// <see cref="Card"/> KEEPS ITS PERSONA-ERA NAME DELIBERATELY (F103.2 review call, T176): the
/// internal entry model generalises its two-file fields from persona-specific <c>card</c> to the
/// neutral <c>manifest</c> (<see cref="Catalog.CatalogEntrySummary.Manifest"/>,
/// <see cref="Catalog.CatalogEntryContent.ManifestJson"/>), but THIS wire field is what the Admin
/// UI's existing persona-review screen already reads — renaming it would be a second, UI-side
/// change this backend-only task has no business making. Keeping the wire name is the smaller,
/// lower-blast-radius option (the alternative — updating the admin-ui read in the same task — would
/// reach outside this task's owned files for zero behavioural gain); a future kind-aware DTO
/// (once a theme detail view actually exists) is exactly the additive, kind-routed follow-up F103.3
/// anticipates, not a rename of this one.
/// </para>
///
/// <see cref="Kind"/>/<see cref="Audience"/>/<see cref="BestFor"/>/<see cref="Author"/>/
/// <see cref="Description"/>/<see cref="SamplePatter"/> are read straight off the hash-verified
/// <see cref="CatalogEntryContent"/> this whole response is built from (<see cref="Kind"/>/
/// <see cref="Audience"/>/<see cref="BestFor"/> straight off it; <see cref="Author"/>/
/// <see cref="Description"/>/<see cref="SamplePatter"/> parsed out of <see cref="Meta"/> via
/// <see cref="Catalog.CatalogEntryMetaJson"/>). See <see cref="CatalogController"/>'s own remarks
/// for the shared <see cref="Unreachable"/>-flag shape this reuses from <see cref="CatalogIndexResponse"/>.
/// </summary>
/// <param name="Card">The entry's manifest — a persona card today — as raw JSON text, or <see langword="null"/> when <see cref="Unreachable"/>.</param>
/// <param name="Meta">The shelf metadata's raw JSON text, or <see langword="null"/> when <see cref="Unreachable"/>.</param>
/// <param name="FetchedAt">When THIS content was originally fetched (SPEC F90.4's stale-serve stamp); <see langword="null"/> when <see cref="Unreachable"/>.</param>
/// <param name="Unreachable">
/// <see langword="true"/> when the catalog itself is currently unreachable (no usable index to
/// resolve this slug against) — distinct from a genuinely unknown slug, which 404s instead.
/// </param>
/// <param name="Kind">
/// <c>"persona"</c>, <c>"theme"</c>, or <c>"font"</c> (SPEC F103.1, F103.3, widened by F104.1), or
/// <see langword="null"/> when <see cref="Unreachable"/>. A font entry's <see cref="Card"/>/
/// <see cref="Meta"/> are its raw <c>.font.json</c>/<c>.meta.json</c> text — the SAME generic,
/// kind-agnostic fetch every other kind already gets (S1 review finding, T193): this endpoint
/// never builds a font-specific projection (no asset list on the wire yet), it only had to stop
/// 500ing on <see cref="Kind"/> itself.
/// </param>
/// <param name="Audience"><c>"everyone"</c> or <c>"mature"</c> (same lowercase wire vocabulary as <see cref="CatalogShelfEntryDto.Audience"/>), or <see langword="null"/> when <see cref="Unreachable"/>.</param>
/// <param name="BestFor">Optional genre chips (F90.4a), or <see langword="null"/> when <see cref="Unreachable"/> — empty, never null, once reachable.</param>
/// <param name="Author">The entry's credited author (F90.4a), or <see langword="null"/> when unreachable or absent from meta.json.</param>
/// <param name="Description">The entry's shelf description (F90.4a), or <see langword="null"/> when unreachable or absent from meta.json.</param>
/// <param name="SamplePatter">Sample patter lines (F90.4a), or <see langword="null"/> when <see cref="Unreachable"/> — empty, never null, once reachable.</param>
public sealed record CatalogEntryResponse(
    string? Card,
    string? Meta,
    DateTimeOffset? FetchedAt,
    bool Unreachable,
    string? Kind,
    string? Audience,
    IReadOnlyList<string>? BestFor,
    string? Author,
    string? Description,
    IReadOnlyList<string>? SamplePatter);
