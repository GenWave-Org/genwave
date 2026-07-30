namespace GenWave.Tts;

/// <summary>
/// Renders one fallback-chain hop (gh-#147): given a <see cref="TtsFallbackProfile"/> and the
/// caller's already-normalized text/voice, produces a rendered audio file and returns its path —
/// the per-engine wire adapter <see cref="FallbackTtsSynthesizer"/> resolves by
/// <see cref="Engine"/> while executing the chain. One implementation per engine kind
/// (<see cref="PiperTtsSynthesizer"/>, <see cref="KokoroFallbackRenderer"/>), registered in
/// <see cref="TtsServiceCollectionExtensions"/>; <see cref="TtsFallbackOptionsValidator"/>
/// guarantees at startup that every configured hop names a kind a renderer exists for.
/// </summary>
public interface IFallbackProfileRenderer
{
    /// <summary>The canonical engine-kind name this renderer serves (<see cref="DependencyNames"/>).</summary>
    string Engine { get; }

    /// <summary>
    /// Renders <paramref name="text"/> against <paramref name="profile"/>.
    /// <paramref name="requestVoice"/> is the caller's per-request voice — each engine decides
    /// whether the profile's own <see cref="TtsFallbackProfile.Voice"/> overrides it on the wire
    /// (kokoro) or is display-only (piper); see that property's schema-level remarks.
    /// </summary>
    Task<string> RenderAsync(TtsFallbackProfile profile, string text, string requestVoice, CancellationToken ct);
}
