namespace GenWave.Orchestration;

using GenWave.Core.Domain;

/// <summary>
/// SPEC F117.2 (STORY-309, PLAN T250), F175.2 (STORY-430, PLAN T449) — the templated show-line arm of
/// the <see cref="Orchestrator"/>'s <c>StationId</c> drain (see that method's own remarks: "the SAME
/// <see cref="SegmentKind.StationId"/> request, <see cref="SegmentRequest.ShowName"/> additionally
/// stamped"). Hoisted into its own public, static, pure function so a PLAN T449 Host.Tests fact
/// can call the EXACT SAME code the Orchestrator calls at render time and assert on its output,
/// instead of re-typing the `with` expression a second time somewhere a drift between the two
/// could go unnoticed.
///
/// <para>
/// Takes <see cref="ShowSummary"/>, never <c>GenWave.Core.Domain.Show</c> (the full CRUD entity) — the
/// Orchestrator's own <c>StationId</c> drain arm never reads anything beyond the cached
/// <see cref="GenWave.Abstractions.Playout.OnAirSnapshot.Show"/> snapshot
/// <c>CachingScheduleResolver.TryGetCurrent()</c> already hands it (SPEC F117.2's own "no extra store
/// round trip" rule), so this function's own parameter type stays exactly that shape rather than
/// widening to one the render path itself never sees.
/// </para>
///
/// <para>
/// <b>F175.2's own pin: the show's sponsor is invisible here, by TYPE, not merely by omission.</b>
/// <see cref="ShowSummary"/> carries no <c>SponsorId</c>/sponsor member at all (SPEC F115.2's
/// "dormant columns unread" law — see that record's own remarks) and this function reads only
/// <see cref="ShowSummary.Name"/>, so two <c>Show</c> rows differing ONLY in their linked sponsor
/// project to the identical <see cref="ShowSummary"/> shape and this function returns byte-identical
/// requests for both — there is no sponsor-shaped input this code could branch on even if it wanted
/// to, whether or not a caller ever remembers to test that (PLAN T449 ruling).
/// </para>
/// </summary>
public static class ShowIdentRequest
{
    /// <summary>
    /// Layers <paramref name="show"/>'s name onto <paramref name="baseRequest"/> for the templated
    /// show-line floor, or returns <paramref name="baseRequest"/> unchanged when no show is on the air.
    /// <see cref="ShowSummary"/> carries no sponsor member, so a sponsored show and an unsponsored one
    /// produce the same request (SPEC F175.2).
    /// </summary>
    public static SegmentRequest For(SegmentRequest baseRequest, ShowSummary? show) =>
        show is null ? baseRequest : baseRequest with { ShowName = show.Name };
}
