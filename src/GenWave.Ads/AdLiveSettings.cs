namespace GenWave.Ads;

/// <summary>
/// The <c>Station:Ads:*</c> Live knobs read once per render claim (SPEC F167.1, F167.2, F168.4;
/// STORY-402, STORY-403; PLAN T415, T416) — <see cref="AdLiveSettingsReader.Read"/>'s own return
/// shape, the <see cref="AdStockSettings"/> precedent applied to this cast-pool/bed-fade trio.
/// </summary>
/// <param name="AnnouncerVoice">SPEC F167.1's <c>Station:Ads:AnnouncerVoice</c> — a Kokoro voice id, or
/// <c>""</c> (the default) meaning "use the station's own voice"
/// (<see cref="Core.Abstractions.IStationIdentityProvider.Current"/>).</param>
/// <param name="CastVoices">SPEC F167.1's <c>Station:Ads:CastVoices</c> — the comma-separated pool
/// <see cref="AdCastPicker"/> casts <c>VOICE1</c>/<c>VOICE2</c> from, already split/trimmed/de-duped by
/// <see cref="AdLiveSettingsReader"/>, order preserved. Empty means the pool is genuinely empty
/// (SPEC F167.4).</param>
/// <param name="BedFadeMs">SPEC F168.4's <c>Station:Ads:BedFadeMs</c> — how long, in milliseconds, the
/// background music fades to silence across the render's trailing tail. No default (PLAN T416 review
/// F3+O3): <see cref="AdLiveSettingsReader.Read"/> is the ONE construction site this record has —
/// every other caller builds it explicitly (that reader's own <c>DefaultBedFadeMs</c> constant is
/// where the 300ms default actually lives, and the only place clamping to
/// <c>MinBedFadeMs</c>/<c>MaxBedFadeMs</c> happens); a second default here would protect zero real
/// callers and hide a value already owned elsewhere.</param>
/// <param name="TargetLufs">SPEC F196.1; STORY-463; PLAN T542 — <c>Loudness:TargetLufs</c>, the
/// station's own loudness target, read once per render claim exactly like <paramref name="BedDuckDb"/>
/// beside it and forwarded into <see cref="GenWave.Tts.CastAssemblyRequest.TargetLufs"/>. It is the
/// bed's reference level only when the voice is unmeasurable (the voice's own LUFS wins otherwise,
/// gh-#746; <c>FfmpegAudioMixer.ResolveBedGainDb</c>), so it is one more render input
/// <see cref="AdPreviewKey.Compute"/> hashes.</param>
/// <remarks>
/// Public, not internal (PLAN T442 ruling): <see cref="AdPreviewKey.Compute"/> is a genuine
/// cross-assembly seam — <c>GenWave.Host</c>'s <c>AdsController</c> calls it on every read to detect a
/// stale preview — and a public method cannot expose a less-accessible parameter type (CS0051). The
/// <see cref="AdDeterministicSeed"/> precedent this class otherwise follows stays <c>internal</c>
/// because it has no caller outside this assembly; this record does now.
/// </remarks>
public sealed record AdLiveSettings(
    string AnnouncerVoice, IReadOnlyList<string> CastVoices, int BedFadeMs, double BedDuckDb, double TargetLufs);
