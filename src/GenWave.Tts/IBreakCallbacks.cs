using GenWave.Core.Domain;

namespace GenWave.Tts;

/// <summary>
/// The strict half of <see cref="BreakCallbacks"/> (SPEC F218.40-F218.45): plan and settle a callback offer with no fail-open guard, so a
/// caller that reports faults itself (the <c>callbacks</c> rule, via the rule book, F220.9) sees them.
/// </summary>
public interface IBreakCallbacks
{
    /// <summary>The ask for this break, with the offer row written before it returns; null when the kind carries none or the setting is 0. Faults propagate.</summary>
    Task<CallbackAsk?> PlanStrictAsync(CallbackBreak callbackBreak, Persona? persona, PersonaCard? card, CancellationToken ct);

    /// <summary>Settles the offer with the line by the word rule; a fallback line, or one with no utterance id, settles unused. Faults propagate.</summary>
    Task SettleStrictAsync(CallbackAsk ask, long? utteranceId, string? line, bool fallback, CancellationToken ct);
}
