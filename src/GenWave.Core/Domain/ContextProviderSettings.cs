namespace GenWave.Core.Domain;

/// <summary>
/// One <see cref="Abstractions.IContextProvider"/>'s live settings (SPEC F107.2), read through
/// <see cref="Abstractions.IContextSettingsProvider"/>.
/// </summary>
/// <param name="Enabled">
/// Off by default (fail-closed, F107.2/F108.1): while false the pipeline never calls
/// <see cref="Abstractions.IContextProvider.FetchAsync"/> for this provider and produces no output —
/// the same skip-never-silence handling a stale or failing fetch gets.
/// </param>
/// <param name="SegmentCadenceMinutes">
/// The cadence-slot width governing how often the pipeline may fetch: at most once per this many
/// minutes (F107.2's fetch-once-per-slot rule), regardless of how often the ticker calls in.
/// </param>
/// <param name="PatterCadenceMinutes">
/// The separate cadence (F107.5) the patter lane offers this provider's already-fetched
/// <see cref="ContextContent.PatterFact"/> — at most once per this many minutes. Independent of
/// <see cref="SegmentCadenceMinutes"/>: it gates when the SAME fetched content is next surfaced for
/// patter, not a second fetch.
/// </param>
/// <param name="PersonaId">
/// Which persona voices this provider's aired content (F107.7, <c>Context:{Key}:PersonaId</c>). Read
/// only by the T224 Orchestrator drain arm — the pipeline itself never interprets this value.
/// <see langword="null"/> and <c>0</c> (and any negative value) all mean the SAME thing: defer to the
/// on-air DJ, exactly like every other persona-voiced segment (LeadIn/BackAnnounce). Only a POSITIVE
/// value names an explicit persona; it is never the station's own voice by construction — the drain
/// arm degrades to the station voice only as the one further fallback for that positive id itself
/// failing to resolve (deleted persona, no store wired, F12.4), or for the on-air-DJ rung finding
/// nobody in the chair. <see langword="null"/> reads back identically to <c>0</c> on purpose: config
/// binding cannot distinguish "key absent" from "key bound to 0" for a nullable numeric setting, so an
/// unconfigured provider and an explicitly-zeroed one must resolve the same way — see
/// <see cref="Abstractions.NoOpContextSettingsProvider"/>'s own remarks for the "never itself the
/// reason a segment fails to air" framing this shares.
/// </param>
public sealed record ContextProviderSettings(
    bool Enabled, int SegmentCadenceMinutes, int PatterCadenceMinutes, long? PersonaId);
