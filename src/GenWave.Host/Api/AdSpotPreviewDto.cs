namespace GenWave.Host.Api;

/// <summary>
/// The wire shape of an ad spot's rendered preview stamp (SPEC F174.4; STORY-424 AC4; PLAN T442) —
/// <see cref="AdSpotDto.Preview"/> is <see langword="null"/> exactly when
/// <c>GenWave.Core.Domain.AdSpot.PreviewPath</c> is <see langword="null"/> (no preview has ever been
/// rendered for this row); otherwise this carries the last render's own timestamp/key plus the live
/// staleness verdict <see cref="AdsController.ToPreviewDto"/> recomputes on every read.
/// </summary>
/// <param name="At">When the preview currently on disk was rendered (<c>preview_at</c>).</param>
/// <param name="Key">The staleness key stamped alongside the render (<c>preview_key</c>) — a lowercase
/// SHA256 hex digest over every input that render read.</param>
/// <param name="Stale">Whether <see cref="Key"/> still matches a fresh recomputation over the row's
/// CURRENT inputs. <see langword="true"/> means the script/cast/bed or a Live Ads setting changed since
/// this preview rendered — re-render before trusting what <c>GET /api/ads/{id}/preview.wav</c> streams.</param>
public sealed record AdSpotPreviewDto(DateTime At, string Key, bool Stale);
