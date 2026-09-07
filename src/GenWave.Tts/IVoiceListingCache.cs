namespace GenWave.Tts;

/// <summary>
/// The narrow write-side seam a voice-pack install/uninstall needs onto <see cref="CachedVoiceLister"/>
/// (SPEC F166.4, PLAN T413) — deliberately NOT part of the published <c>GenWave.Abstractions</c> NuGet
/// (unlike <c>ITtsVoiceLister</c>, GenWave.Host already depends on GenWave.Tts directly, so this seam
/// needs no wider publication). <see cref="CachedVoiceLister"/> is registered concretely once and
/// exposed under both this interface and <c>ITtsVoiceLister</c> (see
/// <c>TtsServiceCollectionExtensions</c>'s own remarks) — a caller invalidating through this seam
/// invalidates the SAME cache <c>GET /api/voices</c> reads, so kokoro's post-install rescan
/// (SPEC F166.3) is reflected the very next voices listing rather than up to five minutes later.
/// </summary>
public interface IVoiceListingCache
{
    /// <summary>Drops the cached voice list — the next <see cref="ITtsVoiceLister.ListVoicesAsync"/>
    /// call re-fetches from the TTS backend regardless of how much of the TTL window remains.</summary>
    void Invalidate();
}
