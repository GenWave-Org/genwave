namespace GenWave.Tts;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Events;

/// <summary>
/// The LLM degradation state machine (SPEC F69.1–F69.5, STORY-188): Normal/Soft/Hard, one step at
/// a time. <see cref="Evaluate"/> is the single entry point — both a read (returns the current
/// <see cref="DegradationSnapshot"/>) and, when warranted, a write (applies and logs a transition)
/// — called from two places: <see cref="DegradationGatedCopyWriter"/> around every real playout
/// attempt (so a fresh call failure/recovery reaches the mode immediately, not on some separate
/// timer), and <c>GET /api/status</c> (so a just-applied pin or an elapsed cooldown is visible on
/// the very next poll, not only on the next render). Neither call performs I/O — every input it
/// reads (<see cref="IDependencyHealth"/>, <see cref="LlmCopyStatusHolder"/>, the options monitors)
/// is already a cached, in-memory value, so calling <see cref="Evaluate"/> from a status GET never
/// violates the "no health check inside the render/status window" discipline
/// (<see cref="IDependencyHealth"/>'s own remarks).
///
/// <para>
/// PIN (SPEC F69.3) always wins: <see cref="LlmOptions.DegradationPin"/> (allowlisted, live) is
/// checked first on every evaluation. Any value other than <c>"auto"</c> (case-insensitive) holds
/// the named mode; auto drop/raise resumes on the very next evaluation once it is reset to
/// <c>"auto"</c> — using whatever mode it currently holds as the starting point, not a reset to
/// Normal.
/// </para>
///
/// <para>
/// NOT CONFIGURED (the empty-<c>Llm:Endpoint</c> case, SPEC F34.2): with no pin active, this
/// reports Hard with cause "not configured" rather than showing Normal while secretly making zero
/// calls (LlmCopyWriter's own guard already short-circuits an empty endpoint) — an operator glancing
/// at status should see the functional truth. This is a distinct, stable state, not a failure the
/// drop counter ever sees: <see cref="LlmCopyWriter.WriteAsync"/> never calls
/// <see cref="LlmCopyStatusHolder.Record"/> for a disabled writer, so the not-configured branch
/// below is evaluated directly from <see cref="LlmOptions.Endpoint"/> and never touches (or
/// thrashes) the failure counter.
/// </para>
///
/// <para>
/// DROP (call outcomes) vs RAISE (probes) — SPEC F69.2's own split, applied literally, with one
/// addition (T32 review finding — Soft must not be a one-way trap during a sustained outage):
/// Normal → Soft is justified only by <see cref="LlmCopyStatusHolder.ConsecutiveFailureCount"/> —
/// genuine on-air completion attempts (<see cref="TryDropByFailures"/>). Soft → Hard is justified by
/// EITHER of two independent signals, checked every <see cref="Evaluate"/> call: more consecutive
/// real-call failures past the same threshold (<see cref="DegradationGatedCopyWriter"/> still
/// attempts one real call per cooldown window while in Soft — SPEC F69.1 "minimized calls", not
/// zero — so this counter keeps moving even from inside Soft), OR a cached-UNHEALTHY
/// <see cref="IDependencyHealth"/> verdict for <see cref="DependencyNames.Ollama"/> held for at
/// least <see cref="DegradationOptions.CooldownSeconds"/> (<see cref="TryDropByProbe"/>) — the
/// probe keeps observing Ollama on its own background cadence regardless of whether this feature is
/// calling it at all, so a sustained outage reaches Hard automatically even if Soft's throttled
/// attempts happen to keep missing the failure threshold. A verdict whose
/// <see cref="DependencyHealthVerdict.Reason"/> is
/// <see cref="DependencyHealthProber.NotConfiguredReason"/> never counts for this — that is the
/// disabled-by-design state the NOT CONFIGURED branch above already owns, not an outage.
/// </para>
///
/// <para>
/// RAISE is symmetric with the probe-drop half of the above: a cached-HEALTHY verdict held for at
/// least <see cref="DegradationOptions.CooldownSeconds"/> raises the mode one step
/// (<see cref="TryRaise"/>). Both drop-by-probe and raise re-arm off the same <c>since</c> timer,
/// which every <see cref="Transition"/> resets — so a full Hard → Soft → Normal recovery is always
/// one step per <see cref="Evaluate"/> call with a fresh cooldown wait in between, never both steps
/// in a single call, by construction (the method returns as soon as either <see cref="TryDrop"/> or
/// <see cref="TryRaise"/> applies one transition).
/// </para>
/// </summary>
public sealed class DegradationController(
    IDependencyHealth dependencyHealth,
    LlmCopyStatusHolder llmStatusHolder,
    IOptionsMonitor<LlmOptions> llmOptions,
    IOptionsMonitor<DegradationOptions> degradationOptions,
    TimeProvider timeProvider,
    ILogger<DegradationController> logger,
    IStationEventSink? events = null)
{
    // Mode-change publish seam (SPEC F72.1, STORY-195, T39 — depends on this class's own T32
    // transitions); no-op unless the host binds a real sink (gitea-#246).
    readonly IStationEventSink events = events ?? NoOpStationEventSink.Instance;

    /// <summary>Cause shown while <see cref="LlmOptions.Endpoint"/> is empty — see the class remarks.</summary>
    public const string NotConfiguredCause = "LLM not configured (Llm:Endpoint is empty)";

    readonly object gate = new();

    DegradationMode mode = DegradationMode.Normal;
    bool pinned;
    DateTimeOffset since = timeProvider.GetUtcNow();
    string cause = "startup default";

    // The ConsecutiveFailureCount value already "spent" on the most recent transition (of any
    // kind — drop, raise, or pin). TryDrop only reacts to failures ACCRUED SINCE that point, so a
    // long-stale count (frozen while Soft/Hard bypass the LLM, or left over from a since-resolved
    // not-configured period) can never masquerade as a fresh streak the moment auto resumes.
    int failuresAtLastTransition;

    /// <summary>
    /// Re-derives the current mode from whatever is cached right now (pin, LLM configuredness,
    /// call-outcome/probe state) and returns the resulting snapshot. Applies and logs a transition
    /// exactly when the mode (or the pinned flag) actually changes — a poll that finds nothing new
    /// is silent.
    /// </summary>
    public DegradationSnapshot Evaluate()
    {
        lock (gate)
        {
            var pin = ParsePin(llmOptions.CurrentValue.DegradationPin);
            if (pin is { } pinnedMode)
            {
                if (!pinned || mode != pinnedMode)
                    Transition(pinnedMode, pinned: true, $"pinned to {pinnedMode.ToString().ToLowerInvariant()}");

                return Snapshot();
            }

            if (pinned)
            {
                // Just unpinned: give the auto machinery a clean slate rather than reinterpreting
                // whatever failure count built up (or was frozen) under the pin as a brand-new
                // streak — see failuresAtLastTransition's own remarks.
                pinned = false;
                failuresAtLastTransition = llmStatusHolder.ConsecutiveFailureCount;
            }

            if (string.IsNullOrEmpty(llmOptions.CurrentValue.Endpoint))
            {
                if (mode != DegradationMode.Hard || cause != NotConfiguredCause)
                    Transition(DegradationMode.Hard, pinned: false, NotConfiguredCause);

                return Snapshot();
            }

            if (!TryDrop())
                TryRaise();

            return Snapshot();
        }
    }

    bool TryDrop()
    {
        if (mode == DegradationMode.Hard) return false;

        if (TryDropByFailures()) return true;

        // Soft is not a one-way trap (T32 review finding, SPEC F69.2 "drops must chain during a
        // sustained outage") — see the class remarks for why the probe is the second, independent
        // signal that lets Soft -> Hard fire even when Soft's cadence-throttled real attempts
        // haven't yet racked up a fresh failure streak of their own.
        return mode == DegradationMode.Soft && TryDropByProbe();
    }

    bool TryDropByFailures()
    {
        var threshold = degradationOptions.CurrentValue.ConsecutiveFailureThreshold;
        var failures = llmStatusHolder.ConsecutiveFailureCount;
        var newFailures = failures - failuresAtLastTransition;
        if (newFailures < threshold) return false;

        var next = (DegradationMode)((int)mode + 1);
        Transition(next, pinned: false, $"{newFailures} consecutive LLM failures (threshold {threshold})");
        return true;
    }

    bool TryDropByProbe()
    {
        var verdict = dependencyHealth.GetVerdict(DependencyNames.Ollama);
        if (verdict is not { Healthy: false } || verdict.Reason == DependencyHealthProber.NotConfiguredReason)
            return false;

        var cooldown = TimeSpan.FromSeconds(degradationOptions.CurrentValue.CooldownSeconds);
        if (timeProvider.GetUtcNow() - since < cooldown) return false;

        Transition(DegradationMode.Hard, pinned: false,
            $"{DependencyNames.Ollama} dependency probe unhealthy after cooldown ({verdict.Reason})");
        return true;
    }

    bool TryRaise()
    {
        if (mode == DegradationMode.Normal) return false;

        var verdict = dependencyHealth.GetVerdict(DependencyNames.Ollama);
        if (verdict is not { Healthy: true }) return false;

        var cooldown = TimeSpan.FromSeconds(degradationOptions.CurrentValue.CooldownSeconds);
        if (timeProvider.GetUtcNow() - since < cooldown) return false;

        var next = (DegradationMode)((int)mode - 1);
        Transition(next, pinned: false, "dependency probe healthy after cooldown");
        return true;
    }

    void Transition(DegradationMode newMode, bool pinned, string cause)
    {
        var previous = mode;
        mode = newMode;
        this.pinned = pinned;
        since = timeProvider.GetUtcNow();
        this.cause = cause;
        failuresAtLastTransition = llmStatusHolder.ConsecutiveFailureCount;

        logger.LogInformation(
            "LLM degradation mode {Previous} -> {New} ({Cause})", previous, newMode, cause);

        events.Publish(new DegradationModeChanged(previous.ToString(), newMode.ToString(), cause));
    }

    DegradationSnapshot Snapshot() => new(mode, pinned, since, cause);

    static DegradationMode? ParsePin(string pin) => pin.Trim().ToLowerInvariant() switch
    {
        "normal" => DegradationMode.Normal,
        "soft" => DegradationMode.Soft,
        "hard" => DegradationMode.Hard,
        // "auto", empty, or anything unrecognized (SettingValidator should already have rejected
        // this at the api boundary — stay defensive rather than throw from a read path).
        _ => null,
    };
}
