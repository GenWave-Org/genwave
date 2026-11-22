using GenWave.Core.Domain;

namespace GenWave.Tts;

/// <summary>
/// Where sound tags are decided just before a render (SPEC F219.17, F219.19): copy for a DJ or crosstalk line keeps the
/// engine's tags up to the live cap; ad copy and any engine without tags get none. Runs BEFORE the speech normaliser so the
/// normaliser only ever sees tags that may air. The engine is the one that will render: the active engine on air, the named
/// engine for a "Hear it".
/// </summary>
public sealed class SoundTagRenderGate(IActiveVoiceEngine activeEngine, ISoundTagCapProvider cap)
{
    /// <param name="engine">The engine that will render, or null for the active engine.</param>
    public string Apply(string text, SegmentKind? kind, VoiceEngine? engine = null)
    {
        if (kind == SegmentKind.Ad)
            return SoundTags.Strip(text);

        var profile = (engine ?? activeEngine.Current)?.Profile;
        return profile is null ? SoundTags.Strip(text) : SoundTags.Apply(text, profile, cap.Current);
    }
}
