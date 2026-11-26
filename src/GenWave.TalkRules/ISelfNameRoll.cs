namespace GenWave.TalkRules;

/// <summary>
/// The one roll that decides whether this break's DJ is asked to say their own name (gh-#150). A service seam so the odds and the source
/// live in one place and a spec can pin the draw; a rule that rolls takes it by interface (F220.2).
/// </summary>
public interface ISelfNameRoll
{
    /// <summary>Rolls once: true when this break is asked to work the DJ's name in.</summary>
    bool Rolls();
}
