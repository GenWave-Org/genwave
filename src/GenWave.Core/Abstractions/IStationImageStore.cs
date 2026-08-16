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

    /// <summary>
    /// Upserts the single row (SPEC F131): no row yet inserts one at <c>id = 1</c>, an existing row
    /// replaces every column — including <paramref name="token"/>, which the CALLER has already rotated
    /// before this method ever runs (mirrors <see cref="IPersonaAvatarStore.UpsertAsync"/>'s own
    /// "the store is dumb about rotation policy" discipline). <c>updated_at</c> is always the write's
    /// own <c>now()</c>.
    /// </summary>
    Task UpsertAsync(byte[] bytes, string sha256, string token, CancellationToken ct);

    /// <summary>Removes the station image row, if any — reverting the F88 fallback to the shipped logo.
    /// Returns <see langword="true"/> when a row was deleted, <see langword="false"/> when none
    /// existed.</summary>
    Task<bool> DeleteAsync(CancellationToken ct);
}
