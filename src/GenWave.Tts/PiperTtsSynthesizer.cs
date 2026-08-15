namespace GenWave.Tts;

using Microsoft.Extensions.Options;
using GenWave.Core.Domain;

/// <summary>
/// Piper-engine hop renderer (SPEC F70.1, STORY-190, gh-#147) — the <c>"piper"</c>
/// <see cref="IFallbackProfileRenderer"/> the chain executor <see cref="FallbackTtsSynthesizer"/>
/// resolves for piper-kind hops (including the implicit legacy single-hop chain the flat
/// <c>Tts:Fallback:Endpoint</c>/<c>Voice</c> keys form). Targets the upstream
/// <c>piper.http_server</c> HTTP wrapper the compose <c>piper</c> service runs — the actual
/// POST/strip/write wire mechanics live in <see cref="PiperWireProtocol"/>, shared with
/// <see cref="PiperPrimaryTtsSynthesizer"/> (Piper as the PRIMARY engine, SPEC F99.4); only the
/// endpoint SOURCE differs between the two: this reads it from the operator's
/// <see cref="TtsFallbackProfile"/>, not a fixed option.
///
/// No per-request voice selector exists on that wrapper — exactly one voice model is baked into
/// the running container at start (compose.yaml's <c>MODEL_DOWNLOAD_LINK</c>) — so neither the
/// caller's <see cref="TtsRenderContext.Voice"/> nor the hop's display-only
/// <see cref="TtsFallbackProfile.Voice"/> is ever put on the wire; see that property's
/// schema-level remarks (gh-#147's honest-labeling contract).
///
/// No boot-frozen <see cref="HttpClient.BaseAddress"/>, same discipline as
/// <see cref="KokoroTtsSynthesizer"/> (SPEC F36.1-F36.2): the hop endpoint arrives per call from
/// the profile, itself resolved from <see cref="IOptionsMonitor{TOptions}.CurrentValue"/>
/// upstream, so a live repoint of the legacy <c>Tts:Fallback:Endpoint</c> applies to the very
/// next render with no api restart.
/// </summary>
public sealed class PiperTtsSynthesizer(
    HttpClient http,
    IOptionsMonitor<TtsOptions> ttsOptions) : IFallbackProfileRenderer
{
    public string Engine => DependencyNames.Piper;

    public Task<string> RenderAsync(TtsFallbackProfile profile, TtsRenderContext context, CancellationToken ct) =>
        PiperWireProtocol.RenderAsync(http, profile.Endpoint, context, ttsOptions.CurrentValue, ct);
}
