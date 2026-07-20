namespace GenWave.Core.Abstractions;

using GenWave.Core.Domain;

/// <summary>
/// Low-level TTS contract: text + voice in, an audio file path out (§0.1).
/// Faults surface as exceptions; the caller (<see cref="ITtsSegmentSource"/>) is responsible for
/// translating failures to null so that nothing propagates to the playout loop.
/// </summary>
public interface ITtsSynthesizer
{
    /// <summary>
    /// Synthesizes <paramref name="text"/> using the specified <paramref name="voice"/> and returns
    /// the absolute path to the generated audio file.
    /// </summary>
    Task<string> SynthesizeAsync(string text, string voice, CancellationToken ct);

    /// <summary>
    /// Kind-aware overload (SPEC F70.3, STORY-191): carries the speech kind alongside text/voice so
    /// an implementation can route per kind (see the TTS module's <c>Tts:EngineByKind</c> override
    /// map) without widening the two-arg contract every existing implementation and test double
    /// already satisfies. The default implementation below simply discards
    /// <see cref="TtsRenderContext.Kind"/> and forwards to
    /// <see cref="SynthesizeAsync(string, string, CancellationToken)"/> — every implementation that
    /// does not override this member (every engine client, every fake) behaves EXACTLY as it did
    /// before this member existed. Only a decorator that needs to see the kind (the
    /// normalization/correction seam that must pass it through, and the fallback router that acts on
    /// it) overrides this method; the TTS segment source is the only caller that knows a real
    /// <see cref="SegmentKind"/> to stamp here — every other caller (safe/authored segments, the
    /// admin preview endpoint) keeps calling the plain two-arg overload, which reaches this one with
    /// <c>Kind: null</c>.
    /// </summary>
    Task<string> SynthesizeAsync(TtsRenderContext context, CancellationToken ct) =>
        SynthesizeAsync(context.Text, context.Voice, ct);
}
