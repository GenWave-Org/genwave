namespace GenWave.Tts;

/// <summary>
/// Binds <c>Tts:SoundTags:*</c> (SPEC F219.16/.17). Live setting <c>Tts:SoundTags:MaxPerBreak</c>: the most sound tags one break
/// may carry; 0 turns them off. Range 0-5 is enforced by the settings validator (a nested DataAnnotation is dead at boot).
/// </summary>
public sealed class TtsSoundTagsOptions
{
    public const string Section = "Tts:SoundTags";

    public int MaxPerBreak { get; set; } = 1;
}
