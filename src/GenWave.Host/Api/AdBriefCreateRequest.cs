namespace GenWave.Host.Api;

/// <summary>
/// <c>POST /api/ad-briefs</c>'s own request body (SPEC F171.6; STORY-411; PLAN T435) —
/// always creates an OWNER brief; there is no <c>packSlug</c> field here at all (pack briefs land only
/// through <c>IAdBriefStore.UpsertAllAsync</c>'s own pack-install path, never through this admin-facing
/// POST — see <see cref="AdBriefsController.Create"/>'s own remarks). <see cref="Enabled"/> defaults
/// to <see langword="true"/> when omitted — the add form's own "new briefs are live by default"
/// posture, an owner opts a brief OUT rather than remembering to opt one in.
/// </summary>
/// <param name="SponsorId">The sponsor this brief is about — required.</param>
/// <param name="Premise">The brief's premise hint, or <see langword="null"/>.</param>
/// <param name="Tone">The brief's tone hint, or <see langword="null"/>.</param>
/// <param name="Structure">The brief's structure hint, or <see langword="null"/>.</param>
/// <param name="Enabled">Whether the writer may sample this brief immediately — <see langword="null"/>
/// (omitted) defaults to <see langword="true"/>.</param>
public sealed record AdBriefCreateRequest(
    long? SponsorId,
    string? Premise,
    string? Tone,
    string? Structure,
    bool? Enabled);
