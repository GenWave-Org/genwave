namespace GenWave.Tts;

using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// Piper as the PRIMARY engine (SPEC F99.4, STORY-257) — the piper-only topology's explicit
/// opt-in path (<c>compose.piper-only.yaml</c> sets <c>Tts:PiperPrimaryEndpoint</c>): every
/// render goes straight to Piper, satisfying voice integrity (F99.1) by producing the DJ's own
/// configured voice directly, with no fallback chain to lean on — <see cref="FallbackTtsSynthesizer"/>
/// selects this instance as its <c>primary</c> collaborator (see
/// <see cref="TtsServiceCollectionExtensions"/>), and an empty chain makes that class a
/// transparent pass-through, exactly the same mechanic the default Kokoro-primary topology uses.
///
/// Reuses the exact wire mechanics <see cref="PiperTtsSynthesizer"/> speaks as a fallback hop
/// (<see cref="PiperWireProtocol"/>) — only the endpoint SOURCE differs: this reads
/// <see cref="TtsOptions.PiperPrimaryEndpoint"/> (a dedicated key, never <see cref="TtsOptions.Endpoint"/>
/// — see that property's remarks for why repointing <c>Endpoint</c> itself would be wrong).
/// </summary>
public sealed class PiperPrimaryTtsSynthesizer(
    HttpClient http,
    IOptionsMonitor<TtsOptions> ttsOptions) : ITtsSynthesizer
{
    public Task<string> SynthesizeAsync(string text, string voice, CancellationToken ct) =>
        SynthesizeAsync(new TtsRenderContext(text, voice, Kind: null), ct);

    public Task<string> SynthesizeAsync(TtsRenderContext context, CancellationToken ct)
    {
        var cfg = ttsOptions.CurrentValue;
        var endpoint = cfg.PiperPrimaryEndpoint;
        if (string.IsNullOrEmpty(endpoint))
        {
            // Unreachable in production: TtsServiceCollectionExtensions only ever resolves this
            // class as FallbackTtsSynthesizer's primary when PiperPrimaryEndpoint is set. Fails
            // loudly rather than rendering against an empty base URL, guarding hand-built DI
            // wiring (tests, tools) that skips that selection.
            throw new InvalidOperationException(
                "PiperPrimaryTtsSynthesizer resolved with no Tts:PiperPrimaryEndpoint configured.");
        }

        return PiperWireProtocol.RenderAsync(http, endpoint, context, cfg, ct);
    }
}
