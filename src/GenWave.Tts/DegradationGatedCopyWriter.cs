namespace GenWave.Tts;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Microsoft.Extensions.Options;

/// <summary>
/// The <see cref="ISegmentCopyWriter"/> <see cref="TtsSegmentSource"/> actually resolves (SPEC
/// F69.1, F69.4, STORY-188) — the ONE place degradation mode gates a render. While
/// <see cref="DegradationController"/> reports <see cref="DegradationMode.Normal"/>, this routes to
/// <see cref="LlmCopyWriter"/> exactly as before this feature shipped (it already degrades a
/// same-call miss to <paramref name="templateWriter"/> on its own, SPEC F34.4 — unchanged). In
/// <see cref="DegradationMode.Hard"/> this routes straight to <paramref name="templateWriter"/>,
/// unconditionally — zero LLM calls, matching SPEC F69.1 literally.
///
/// <para>
/// <see cref="DegradationMode.Soft"/> is NOT the same "zero calls" routing (T32 review finding: SPEC
/// F69.1 says Soft is "minimized calls", not "zero calls" — treating it identically to Hard made
/// Soft behaviorally indistinguishable and, worse, froze <see cref="LlmCopyStatusHolder"/>'s failure
/// signal the moment Soft was entered). Instead, at most one real <see cref="LlmCopyWriter"/>
/// attempt is claimed per <see cref="DegradationOptions.CooldownSeconds"/> window
/// (<see cref="ClaimSoftCadenceSlot"/>) — every other segment in that window routes to
/// <paramref name="templateWriter"/>. The claimed attempt's outcome (success or failure) still
/// reaches <see cref="LlmCopyStatusHolder"/> exactly as a Normal-mode attempt would, so a sustained
/// outage keeps feeding <see cref="DegradationController"/>'s drop signal even from inside Soft —
/// see the controller's own remarks for the second, independent probe-driven half of that fix.
/// </para>
///
/// <para>
/// Operator-explicit paths never pass through here at all (SPEC F69.4): the persona-preview seam
/// (<see cref="IPersonaPreviewWriter"/>) is registered directly against the same
/// <see cref="LlmCopyWriter"/> singleton this class wraps, so "operator-explicit actions are never
/// mode-gated" holds by construction — there is no runtime check to bypass, because there is no
/// path from a preview request into this class.
/// </para>
/// </summary>
public sealed class DegradationGatedCopyWriter(
    DegradationController controller,
    LlmCopyWriter llmWriter,
    TemplateCopyWriter templateWriter,
    IOptionsMonitor<DegradationOptions> degradationOptions,
    TimeProvider timeProvider) : ISegmentCopyWriter
{
    readonly object cadenceGate = new();
    DateTimeOffset? lastSoftAttemptAt;

    public async Task<SegmentCopy> WriteAsync(SegmentRequest request, CancellationToken ct)
    {
        // Evaluated before the attempt so a just-earned raise/drop (probe past cooldown, or a
        // failure streak crossing threshold) is used on THIS render, not the next one — playout
        // traffic is what drives re-evaluation in production; nothing else polls the controller on
        // a timer.
        var mode = controller.Evaluate().Mode;

        var attemptsLlm = mode switch
        {
            DegradationMode.Normal => true,
            DegradationMode.Soft => ClaimSoftCadenceSlot(),
            _ => false, // Hard (SPEC F69.1): zero calls, no cadence exception.
        };

        if (!attemptsLlm)
            return await templateWriter.WriteAsync(request, ct);

        var copy = await llmWriter.WriteAsync(request, ct);

        // Evaluated again immediately after: a failure this call just recorded to
        // LlmCopyStatusHolder reaches the mode right away rather than waiting for the next segment.
        controller.Evaluate();
        return copy;
    }

    /// <summary>
    /// True at most once per <see cref="DegradationOptions.CooldownSeconds"/> window (SPEC F69.1
    /// "minimized calls", T32 review finding) — the Soft cadence rule. Thread-safe: this writer is
    /// a singleton and could in principle be entered concurrently for two segments.
    /// </summary>
    bool ClaimSoftCadenceSlot()
    {
        var now = timeProvider.GetUtcNow();
        var cooldown = TimeSpan.FromSeconds(degradationOptions.CurrentValue.CooldownSeconds);
        lock (cadenceGate)
        {
            if (lastSoftAttemptAt is { } last && now - last < cooldown)
                return false;

            lastSoftAttemptAt = now;
            return true;
        }
    }
}
