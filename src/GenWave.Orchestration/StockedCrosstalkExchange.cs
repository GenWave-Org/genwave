namespace GenWave.Orchestration;

using GenWave.Core.Domain;

/// <summary>
/// SPEC F127.6, F127.7 (STORY-328, PLAN T285) — one ready-to-air exchange sitting in
/// <see cref="CrosstalkPlanner"/>'s in-memory stock: a cast pair paired with the single mixed asset
/// <c>GenWave.Tts.CrosstalkAssembler.AssembleAsync</c> already produced for it (a LATER task's own
/// writer→assembler composition, PLAN T286 — this type is only ever CONSTRUCTED by that caller and
/// handed to <see cref="CrosstalkPlanner.Stock"/>). <see cref="AssetPath"/>/<see cref="Loudness"/>/
/// <see cref="Cue"/>/<see cref="DurationMs"/> mirror
/// <c>GenWave.Tts.CrosstalkAssemblyResult.Assembled</c>'s own four members exactly — the shape vend
/// needs to compose a played segment, carried forward unchanged rather than re-derived.
///
/// <para>
/// No schema (SPEC F127.7's own "the stock survives nothing" ruling) — this record is never
/// persisted; a process restart simply loses every instance <see cref="CrosstalkPlanner"/> was
/// holding, and the stock regenerates from nothing.
/// </para>
/// </summary>
/// <param name="ShowSlug">The enabled show this exchange was stocked for, keyed by SLUG
/// (<c>station.show.slug</c>) — <see cref="CrosstalkPlanner"/>'s own per-show stock key (SPEC
/// F127.7's "≤2 ready exchanges per enabled show"). SLUG, not display name (PLAN T285 review F4):
/// the mutable, non-unique name would let an operator's rename silently orphan a show's stock.</param>
/// <param name="Cast">The persona-id pair this exchange was generated against — compared at vend
/// time to the CURRENT grid adjacency (SPEC F127.7's staleness check); a mismatch discards this
/// exchange rather than airing a cast pair the schedule no longer names.</param>
/// <param name="AssetPath">Absolute path to the single mixed audio asset — deleted whenever this
/// exchange is discarded (staleness) or retired (aired).</param>
/// <param name="Script">
/// SPEC F127.11 (STORY-329, PLAN T287) — the full validated script this exchange's asset was mixed
/// from, carried straight off <c>GenWave.Tts.CrosstalkScriptWriter</c>'s own accepted output (round-2
/// review F8: that writer already produces the published <see cref="CrosstalkAiredScript"/> shape
/// directly — no GenWave.Tts-local script type and no mapper stands between the two, so
/// <c>GenWave.Host.Crosstalk.CrosstalkStockWorker</c> simply carries it forward, unchanged, when it
/// builds this record — this project still never references <c>GenWave.Tts</c> itself, only the
/// shared Abstractions shape). Carried forward, unchanged again, onto the composed
/// <c>MediaItem.CrosstalkScript</c> at vend time, so the booth log's own <c>pick</c> jsonb stamp
/// answers "what did they say" (F127.11) with no re-derivation. Defaults to <see langword="null"/> so
/// every pre-T287 construction site (every T285 spec in this project's own test suite) stays diff-free.
/// </param>
public sealed record StockedCrosstalkExchange(
    string ShowSlug,
    CrosstalkCast Cast,
    string AssetPath,
    Loudness Loudness,
    CuePoints? Cue,
    int? DurationMs,
    CrosstalkAiredScript? Script = null);
