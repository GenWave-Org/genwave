namespace GenWave.Orchestration;

/// <summary>
/// SPEC F82.3 — <see cref="PersonaRanker"/>'s tunables, defaulted to the PRD's proposed values
/// pending a listening pass against the demo library (TODO noted in SPEC.md, not this task's to
/// resolve). This record only ever carries the raw operator-facing setting: <see cref="PersonaRanker"/>
/// itself — not this record — is where F82.4's hard 5% exploration floor is enforced
/// (<see cref="PersonaRanker.MinimumExplorationRate"/>), so an operator setting of exactly 0 here is
/// preserved as written rather than silently rewritten to 0.05 at construction.
/// </summary>
public sealed record PersonaRankerOptions
{
    /// <summary>Multiplier on a matched taste rule's weight in the score sum (SPEC F82.2).</summary>
    public double BiasGain { get; init; } = 1.0;

    /// <summary>Multiplier on the absolute energy-vs-target distance penalty (SPEC F82.2).</summary>
    public double EnergyPull { get; init; } = 2.0;

    /// <summary>Softmax temperature over the Top-K scored pool (SPEC F82.3).</summary>
    public double Temperature { get; init; } = 0.7;

    /// <summary>How many top-scored candidates enter the softmax sample (SPEC F82.3).</summary>
    public int TopK { get; init; } = 18;

    /// <summary>
    /// The operator-facing exploration-slice setting (SPEC F82.3, F82.4). <see cref="PersonaRanker"/>
    /// clamps this up to its own 5% floor at pick time — this property itself is never clamped.
    /// </summary>
    public double ExplorationRate { get; init; } = 0.15;

    /// <summary>
    /// Multiplier on <c>PersonaRankCandidate.Nudge</c> in the score sum (SPEC F151.1, STORY-371, PLAN
    /// T370) — rung 0 only (F151.2): the envelope-only ladder never scores at all, so a candidate's
    /// nudge simply never reaches it. Exploration picks are nudge-blind too (T370 review HIGH-1): the
    /// nudge IS bias, and SPEC F82.4's exploration slice is bias-blind by law, not taste-blind
    /// specifically. Default 0.5, matching
    /// <c>GenWave.MediaLibrary.Options.GardenerOptions.NudgeGain</c>'s own default — the SAME value,
    /// not a coincidence: <see cref="PersonaRanker"/> lives in this framework-free project and cannot
    /// reference <c>GardenerOptions</c> (Architecture law L1), so the Host composition root
    /// (<c>GenWave.Host.Options.PersonaRankerOptionsServiceCollectionExtensions</c>) resolves the
    /// already-bound, already-boot-validated <c>IOptions&lt;GardenerOptions&gt;</c> and copies its
    /// <c>NudgeGain</c> onto the plain-value <see cref="PersonaRankerOptions"/> singleton via a
    /// <c>with</c> expression at the ONE place that singleton is built (MED-6, T370 review — not a
    /// <c>PostConfigure</c> callback: this property is <c>init</c>-only and cannot be reassigned from
    /// one) — one source of truth, no separate <c>PersonaRanker:NudgeGain</c> key exists or is
    /// documented. This property's own default (0.5) is what a deployment gets if that copy step
    /// never runs (e.g. a pre-T370 test construction), so it is never left at an undocumented value.
    /// </summary>
    public double NudgeGain { get; init; } = 0.5;
}
