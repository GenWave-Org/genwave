namespace GenWave.Host.Api;

/// <summary>
/// <c>POST /api/ad-briefs</c>'s own request body (SPEC F162.1, F162.2; STORY-392; PLAN T403b) —
/// always creates an OWNER brief; there is no <c>packSlug</c> field here at all (pack briefs land only
/// through <c>IAdBriefStore.UpsertAsync</c>'s own pack-install path, never through this admin-facing
/// POST — see <see cref="AdBriefsController.Create"/>'s own remarks). <see cref="Enabled"/> defaults
/// to <see langword="true"/> when omitted — the add form's own "new briefs are live by default"
/// posture, an owner opts a brief OUT rather than remembering to opt one in.
/// </summary>
/// <param name="Brand">The brand this brief is about — required.</param>
/// <param name="Premise">The brand's premise hint, or <see langword="null"/>.</param>
/// <param name="Tone">The brand's tone hint, or <see langword="null"/>.</param>
/// <param name="Structure">The brand's structure hint, or <see langword="null"/>.</param>
/// <param name="Enabled">Whether the writer may sample this brief immediately — <see langword="null"/>
/// (omitted) defaults to <see langword="true"/>.</param>
public sealed record AdBriefCreateRequest(
    string? Brand,
    string? Premise,
    string? Tone,
    string? Structure,
    bool? Enabled);
