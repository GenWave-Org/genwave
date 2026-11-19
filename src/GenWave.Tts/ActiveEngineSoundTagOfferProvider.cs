namespace GenWave.Tts;

/// <summary>Offers the active engine's profile tags with the live cap (SPEC F219.16); nothing when either is empty or off.</summary>
public sealed class ActiveEngineSoundTagOfferProvider(IActiveVoiceEngine activeEngine, ISoundTagCapProvider cap) : ISoundTagOfferProvider
{
    public SoundTagOffer? Current
    {
        get
        {
            var tags = activeEngine.Current?.Profile.SoundTags;
            var max = cap.Current;
            return tags is { Count: > 0 } && max > 0 ? new SoundTagOffer(tags, max) : null;
        }
    }
}
