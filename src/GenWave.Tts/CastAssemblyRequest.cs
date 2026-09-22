namespace GenWave.Tts;

using GenWave.Core.Domain;

/// <summary>
/// Everything <see cref="CrosstalkAssembler.AssembleCastAsync"/> needs to render and mix a 1-3 voice
/// cast into one measured, tag-embedded artifact (SPEC F161.2, F161.3; STORY-391; PLAN T401) — the
/// widened sibling of <see cref="CrosstalkAssemblyRequest"/>'s own two-persona-card shape: a cast
/// here is <see cref="VoiceSpec"/>s, not <see cref="PersonaCard"/>s (ad voices are actors, not the
/// station's DJs — persona cards are never required), the ceiling is an explicit per-request value
/// (never the global <see cref="CrosstalkOptions.DurationTargetSeconds"/> knob this project's OTHER
/// assembly request reads), and an optional <see cref="Bed"/> mixes under the finished cast exactly
/// like <see cref="GenWave.Core.Abstractions.IAudioMixer"/>'s existing <see cref="AudioMixRequest"/>
/// bed path already does for a single voice (the <c>SafeSegmentAuthor</c> precedent — reused here,
/// never re-implemented).
/// </summary>
/// <param name="Lines">Every line to render, in script order, each tagged by which cast member
/// speaks it. At least one line is required (SPEC F161.2 relaxes crosstalk's own 2-line floor — a
/// single-voice, single-line spot is legal).</param>
/// <param name="Cast">1-3 DISTINCT-tagged voices (SPEC F161.2 review F8: <see cref="CastMember"/>, a
/// named record — not a raw tuple, consistent with <see cref="CastLine"/>'s own shape). Every
/// <see cref="Lines"/> tag must name one of these — an untagged line is a caller-contract violation,
/// not a render failure.</param>
/// <param name="CeilingSeconds">
/// The FINAL artifact's real, measured duration may not exceed this (SPEC F161.2) — over-ceiling
/// discards the whole attempt, never trims. Always caller-supplied (e.g. <c>spot_seconds × (1 +
/// Ads:DurationToleranceRatio)</c>); this request never reads <see cref="CrosstalkOptions"/> for it.
/// </param>
/// <param name="Tags">Brand tags embedded into the final artifact's file metadata (F161.3, "a spot
/// that escapes the box still says who made it") — always applied, even with no <see cref="Bed"/>
/// (the <c>SafeSegmentAuthor</c> "voice-only still goes through the mixer once" posture, so
/// tag-embedding stays in exactly one place).</param>
/// <param name="OutputDirectory">Where the final artifact (and its own transient pre-bed mix) is
/// written — the caller's own authored-root subdirectory. This class never resolves one itself
/// (GenWave.Tts carries no opinion on where an "ads" authored subtree lives).</param>
/// <param name="Bed">An optional bed to duck the cast under, or <see langword="null"/> for
/// cast-only.</param>
/// <param name="BedDuckDb">Bed attenuation in dB relative to the cast. Ignored when
/// <see cref="Bed"/> is <see langword="null"/>.</param>
/// <param name="BedPadSeconds">Lead-in/tail-out padding around the cast. Ignored when
/// <see cref="Bed"/> is <see langword="null"/>.</param>
/// <param name="BedFadeSeconds">SPEC F168.4; STORY-403; PLAN T416 — how long, in seconds, the bed
/// fades to silence across the render's trailing tail (<c>Station:Ads:BedFadeMs</c>, converted to
/// seconds by the one caller that varies it — <see cref="GenWave.Ads.AdRenderService"/>, plain text
/// not a <c>cref</c>: GenWave.Tts must never reference GenWave.Ads). Zero (the default) means no
/// fade — every existing caller of this record, including this project's own tests, keeps compiling
/// and behaving unchanged. Ignored when <see cref="Bed"/> is <see langword="null"/>.</param>
/// <param name="TargetLufs">SPEC F196.1; STORY-463; PLAN T542 — the station's own loudness target
/// (<c>Loudness:TargetLufs</c>), forwarded into <see cref="AudioMixRequest.TargetLufs"/> unchanged
/// (<see cref="CrosstalkAssembler.AssembleCastAsync"/> is the one call site). Defaulted to −16.0 —
/// <see cref="AudioMixRequest.TargetLufs"/>'s own default — so every existing caller of this record
/// keeps compiling and behaving unchanged. Ignored when <see cref="Bed"/> is
/// <see langword="null"/>.</param>
public sealed record CastAssemblyRequest(
    IReadOnlyList<CastLine> Lines,
    IReadOnlyList<CastMember> Cast,
    double CeilingSeconds,
    AudioTags Tags,
    string OutputDirectory,
    BedSpec? Bed = null,
    double BedDuckDb = 0.0,
    double BedPadSeconds = 0.0,
    double BedFadeSeconds = 0.0,
    double TargetLufs = -16.0);
