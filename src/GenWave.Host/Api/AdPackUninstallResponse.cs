using GenWave.Core.Domain;

namespace GenWave.Host.Api;

/// <summary>
/// Response body for <c>DELETE /api/ad-packs/{slug}</c> ONLY when the uninstall left at least one of
/// the pack's own sponsors standing (SPEC F172.4; STORY-416; PLAN T437) —
/// <see cref="AdPackController.Uninstall"/> answers 204,
/// bare, exactly as before when <see cref="AdPackUninstallResult.Deleted.KeptSponsors"/> comes back
/// empty; this body exists ONLY for the non-empty case, so a caller never has to distinguish "200 with
/// an empty array" from "204" — there is no such state.
///
/// <para>
/// <b>Why a sponsor can survive an uninstall at all.</b> SPEC F171.5's own rule for deleting a sponsor
/// directly — "no brief, spot (any state), or show references the sponsor" — applies here unexceptioned:
/// a <c>source = 'pack'</c> spot this SAME uninstall call just retired (or one still <c>rendering</c>,
/// which the retire step deliberately skips) is still, in that rule's own words, a "spot (any state)".
/// An OWNER-authored <c>station.ad_brief</c> row on the same sponsor is the "brief" half of that SAME
/// rule — the pack's own <c>pack_slug</c>-scoped brief delete never touches an
/// owner brief, so it keeps that sponsor standing too. db/46 makes both
/// <c>station.ad_spot.sponsor_id</c> and <c>station.ad_brief.sponsor_id</c> <c>NOT NULL REFERENCES ...
/// ON DELETE RESTRICT</c>, so that sponsor's row cannot delete — it survives, orphaned from the
/// now-uninstalled pack (its own <c>pack_slug</c> column still names the slug, though nothing installed
/// under that slug remains) — "Pause is the soft option" (SPEC F171.5) is how an operator retires one of
/// these going forward.
/// </para>
///
/// <para>
/// <b>A repeat <c>DELETE</c> of the same, already-uninstalled slug answers 200 again, forever —
/// that is the contract, not a bug.</b> A kept sponsor's own
/// <c>pack_slug</c> column is never cleared, so <see cref="AdPackController.Uninstall"/>'s own
/// pre-check (<c>GenWave.MediaLibrary.Station.AdBriefRepository.UninstallPackAsync</c>'s own remarks)
/// keeps finding this slug "installed" on every subsequent call, and can never answer
/// <see cref="AdPackUninstallResult.NotFound"/> for it again. A second call's own
/// <see cref="RetiredSpots"/> reads 0 (nothing left in <c>source = 'pack'</c> state to retire — the
/// first call already did), and <see cref="KeptSponsors"/> names the identical survivor set again.
/// </para>
///
/// <para>
/// <b>Reinstalling a slug whose sponsor survived an earlier uninstall reuses that same row.</b>
/// <c>POST /api/ad-packs/{slug}/install</c>'s own sponsor-identity write,
/// <c>GenWave.MediaLibrary.Station.SponsorRepository.UpsertPackSponsorsAsync</c>, upserts on
/// <c>station.sponsor</c>'s own <c>sponsor_pack_slug_name_key</c> constraint — a survivor's
/// <c>pack_slug</c>/name pair still holds that constraint, so a reinstall UPDATEs the same row
/// (refreshing only <c>name</c>/<c>updated_at</c>) rather than inserting a second one; any brief/spot
/// history that kept it alive through the earlier uninstall carries straight through, untouched.
/// </para>
/// </summary>
/// <param name="Slug">The route slug just uninstalled.</param>
/// <param name="RetiredSpots">How many <c>source = 'pack'</c> <c>station.ad_spot</c> rows this call
/// retired — <see cref="AdPackUninstallResult.Deleted.RetiredSpots"/>, carried through unchanged.</param>
/// <param name="KeptSponsors">Every pack-owned sponsor this call could NOT delete, in
/// <see cref="AdPackUninstallResult.Deleted.KeptSponsors"/>'s own id order — never empty on this
/// response (an empty list is a 204, not a 200; see this record's own class remarks).</param>
public sealed record AdPackUninstallResponse(string Slug, int RetiredSpots, IReadOnlyList<SponsorRefDto> KeptSponsors);
