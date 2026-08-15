namespace GenWave.Tts;

/// <summary>
/// The <see cref="DependencyNames"/> key of the engine chosen as primary at composition time (SPEC
/// F99.4, STORY-257) — Kokoro on every topology except the piper-only opt-in, where it is Piper.
/// Computed ONCE from <see cref="TtsOptions.PiperPrimaryEndpoint"/>
/// (<see cref="TtsServiceCollectionExtensions"/>) and shared as its own singleton so a reader that
/// only needs to know WHICH engine is primary (<see cref="VoiceHealthReader"/>) never has to
/// resolve <see cref="FallbackTtsSynthesizer"/> itself — that class's own primary is an
/// HTTP-backed typed client (<c>KokoroTtsSynthesizer</c>/<c>PiperPrimaryTtsSynthesizer</c>), and
/// constructing it as a side effect of an unrelated status read would violate <c>GET
/// /api/status</c>'s "no <see cref="System.Net.Http.IHttpClientFactory"/> dependency at all"
/// contract (STORY-125 AC3). Both <see cref="FallbackTtsSynthesizer"/>'s own composition and
/// <see cref="VoiceHealthReader"/> read this SAME instance, so they can never disagree on which
/// engine is primary.
/// </summary>
public sealed record PrimaryVoiceEngine(string DependencyName);
