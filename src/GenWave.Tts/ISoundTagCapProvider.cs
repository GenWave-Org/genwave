namespace GenWave.Tts;

/// <summary>The live <c>Tts:SoundTags:MaxPerBreak</c> cap (SPEC F219.16/.17). Implementations re-read on every call; callers must not cache it.</summary>
public interface ISoundTagCapProvider
{
    /// <summary>Most sound tags one break may carry; 0 means off. Never negative.</summary>
    int Current { get; }
}
