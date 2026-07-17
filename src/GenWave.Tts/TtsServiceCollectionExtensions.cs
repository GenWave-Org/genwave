using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Events;
using GenWave.Loudness;

namespace GenWave.Tts;

/// <summary>
/// Composition of the TTS service (gitea-#243): options, copy-writer chain, synthesizer/voices clients,
/// and the safe-segment authoring pipeline. The host wires the whole service with one call; a
/// module overrides individual seams (<see cref="ISegmentCopyWriter"/>, <see cref="ITtsSynthesizer"/>,
/// …) after this runs.
/// </summary>
public static class TtsServiceCollectionExtensions
{
    public static IServiceCollection AddGenWaveTts(this IServiceCollection services, IConfiguration configuration)
    {
        // Event-seam default (gitea-#246): TtsSegmentSource publishes SegmentGenerated; TryAdd so the
        // host's real binding (AddGenWavePlayout) wins.
        services.TryAddSingleton<IStationEventSink, NoOpStationEventSink>();

        // TTS options — validated at startup; RenderBudgetSeconds must be positive.
        services
            .AddOptions<TtsOptions>()
            .Bind(configuration.GetSection(TtsOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // LLM options — registered unconditionally (SPEC F34.2); an empty Llm:Endpoint just means
        // LlmCopyWriter stays disabled. IOptionsMonitor<LlmOptions> (not IOptions) is what
        // LlmCopyWriter reads per render, so a live edit to Llm:Endpoint/Model/TimeoutSeconds/
        // MaxCopyChars applies without a restart (F36.2).
        services
            .AddOptions<LlmOptions>()
            .Bind(configuration.GetSection(LlmOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // TTS wiring: ISegmentCopyWriter is the copy-writer seam (SPEC F34.1) TtsSegmentSource
        // consumes. TemplateCopyWriter is registered concretely as the terminal fallback rung;
        // LlmCopyWriter (SPEC F34.2-F34.5) is the seam's active implementation — it authors
        // LeadIn/BackAnnounce from the configured LLM and degrades to TemplateCopyWriter on any
        // miss, including a disabled (empty Llm:Endpoint) writer. LlmCopyStatusHolder is the
        // in-memory last-attempt record GET /api/status (STORY-125) reads.
        services
            .AddSingleton<PatterTemplateRenderer>()
            .AddSingleton<TemplateCopyWriter>()
            .AddSingleton<LlmCopyStatusHolder>()
            // LlmCopyWriter also consumes IActivePersonaAccessor (a host-registered seam) —
            // resolved per LLM render only, composing the active persona's backstory + style into
            // the prompt (SPEC F35.2/F35.3). Registered concretely ONCE and exposed under BOTH
            // seams it implements — the on-air ISegmentCopyWriter (always-succeeds, template
            // fallback) and the preview-only IPersonaPreviewWriter (never silently degrades, SPEC
            // F35.6/T7) — so the persona preview endpoint reuses the exact same prompt-building/
            // hygiene instance the feeder does, never a second parallel writer.
            .AddSingleton<LlmCopyWriter>()
            .AddSingleton<ISegmentCopyWriter>(sp => sp.GetRequiredService<LlmCopyWriter>())
            .AddSingleton<IPersonaPreviewWriter>(sp => sp.GetRequiredService<LlmCopyWriter>())
            .AddSingleton<ITtsSegmentSource, TtsSegmentSource>();

        // TTS/voices clients deliberately carry no BaseAddress (SPEC F36.1–F36.2, F36.4) —
        // Tts:Endpoint is read from IOptionsMonitor<TtsOptions>.CurrentValue and an absolute URI is
        // built per call inside KokoroTtsSynthesizer/KokoroVoiceLister, so a live PUT to
        // Tts:Endpoint applies to the next render/voices call with no api restart.
        // IOptionsMonitor<TtsOptions> is resolved automatically as an ordinary constructor
        // dependency — no configure delegate needed on either registration.
        services.AddHttpClient<KokoroTtsSynthesizer>();

        // Voices listing (SPEC F29.4, STORY-097): same Tts:Endpoint as the synthesizer above — no
        // separate config key for the voices call. CachedVoiceLister wraps the typed HttpClient
        // with a ~5 min in-memory TTL so a Safe content form load never round-trips Kokoro on
        // every keystroke.
        services.AddHttpClient<KokoroVoiceLister>();

        // LlmCopyWriter's HTTP client (SPEC F34.3, F36.2): deliberately no BaseAddress here — the
        // endpoint comes from IOptionsMonitor<LlmOptions>.CurrentValue per render, so a live PUT
        // to Llm:Endpoint takes effect on the next call without an api restart.
        // MaxResponseContentBufferSize bounds a completions reply (T3 review finding) — a
        // misbehaving/compromised endpoint can't make this writer buffer an unbounded response body.
        services.AddHttpClient(LlmCopyWriter.HttpClientName, client =>
        {
            client.MaxResponseContentBufferSize = LlmCopyWriter.MaxResponseContentBytes;
        });

        services
            // IOptionsMonitor<TtsOptions> (not the KokoroVoiceLister's own snapshot) so a
            // repointed Tts:Endpoint invalidates the short TTL cache instead of serving the OLD
            // endpoint's voice list for up to 5 more minutes (SPEC F36.4).
            .AddSingleton<ITtsVoiceLister>(sp =>
                new CachedVoiceLister(
                    sp.GetRequiredService<KokoroVoiceLister>(),
                    sp.GetRequiredService<IOptionsMonitor<TtsOptions>>(),
                    TimeSpan.FromMinutes(5)))
            // The typed HttpClient factory registers KokoroTtsSynthesizer as transient; expose it
            // via the singleton interface by resolving from the factory on first use.
            .AddSingleton<ITtsSynthesizer>(sp => sp.GetRequiredService<KokoroTtsSynthesizer>());

        return services;
    }

    /// <summary>
    /// Safe-loop authoring (F27, STORY-078/079): SafeSegmentAuthor composes the shipped
    /// TTS/mixer/analyzer/authored-insert seams into one all-or-nothing pipeline. Registered
    /// behind <see cref="ISafeSegmentAuthor"/> so callers (the authoring endpoint, the boot seed)
    /// can be tested with a fake without exercising the real render pipeline.
    /// </summary>
    public static IServiceCollection AddGenWaveSafeSegmentAuthoring(this IServiceCollection services) =>
        services
            .AddSingleton<IAudioMixer, FfmpegAudioMixer>()
            .AddSingleton<SafeSegmentAuthor>()
            .AddSingleton<ISafeSegmentAuthor>(sp => sp.GetRequiredService<SafeSegmentAuthor>());
}
