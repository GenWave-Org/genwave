namespace GenWave.Tts;

using GenWave.Core.Domain;

/// <summary>
/// Renders one fallback-chain hop (gh-#147): given a <see cref="TtsFallbackProfile"/> and the
/// caller's already-normalized render context, produces a rendered audio file and returns its path —
/// the per-engine wire adapter <see cref="FallbackTtsSynthesizer"/> resolves by
/// <see cref="Engine"/> while executing the chain. One implementation per engine kind
/// (<see cref="PiperTtsSynthesizer"/>, <see cref="KokoroFallbackRenderer"/>), registered in
/// <see cref="TtsServiceCollectionExtensions"/>; <see cref="TtsFallbackOptionsValidator"/>
/// guarantees at startup that every configured hop names a kind a renderer exists for.
///
/// <see cref="TtsRenderContext"/> — not the bare <c>(text, voice)</c> pair this contract carried
/// pre-T137 — so a kokoro-kind hop can read <see cref="TtsRenderContext.Rules"/> the identical way
/// the primary <see cref="KokoroTtsSynthesizer"/> already does (SPEC F97.6): resolved upstream, at
/// <see cref="TtsSegmentSource"/>, and carried down unchanged — an implementation must never resolve
/// rules itself from a persona/rules provider (ARCHITECTURE.md "Carrying persona facts to the
/// engine"). An implementation that has no use for anything beyond text/voice
/// (<see cref="PiperTtsSynthesizer"/>, which strips markup outright) simply ignores the rest of the
/// context, mirroring how <see cref="ITtsSynthesizer"/>'s own kind-aware overload widened the same
/// way at F70.3/T134 with zero behavior change for a caller that never opted in.
/// </summary>
public interface IFallbackProfileRenderer
{
    /// <summary>The canonical engine-kind name this renderer serves (<see cref="DependencyNames"/>).</summary>
    string Engine { get; }

    /// <summary>
    /// Renders <paramref name="context"/> against <paramref name="profile"/>.
    /// <see cref="TtsRenderContext.Voice"/> is the caller's per-request voice — each engine decides
    /// whether the profile's own <see cref="TtsFallbackProfile.Voice"/> overrides it on the wire
    /// (kokoro) or is display-only (piper); see that property's schema-level remarks.
    /// </summary>
    Task<string> RenderAsync(TtsFallbackProfile profile, TtsRenderContext context, CancellationToken ct);
}
