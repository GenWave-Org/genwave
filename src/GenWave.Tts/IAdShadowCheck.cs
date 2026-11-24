using GenWave.Core.TalkRules;

namespace GenWave.Tts;

/// <summary>
/// The shadow hook <see cref="AdScriptWriter"/> calls once today's code has decided attempt 1 of an ad (SPEC F220.18,
/// F220.19). An interface because the ad snapshot lives in GenWave.Ads, which this project must never reference (L10);
/// GenWave.Ads supplies the implementation. It only observes: an implementation must never throw for anything short of
/// the caller's own cancellation, and the writer reads nothing back.
/// </summary>
public interface IAdShadowCheck
{
    /// <summary>True when the live mode is shadow, read fresh each call (F220.18).</summary>
    bool IsOn();

    /// <summary>
    /// Judges and records attempt 1 of one ad. <paramref name="rawReply"/> is the model's reply as received (empty when the
    /// call faulted before any reply). <paramref name="judgedText"/> is the reply after the line-aware clean-up and BEFORE
    /// the phone rewrite, i.e. the text the rules judge (F220.17); null when there is nothing whole to judge (no reply, a
    /// reply cut short by the token cap, or nothing left after the line-aware clean-up), in which case the row is
    /// stored with no verdicts. A text the phone rewrite empties IS judged. A throw from the caller's validator is not a
    /// null: the row is stored as Dropped WITH the verdicts. A throw from the rule book logs a Warning and stores nothing.
    /// </summary>
    ValueTask CheckAsync(
        AdScriptWriteRequest request, string rawReply, string? judgedText, TalkLegacyOutcome legacyOutcome, CancellationToken ct);
}
