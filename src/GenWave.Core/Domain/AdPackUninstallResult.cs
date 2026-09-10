namespace GenWave.Core.Domain;

/// <summary>
/// Discriminated union expressing every outcome of
/// <c>Abstractions.IAdBriefStore.UninstallPackAsync</c> (SPEC F172.4; STORY-416; PLAN T437) — the
/// SAME "one round trip, one discriminated result" posture <see cref="SponsorDeleteResult"/> and
/// <see cref="VoicePackDeleteResult"/> already carry one seam over.
///
/// <para>
/// <b>Placed beside <see cref="SponsorDeleteResult"/>/<see cref="VoicePackDeleteResult"/> in
/// <c>Domain/</c>, not <c>Abstractions/</c>.</b> Both of those sibling shapes — this type's own
/// direct precedent — live in <c>GenWave.Core.Domain</c>, never <c>GenWave.Core.Abstractions</c>
/// (which holds interfaces such as <see cref="Abstractions.IAdBriefStore"/> itself, not the result
/// records those interfaces return); this type follows the REAL convention those two files already
/// establish rather than a literal reading that would put it beside the interfaces instead.
/// </para>
/// </summary>
public abstract record AdPackUninstallResult
{
    private AdPackUninstallResult() { }

    /// <summary>Every <c>station.ad_brief</c> row for this slug is gone, and every <c>source = 'pack'</c>
    /// spot for this slug that was not already <c>retired</c> (nor currently <c>rendering</c> — that
    /// state stays undiscardable, <c>Station.AdSpotRepository.RetireAsync</c>'s own invariant, PLAN
    /// T437 review round 2 finding 2) now is — SPEC F172.4's own words: "Pack-sourced spots
    /// (<c>source='pack'</c>) of the pack are retired by the uninstall as today."
    /// <see cref="Sponsors"/> counts only the sponsor rows that ALSO deleted — a sponsor still named by
    /// one of this call's own just-retired spots cannot (<c>station.ad_spot.sponsor_id</c> is
    /// <c>NOT NULL REFERENCES ... ON DELETE RESTRICT</c>, db/46) and is deliberately left in place
    /// rather than attempted and failed; see <c>Station.AdBriefRepository.UninstallPackAsync</c>'s own
    /// remarks for why. STORY-416 AC1's "sponsor count is 0" holds for a pack whose sponsors carry no
    /// spot history; AC3 never claims the sponsor count, only the retirement.</summary>
    /// <param name="Briefs">How many <c>station.ad_brief</c> rows this call deleted.</param>
    /// <param name="Sponsors">How many <c>station.sponsor</c> rows this call deleted — never counts a
    /// row <see cref="KeptSponsors"/> also lists; the two are mutually exclusive by construction (a
    /// pack sponsor either deleted cleanly or survived, never both).</param>
    /// <param name="RetiredSpots">How many <c>source = 'pack'</c> <c>station.ad_spot</c> rows this call
    /// retired.</param>
    /// <param name="KeptSponsors">The pack-owned sponsor rows this call could NOT delete: db/46 makes
    /// <c>station.ad_spot.sponsor_id</c> <c>NOT NULL REFERENCES ... ON DELETE RESTRICT</c>, and SPEC
    /// F171.5's own rule for deleting a sponsor directly — "no brief, spot (any state), or show
    /// references the sponsor" — applies here unexceptioned: a spot THIS SAME call just retired is
    /// still, in that rule's own words, a "spot (any state)", so its sponsor survives too, orphaned
    /// from the now-uninstalled pack rather than attempted and failed against the FK. An OWNER-authored
    /// <c>station.ad_brief</c> row on a pack sponsor is the SAME "brief" half of that rule — the
    /// pack's own <c>pack_slug</c>-scoped brief delete never touches it, so it
    /// keeps that sponsor standing too, for the identical reason. Never "kept as
    /// today" — this is a NEW outcome this round adds, not a restatement of the pre-existing retire
    /// behaviour <see cref="RetiredSpots"/> already covered. Ordered by id, read in the SAME
    /// transaction as the writes above (<c>Station.AdBriefRepository.UninstallPackAsync</c>'s own
    /// remarks). Empty exactly when <c>GenWave.Host.Api.AdPackController.Uninstall</c> answers 204
    /// (STORY-416 AC1/AC3's own bare "uninstalled clean" shape); non-empty is what pushes that same
    /// route to 200 (<c>GenWave.Host.Api.AdPackUninstallResponse</c>), naming the survivors
    /// instead.</param>
    public sealed record Deleted(int Briefs, int Sponsors, int RetiredSpots, IReadOnlyList<Sponsor> KeptSponsors) : AdPackUninstallResult;

    /// <summary>No <c>station.ad_brief</c> or <c>station.sponsor</c> row named this
    /// <c>pack_slug</c> before the call — nothing is "installed" for this slug to uninstall.</summary>
    public sealed record NotFound : AdPackUninstallResult;

    /// <summary>At least one owner-authored <c>station.ad_spot</c> row (any state, including
    /// <c>retired</c>, so long as it would NOT itself be retired by this uninstall) or
    /// <c>station.show</c> row still names one of this pack's sponsors — nothing was written, the
    /// whole attempt rolled back. <see cref="SpotTitles"/>/<see cref="ShowNames"/> each carry up to
    /// the first ten referencing rows, ordered by <c>created_at</c> (SPEC F172.4).</summary>
    public sealed record InUse(IReadOnlyList<string> SpotTitles, IReadOnlyList<string> ShowNames) : AdPackUninstallResult;
}
