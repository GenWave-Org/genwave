using GenWave.Core.Domain;
using GenWave.Core.TalkRules;

namespace GenWave.Tts;

/// <summary>
/// What a render hands out of the single-flight section (where it is planned) to the caller that records the line
/// (where it is settled). One per on-air render.
/// </summary>
sealed class RenderHandoff
{
    /// <summary>The break's callback ask, settled with the recorded line, a template fallback included (SPEC F218.44). Legacy and shadow only.</summary>
    public CallbackAsk? Ask { get; set; }

    /// <summary>
    /// Rules mode (SPEC F220.13): the accepted attempt. The caller records the utterance (if there is one), then hands this back to the
    /// flow with the utterance id. Null when nothing was accepted (template, dropped, or not rules mode).
    /// </summary>
    public DjLineAcceptedTalk? AcceptedTalk { get; set; }
}
