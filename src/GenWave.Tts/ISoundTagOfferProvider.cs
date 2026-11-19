namespace GenWave.Tts;

/// <summary>The sound tag offer for the next DJ prompt (SPEC F219.16), read fresh per call so a live engine or cap change applies to it.</summary>
public interface ISoundTagOfferProvider
{
    /// <summary>The offer, or null when the active engine lists no tags or the cap is 0 (the prompt then stays as it was before tags).</summary>
    SoundTagOffer? Current { get; }
}
