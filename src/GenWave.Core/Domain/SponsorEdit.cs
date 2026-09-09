namespace GenWave.Core.Domain;

/// <summary>
/// The sparse fields <c>Abstractions.ISponsorStore.UpdateAsync</c> may change (SPEC F171.1;
/// STORY-406; PLAN T432) — <see langword="null"/> means "leave this column unchanged" (the
/// <see cref="AdSpotEdit"/> sparse-PATCH precedent, one seam over). A blank string clears the column
/// back to <see langword="null"/> for the optional facts, the same convention
/// <c>ShowRepository</c>'s own <c>NullIfBlank</c> already applies to <c>Show.Tagline</c>/<c>Flavor</c>.
/// <see cref="Name"/> is sparse the same way, but a pack-owned sponsor
/// (<see cref="Sponsor.PackSlug"/> non-null) refuses ANY attempt to change it —
/// <see cref="SponsorWriteResult.NamePackOwned"/>, enforced by the store, not this record.
/// </summary>
public sealed record SponsorEdit(
    string? Name,
    string? Tagline,
    string? About,
    string? Phone,
    string? Address,
    string? Website,
    string? Tone);
