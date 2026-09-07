namespace GenWave.Ads;

/// <summary>
/// The two <c>Station:Ads:*</c> Live knobs <see cref="AdCastPicker.Pick"/> needs on every render claim
/// (SPEC F167.1, F167.2; STORY-402; PLAN T415) — <see cref="AdLiveSettingsReader.Read"/>'s own return
/// shape, the <see cref="AdStockSettings"/> precedent applied to the cast-pool pair beside it.
/// <c>Station:Ads:BedFadeMs</c> (a THIRD knob in the same namespace) is not carried here — it belongs
/// to a later task's own bed-mix concern, not this one's cast pick.
/// </summary>
/// <param name="AnnouncerVoice">SPEC F167.1's <c>Station:Ads:AnnouncerVoice</c> — a Kokoro voice id, or
/// <c>""</c> (the default) meaning "use the station's own voice"
/// (<see cref="Core.Abstractions.IStationIdentityProvider.Current"/>).</param>
/// <param name="CastVoices">SPEC F167.1's <c>Station:Ads:CastVoices</c> — the comma-separated pool
/// <see cref="AdCastPicker"/> casts <c>VOICE1</c>/<c>VOICE2</c> from, already split/trimmed/de-duped by
/// <see cref="AdLiveSettingsReader"/>, order preserved. Empty means the pool is genuinely empty
/// (SPEC F167.4).</param>
internal sealed record AdLiveSettings(string AnnouncerVoice, IReadOnlyList<string> CastVoices);
