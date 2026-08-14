using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
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

        // Patter-lane fact source default (SPEC F107.5, STORY-298, PLAN T225) — mirrors
        // IStationEventSink's own shape immediately above: TryAdd so the T226 Host wiring, once it
        // registers the real GenWave.Context.ContextPipeline binding, overrides it. This project has
        // no reference to GenWave.Context (an L1 project one layer further out) and never needs one —
        // the seam lives in GenWave.Core precisely so LlmCopyWriter can depend on the contract alone.
        services.TryAddSingleton<IContextPatterFactSource, NoOpContextPatterFactSource>();

        // Show-flavor patter line default (SPEC F116.3, STORY-308, PLAN T249) — the exact same
        // TryAdd-default-overridden-by-the-Host idiom as IContextPatterFactSource immediately above:
        // this project has no reference to GenWave.Orchestration (an L1 project one layer further
        // out) either, and never needs one — the seam lives in GenWave.Core so LlmCopyWriter can
        // depend on the contract alone. The Host's real GenWave.Orchestration.ShowFlavorLineGate
        // binding (StationOptionsServiceCollectionExtensions) overrides this with plain AddSingleton.
        services.TryAddSingleton<IShowFlavorLineSource, NoOpShowFlavorLineSource>();

        // TTS options — validated at startup; RenderBudgetSeconds must be positive.
        services
            .AddOptions<TtsOptions>()
            .Bind(configuration.GetSection(TtsOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Fallback chain options (SPEC F70.1, STORY-190, gh-#147) — registered unconditionally,
        // mirroring LlmOptions below: an empty chain (no Tts:Fallback:Profiles, no legacy
        // Tts:Fallback:Endpoint) just means FallbackTtsSynthesizer stays a pass-through to Kokoro.
        // IOptionsMonitor<TtsFallbackOptions> (not IOptions) is what FallbackTtsSynthesizer and
        // PiperHealthProbe read per call, so a live edit to the legacy Endpoint/Voice keys applies
        // without a restart. TtsFallbackOptionsValidator + ValidateOnStart is the gh-#147
        // fail-loudly gate: an unknown engine kind or a bad endpoint in Tts:Fallback:Profiles
        // kills the boot with a keyed message instead of skipping hops silently on air.
        services
            .AddOptions<TtsFallbackOptions>()
            .Bind(configuration.GetSection(TtsFallbackOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<TtsFallbackOptions>, TtsFallbackOptionsValidator>();

        // Per-kind engine override map (SPEC F70.3, STORY-191) — a raw JSON leaf, not
        // DataAnnotations-validated: malformed JSON (or an unknown kind/engine entry) degrades to
        // no per-kind overrides with a WARN (TtsEngineByKindProvider) rather than failing boot,
        // mirroring Tts:Corrections' own operator-data discipline below.
        services
            .AddOptions<TtsEngineByKindOptions>()
            .Bind(configuration.GetSection(TtsEngineByKindOptions.Section));
        services.AddSingleton<TtsEngineByKindProvider>();

        // LLM options — registered unconditionally (SPEC F34.2); an empty Llm:Endpoint just means
        // LlmCopyWriter stays disabled. IOptionsMonitor<LlmOptions> (not IOptions) is what
        // LlmCopyWriter reads per render, so a live edit to Llm:Endpoint/Model/TimeoutSeconds/
        // MaxCopyChars applies without a restart (F36.2). DegradationPin (SPEC F69.3) rides the
        // same options class/section — it is one more Llm:* leaf, not a separate config surface.
        services
            .AddOptions<LlmOptions>()
            .Bind(configuration.GetSection(LlmOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // gh-#253: the live Llm:MaxCopyChars seam the patter-duration estimator's cold tier reads
        // (ICopyBoundsProvider lives in GenWave.Abstractions; LlmOptions lives HERE, so this project
        // owns the adapter). TryAdd so a host or test that binds its own bounds source wins.
        services.TryAddSingleton<ICopyBoundsProvider, OptionsMonitorCopyBoundsProvider>();

        // Degradation thresholds (SPEC F69.2, STORY-188) — deployment-tunable, not allowlisted
        // (see DegradationOptions' own remarks for why). ValidateOnStart mirrors every other
        // options class in this method.
        services
            .AddOptions<DegradationOptions>()
            .Bind(configuration.GetSection(DegradationOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Injected clock for DegradationController's cooldown math (no DateTime.Now anywhere in
        // this feature) — TryAdd so a host or test that already registers its own TimeProvider wins.
        services.TryAddSingleton(TimeProvider.System);

        // Operator pronunciation corrections (SPEC F68.5, STORY-185) — a raw JSON leaf, not
        // DataAnnotations-validated: malformed JSON degrades to no corrections with a WARN
        // (SpeechCorrectionProvider) rather than failing boot, since Tts:Corrections is
        // operator-authored data, not deployment topology.
        services
            .AddOptions<TtsCorrectionsOptions>()
            .Bind(configuration.GetSection(TtsCorrectionsOptions.Section));
        services.AddSingleton<SpeechCorrectionProvider>();

        // Card-corrections half of the F71.7 merge seam (STORY-193): a bounded-TTL cache over the
        // active persona's card, resolved through the Host-provided IActivePersonaAccessor (the
        // same seam LlmCopyWriter reads for prompt assembly) — see its own remarks for exactly why
        // a TTL, not an OnChange subscription, is the honest mechanism at this layer.
        services.AddSingleton<ActivePersonaCorrectionsCache>();

        // Station pronunciation rules (SPEC F97.3, STORY-253) — a raw JSON leaf, mirroring
        // Tts:Corrections above exactly: malformed JSON degrades to no rules with a WARN
        // (PronunciationRuleProvider) rather than failing boot.
        services
            .AddOptions<TtsPronunciationsOptions>()
            .Bind(configuration.GetSection(TtsPronunciationsOptions.Section));
        services.AddSingleton<PronunciationRuleProvider>();

        // Card-pronunciations half of the F97.3/F97.4 merge seam — the pronunciation-rule sibling
        // of ActivePersonaCorrectionsCache just above, same TTL mechanism, same accessor seam.
        services.AddSingleton<ActivePersonaPronunciationRulesCache>();

        // Card speaking-pace seam (SPEC F98.1-F98.3, STORY-255, PLAN T140) — the third sibling in
        // this trio, same TTL mechanism, same accessor seam; validates VoiceSpec.Pace at refresh
        // time (TtsPace.Clamp, WarnOnce-latched) rather than at the engine.
        services.AddSingleton<ActivePersonaPaceCache>();

        // Fired-rule observability (SPEC F68.7, STORY-186 AC3) — one counter set for the process
        // lifetime, incremented by NormalizingTtsSynthesizer and read by GET /api/tts/corrections-stats.
        services.AddSingleton<CorrectionsFiredStats>();

        // Fired-rule observability for pronunciation rules (SPEC F97.5, F100.1, STORY-253 AC4) — the
        // pronunciation-rule sibling of CorrectionsFiredStats immediately above. PronunciationRuleHitReporter
        // is resolved automatically as an ordinary constructor dependency by KokoroTtsSynthesizer's and
        // KokoroFallbackRenderer's own AddHttpClient<T> registrations below — no explicit factory needed
        // here, only the two singletons themselves.
        services.AddSingleton<PronunciationRuleHitStats>();
        services.AddSingleton<PronunciationRuleHitReporter>();

        // Dependency health probes (SPEC F70.2, STORY-187): the verdict store lives here — TTS
        // owns the read seam its own render-time fallback logic (T34) will consume — registered
        // concretely once and exposed under IDependencyHealth, mirroring the
        // NormalizingTtsSynthesizer/LlmCopyWriter "one instance, every interface" shape below.
        // The probes themselves live here too (Ollama/Kokoro endpoints are Tts:Endpoint/
        // Llm:Endpoint, already this project's own options) with the same no-BaseAddress typed
        // HttpClient discipline as KokoroTtsSynthesizer/KokoroVoiceLister — each is added to the
        // IDependencyProbe collection declaratively; the Host's DependencyHealthProbeService
        // (GenWave.Host) resolves IEnumerable<IDependencyProbe> and drives the cadence, wholly
        // unaware of which probes exist.
        services.AddSingleton<DependencyHealthStore>();
        services.AddSingleton<IDependencyHealth>(sp => sp.GetRequiredService<DependencyHealthStore>());

        services.AddHttpClient<OllamaHealthProbe>();
        services.AddSingleton<IDependencyProbe>(sp => sp.GetRequiredService<OllamaHealthProbe>());

        services.AddHttpClient<KokoroHealthProbe>();
        services.AddSingleton<IDependencyProbe>(sp => sp.GetRequiredService<KokoroHealthProbe>());

        // Piper fallback probe (SPEC F70.1, F70.2, STORY-190) — third IDependencyProbe entry;
        // DependencyHealthProber (and the Host's DependencyHealthProbeService driving it) needed no
        // change at all to pick this up.
        services.AddHttpClient<PiperHealthProbe>();
        services.AddSingleton<IDependencyProbe>(sp => sp.GetRequiredService<PiperHealthProbe>());

        // TTS wiring: ISegmentCopyWriter is the copy-writer seam (SPEC F34.1) TtsSegmentSource
        // consumes. TemplateCopyWriter is registered concretely as the terminal fallback rung;
        // LlmCopyWriter (SPEC F34.2-F34.5) authors LeadIn/BackAnnounce from the configured LLM and
        // degrades to TemplateCopyWriter on any miss, including a disabled (empty Llm:Endpoint)
        // writer. LlmCopyStatusHolder is the in-memory last-attempt record GET /api/status
        // (STORY-125) reads, and now also DegradationController's drop signal (SPEC F69.2).
        //
        // ISegmentCopyWriter itself resolves to DegradationGatedCopyWriter (SPEC F69.1, F69.4,
        // STORY-188), NOT LlmCopyWriter directly — the one and only place degradation mode gates a
        // render (see its own remarks). IPersonaPreviewWriter stays bound straight to LlmCopyWriter,
        // unchanged, so operator-explicit previews never pass through the gate at all.
        services
            .AddSingleton<PatterTemplateRenderer>()
            .AddSingleton<TemplateCopyWriter>()
            .AddSingleton<LlmCopyStatusHolder>()
            .AddSingleton<DegradationController>()
            // IDegradationModeReader (SPEC F73.1, STORY-196, T41): the same DegradationController
            // singleton above, exposed under the narrow read-only seam LlmCopyWriter depends on for
            // its LlmCallRing mode stamp — mirrors the IDependencyHealth/DependencyHealthStore "one
            // instance, multiple interfaces" shape a few lines below.
            .AddSingleton<IDegradationModeReader>(sp => sp.GetRequiredService<DegradationController>())
            // LlmCallRing (SPEC F73.1-F73.4, STORY-196, T41): the admin call inspector's in-memory
            // ring — GET /api/llm-calls (GenWave.Host) reads the SAME singleton LlmCopyWriter
            // records into. No persistence dependency of any kind (F73.3) — see its own remarks.
            .AddSingleton<LlmCallRing>()
            // LlmCopyWriter also consumes IActivePersonaAccessor (a host-registered seam) —
            // resolved per LLM render only, composing the active persona's backstory + style into
            // the prompt (SPEC F35.2/F35.3). Registered concretely ONCE and exposed under BOTH
            // seams it implements — the on-air copy-writer chain (always-succeeds, template
            // fallback) and the preview-only IPersonaPreviewWriter (never silently degrades, SPEC
            // F35.6/T7) — so the persona preview endpoint reuses the exact same prompt-building/
            // hygiene instance the feeder does, never a second parallel writer.
            .AddSingleton<LlmCopyWriter>()
            .AddSingleton<IPersonaPreviewWriter>(sp => sp.GetRequiredService<LlmCopyWriter>())
            .AddSingleton<DegradationGatedCopyWriter>()
            .AddSingleton<ISegmentCopyWriter>(sp => sp.GetRequiredService<DegradationGatedCopyWriter>())
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

        // Fallback-chain hop renderers (SPEC F70.1, STORY-190, gh-#147) — one per engine kind,
        // exposed under IFallbackProfileRenderer for FallbackTtsSynthesizer's by-kind lookup (the
        // same AddHttpClient-then-AddSingleton shape as the probes above). Same no-BaseAddress
        // discipline: each hop's endpoint arrives per call from its TtsFallbackProfile.
        services.AddHttpClient<PiperTtsSynthesizer>();
        services.AddSingleton<IFallbackProfileRenderer>(sp => sp.GetRequiredService<PiperTtsSynthesizer>());
        services.AddHttpClient<KokoroFallbackRenderer>();
        services.AddSingleton<IFallbackProfileRenderer>(sp => sp.GetRequiredService<KokoroFallbackRenderer>());

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
            // FallbackTtsSynthesizer (SPEC F70.1, F70.4, STORY-190, gh-#147) sits BELOW
            // NormalizingTtsSynthesizer, executing the ordered fallback chain — Kokoro (primary)
            // first, then each configured hop — see its own remarks for the routing rule.
            // Registered concretely once; nothing else in this project resolves it directly.
            .AddSingleton<FallbackTtsSynthesizer>(sp =>
                new FallbackTtsSynthesizer(
                    sp.GetRequiredService<KokoroTtsSynthesizer>(),
                    sp.GetServices<IFallbackProfileRenderer>(),
                    sp.GetRequiredService<IDependencyHealth>(),
                    sp.GetRequiredService<IOptionsMonitor<TtsFallbackOptions>>(),
                    sp.GetRequiredService<ILogger<FallbackTtsSynthesizer>>(),
                    sp.GetRequiredService<TtsEngineByKindProvider>()))
            // The typed HttpClient factory registers KokoroTtsSynthesizer as transient; the
            // singleton every caller (TtsSegmentSource, SafeSegmentAuthor, TtsPreviewController)
            // actually resolves is NormalizingTtsSynthesizer (SPEC F68.1, STORY-185) decorating
            // FallbackTtsSynthesizer (T34) decorating KokoroTtsSynthesizer/PiperTtsSynthesizer —
            // the single Normalize call site sits here, not in any of those callers, and runs
            // exactly once whichever engine ultimately renders. Registered concretely ONCE and
            // exposed under BOTH seams it implements (mirrors LlmCopyWriter's
            // ISegmentCopyWriter/IPersonaPreviewWriter split) — the on-air ITtsSynthesizer and the
            // preview-only ISpeechNormalizationPreview (SPEC F68.6, STORY-186 AC2) — so the admin
            // normalize-preview endpoint reuses the exact same normalization instance the feeder
            // does, never a second parallel one.
            .AddSingleton<NormalizingTtsSynthesizer>(sp =>
                new NormalizingTtsSynthesizer(
                    sp.GetRequiredService<FallbackTtsSynthesizer>(),
                    sp.GetRequiredService<SpeechCorrectionProvider>(),
                    sp.GetRequiredService<ActivePersonaCorrectionsCache>(),
                    sp.GetRequiredService<CorrectionsFiredStats>(),
                    sp.GetRequiredService<ILogger<NormalizingTtsSynthesizer>>()))
            .AddSingleton<ITtsSynthesizer>(sp => sp.GetRequiredService<NormalizingTtsSynthesizer>())
            .AddSingleton<ISpeechNormalizationPreview>(sp => sp.GetRequiredService<NormalizingTtsSynthesizer>());

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
