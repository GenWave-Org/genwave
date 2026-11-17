using GenWave.Core.Domain;

namespace GenWave.Tts;

/// <summary>
/// Carries a break's callback ask out of the single-flight section (where it is planned) to the caller that records
/// the line (where it is settled), including through a template fallback (SPEC F218.44). One slot per on-air render.
/// </summary>
sealed class CallbackSlot
{
    public CallbackAsk? Ask { get; set; }
}
