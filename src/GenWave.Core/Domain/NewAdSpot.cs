namespace GenWave.Core.Domain;

/// <summary>
/// The fields <c>Abstractions.IAdSpotStore.CreateAsync</c> needs to land a new
/// <c>station.ad_spot</c> row (SPEC F159.1, F159.2; STORY-389; PLAN T398). <see cref="InitialState"/>
/// is restricted to <see cref="AdState.Draft"/>, <see cref="AdState.Approved"/>, or
/// <see cref="AdState.Failed"/> — the only states a spot can be BORN into (SPEC F159.2's own
/// transition graph is between EXISTING rows; <see cref="AdState.Rendering"/>/<see cref="AdState.Ready"/>/
/// <see cref="AdState.Retired"/> are reachable only via a transition on the store, never at
/// creation) — <c>IAdSpotStore.CreateAsync</c> rejects any other value.
///
/// <para>
/// <b>Why <see cref="AdState.Approved"/> and <see cref="AdState.Failed"/> are both legal creation
/// states, not just <see cref="AdState.Draft"/>.</b> <c>Station:Ads:AutoApprove</c> (SPEC F159.4)
/// lands a generated spot straight in <see cref="AdState.Approved"/> — PLAN T400's own writer picks
/// the initial state from that flag rather than always creating in <see cref="AdState.Draft"/> and
/// immediately approving in a second round trip. <see cref="AdState.Failed"/> covers STORY-390 AC3's
/// own outcome: a script that never passed the validator after its one re-ask is still recorded, with
/// <see cref="FailReason"/> naming the violated rule, rather than silently producing nothing — the
/// same "visible, never silent" posture every lifecycle store in this codebase keeps.
/// </para>
/// </summary>
/// <param name="Brand">The fictional (or owner's real) brand this spot advertises.</param>
/// <param name="Title">A short operator-facing label — never read aloud.</param>
/// <param name="Brief">The premise/tone/structure hint this spot's script was written from, or
/// <see langword="null"/>.</param>
/// <param name="Script">The spot's own line-by-line copy, or <see langword="null"/> before generation
/// completes.</param>
/// <param name="Source">Where this spot's copy came from (SPEC F159.1).</param>
/// <param name="PackSlug">Set only for a <see cref="AdSource.Pack"/> spot — <see langword="null"/>
/// for every other source.</param>
/// <param name="SpotSeconds">One of the three shipped structures — 15, 30, or 60 (SPEC F160.2); the
/// DDL's own <c>CHECK</c> is the backstop if an invalid value ever reaches the store.</param>
/// <param name="VoicePlan">The voice cast plan, as raw <c>jsonb</c> text, or <see langword="null"/>
/// before it is known.</param>
/// <param name="BedMediaId">An optional background bed track's <c>library.media</c> id.</param>
/// <param name="InitialState">The state this spot is born into — <see cref="AdState.Draft"/>,
/// <see cref="AdState.Approved"/>, or <see cref="AdState.Failed"/> only.</param>
/// <param name="FailReason">Required, and only legal, when <paramref name="InitialState"/> is
/// <see cref="AdState.Failed"/> — the violated validator rule's own id (STORY-390 AC3).</param>
public sealed record NewAdSpot(
    string Brand,
    string Title,
    string? Brief,
    string? Script,
    AdSource Source,
    string? PackSlug,
    int SpotSeconds,
    string? VoicePlan,
    long? BedMediaId,
    AdState InitialState,
    string? FailReason);
