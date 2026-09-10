namespace GenWave.Core.Domain;

/// <summary>
/// One row of <c>station.sponsor</c> (SPEC F171.1; STORY-406; PLAN T432) —
/// <c>Abstractions.ISponsorStore</c>'s own element type. <see cref="Version"/> is the Postgres
/// <c>xmin</c> system column serialized as a string (the <see cref="AdSpot.Version"/> precedent) —
/// <c>ISponsorStore.UpdateAsync</c> takes the previous read's own <see cref="Version"/> back as
/// <c>expectedVersion</c>.
/// </summary>
/// <param name="Id">The row's own surrogate key.</param>
/// <param name="Name">The sponsor's display name — unique per <see cref="PackSlug"/> namespace, folded
/// (<c>station.sponsor_fold</c>) for the comparison, never the raw text (db/46's own
/// <c>sponsor_pack_slug_name_key</c>).</param>
/// <param name="PackSlug">The installing pack's slug, or <see langword="null"/> for an owner-created
/// sponsor — the same owner/pack namespace split <see cref="AdBrief.PackSlug"/> already draws.</param>
/// <param name="Tagline">A short pitch line, or <see langword="null"/>.</param>
/// <param name="About">A longer description, or <see langword="null"/>.</param>
/// <param name="Phone">A contact phone number, or <see langword="null"/>.</param>
/// <param name="Address">A physical address, or <see langword="null"/>.</param>
/// <param name="Website">A <c>http(s)://</c> URL, or <see langword="null"/>.</param>
/// <param name="Tone">A house tone hint the writer falls back to when a brief carries none, or
/// <see langword="null"/>.</param>
/// <param name="Paused">Whether this sponsor's spots are currently withheld from airing —
/// <c>Abstractions.IAdSpotStore.ListAiringExclusionsAsync</c> is what actually enforces the
/// withholding; this flag is only the operator's own lever.</param>
/// <param name="PausedAt">When <see cref="Paused"/> was last set <see langword="true"/> — cleared back
/// to <see langword="null"/> the moment it is set <see langword="false"/> again.</param>
/// <param name="CreatedAt">When this sponsor was first created.</param>
/// <param name="UpdatedAt">When this row was last written.</param>
/// <param name="Version">The row's <c>xmin</c>, as a string — the optimistic-concurrency token
/// <c>ISponsorStore.UpdateAsync</c> takes back as <c>expectedVersion</c>.</param>
public sealed record Sponsor(
    long Id,
    string Name,
    string? PackSlug,
    string? Tagline,
    string? About,
    string? Phone,
    string? Address,
    string? Website,
    string? Tone,
    bool Paused,
    DateTime? PausedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string Version);
