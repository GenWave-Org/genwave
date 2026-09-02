namespace GenWave.Ads;

/// <summary>
/// One entry of an <c>ad_spot.voice_plan</c> jsonb array (SPEC F161.2, STORY-391, PLAN T401 design
/// note) — the shape THIS project (the sole owner of the opaque <c>AdSpot.VoicePlan</c> string; see
/// that property's own remarks: "a caller downstream of this Core seam reconstitutes the shape it
/// expects at its own edge") reads and writes. <c>[{"tag":"ANNOUNCER","voiceId":"...","pace":1.0}, …]</c>
/// — <see cref="Pace"/> defaults to 1.0 ("engine default", <c>VoiceSpec.Pace</c>'s own sentinel) so a
/// plan authored before pace tuning existed still round-trips. <see cref="AdRenderService"/> is the
/// only reader today; T403's owner editor is the shape's first intended writer.
/// </summary>
/// <param name="Tag">The voice tag this entry casts (e.g. <c>ANNOUNCER</c>, <c>VOICE1</c>).</param>
/// <param name="VoiceId">The TTS engine voice identifier to render this tag with.</param>
/// <param name="Pace">Speaking-rate multiplier, clamped at render time by <c>TtsPace.Clamp</c> exactly
/// like every other voice cast (persona or bare).</param>
internal sealed record AdVoicePlanEntry(string Tag, string VoiceId, double Pace = 1.0);
