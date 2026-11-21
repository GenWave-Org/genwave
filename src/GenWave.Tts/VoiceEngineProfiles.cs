namespace GenWave.Tts;

/// <summary>The profiles that ship (SPEC F219.1): <c>kokoro</c>, <c>chatterbox</c>, <c>piper</c>.</summary>
public static class VoiceEngineProfiles
{
    public const string KokoroName = "kokoro";
    public const string ChatterboxName = "chatterbox";
    public const string PiperName = "piper";

    public static readonly VoiceEngineProfile Kokoro = new(
        KokoroName, Speed: true, Ipa: true, PauseTag: true, Clones: false, PicksVoice: true, SoundTags: [], MaxChars: null);

    // Turbo ignores speed; the 9 starting event tags are SPEC F219.20.
    public static readonly VoiceEngineProfile Chatterbox = new(
        ChatterboxName, Speed: false, Ipa: false, PauseTag: false, Clones: true, PicksVoice: true,
        SoundTags:
        [
            "[clear throat]", "[sigh]", "[shush]", "[cough]", "[groan]",
            "[sniff]", "[gasp]", "[chuckle]", "[laugh]",
        ],
        MaxChars: null);

    // PiperWireProtocol posts bare text/plain: no pace, markup or IPA reaches the engine today.
    public static readonly VoiceEngineProfile Piper = new(
        PiperName, Speed: false, Ipa: false, PauseTag: false, Clones: false, PicksVoice: false, SoundTags: [], MaxChars: null);

    public static IReadOnlyList<VoiceEngineProfile> All { get; } = [Kokoro, Chatterbox, Piper];

    public static VoiceEngineProfile? Find(string? name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));
}
