using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SEAM (SPEC F131, STORY-339, PLAN T290, gh-#15) — persistence for the owner-customized station image
/// in <c>station.station_image</c>, a deliberate single-row table (<c>id int primary key default 1
/// check (id = 1)</c>). Ships dark: no consumer lands with this seam yet —
/// <c>StationImageController</c>'s <c>PUT</c>/<c>DELETE</c> (T307, sharing T291's image-normalize
/// pipeline) is the first write consumer; the F88 artwork fallback (row-else-shipped-logo) and the
/// spectator logo/favicon route are the first read consumers.
/// </summary>
public interface IStationImageStore
{
    /// <summary>The current station image, or <see langword="null"/> if the owner has never
    /// customized it (the row does not exist yet — a fresh install ships with none, the "shipped logo"
    /// fallback is the caller's own concern, not this store's).</summary>
    Task<StationImage?> GetAsync(CancellationToken ct);

    /// <summary>Token-only projection of the current station image (PLAN T307 fix round) — the same
    /// answer as <see cref="GetAsync"/>'s own <see cref="StationImage.Token"/>, but never selects the
    /// ≤768 KiB <c>bytes</c> column. For a caller that only ever needs to know WHETHER a row exists
    /// and, if so, under what token — <c>GenWave.Host.Api.AuthController.Stations</c>'s own
    /// <c>GET /api/stations</c> snapshot is the first (the authed shell's own tab-icon href, resolved
    /// from that SAME snapshot rather than a per-navigation bytes fetch) — <see cref="GetAsync"/> would
    /// otherwise pull the whole image payload off Postgres purely to discard it. <see langword="null"/>
    /// for the same "no customization" reason <see cref="GetAsync"/> documents.</summary>
    Task<string?> GetTokenAsync(CancellationToken ct);

    /// <summary>
    /// Upserts the single row (SPEC F131) from <paramref name="image"/>: no row yet inserts one at
    /// <c>id = 1</c>, an existing row replaces every column — including
    /// <see cref="StationImageInput.Token"/>, which the CALLER has already rotated before this method
    /// ever runs (mirrors <see cref="IPersonaAvatarStore.UpsertAsync"/>'s own "the store is dumb about
    /// rotation policy" discipline). <c>byte_size</c> is <paramref name="image"/>'s own derived
    /// <see cref="StationImageInput.ByteSize"/>, never a separately-trusted value (PLAN T307 rider —
    /// the same <see cref="PersonaAvatarInput"/> discipline, applied here). <c>updated_at</c> is always
    /// the write's own <c>now()</c>.
    /// </summary>
    Task UpsertAsync(StationImageInput image, CancellationToken ct);

    /// <summary>Removes the station image row, if any — reverting the F88 fallback to the shipped logo.
    /// Returns <see langword="true"/> when a row was deleted, <see langword="false"/> when none
    /// existed.</summary>
    Task<bool> DeleteAsync(CancellationToken ct);
}
