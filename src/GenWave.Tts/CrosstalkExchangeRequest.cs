namespace GenWave.Tts;

using GenWave.Core.Domain;

/// <summary>
/// Everything <see cref="CrosstalkScriptWriter"/> needs to request one exchange (SPEC F127.3,
/// STORY-326 AC1, AC7) — <see cref="HostCard"/>/<see cref="NeighborCard"/> already CAST by the
/// caller (a LATER task's <c>CrosstalkPlanner</c>, SPEC F127.2 — this writer never resolves who is
/// on either side of the booth), plus the show/daypart/time-of-day hooks the prompt is allowed to
/// carry. <see cref="ShowName"/>/<see cref="Daypart"/> are both optional — an unnamed block or a
/// showless station omits the corresponding prompt line entirely, mirroring
/// <see cref="LlmPromptBuilder.BuildShowLine"/>'s own "invent nothing beyond what's given" discipline
/// one seam over.
///
/// <para>
/// <b>Structurally carries no current track (SPEC F127.3, STORY-326 AC7).</b> There is no
/// <see cref="MediaItem"/>-typed member here, by construction, the same proof
/// <c>Story228_RequestShoutOut</c>'s own reflection fact pins for <c>SegmentRequest</c> one project
/// over: exchanges are generated ahead of air and cannot know what is actually playing when they
/// eventually vend, so the type this writer's prompt is built from has nothing for a future edit to
/// accidentally interpolate a track into.
/// </para>
/// </summary>
public sealed record CrosstalkExchangeRequest(
    PersonaCard HostCard,
    PersonaCard NeighborCard,
    string StationName,
    string? ShowName,
    string? Daypart,
    DateTimeOffset StationLocalNow);
