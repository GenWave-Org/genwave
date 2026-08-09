using GenWave.Context;
using GenWave.Core.Abstractions;
using GenWave.Host.Options;

namespace GenWave.Host.Playout;

/// <summary>
/// The Host's composition of the F107 context seam (STORY-297, PLAN T226) — and, additively, the
/// F110.1/F110.3 clock-anchored imaging settings override (STORY-301/302, PLAN T230), which rides
/// the SAME wall-clock actor this method wires.
/// <c>GenWave.Context.ContextServiceCollectionExtensions.AddGenWaveContext</c> registers the
/// framework-free defaults — the typed HTTP clients, the NoOp
/// <see cref="IStationLocationProvider"/>/<see cref="IContextCacheRootProvider"/> bindings, and the
/// two <see cref="IContextProvider"/> entries (Weather, History). This method is what makes them
/// REAL: the live Options-backed shims, the <see cref="ContextPipeline"/> singleton, its
/// <see cref="IContextPatterFactSource"/> override, and the one wall-clock actor that drives it.
///
/// <para>
/// <b>Ordering — must run after <c>.AddGenWaveOrchestration()</c> and <c>.AddGenWaveTts()</c> in
/// Program.cs</b> (needs <see cref="SpeechDeferralQueue"/> and
/// <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/>&lt;<c>TtsOptions</c>&gt;
/// respectively — both registered by those calls). Calls
/// <c>services.AddGenWaveContext()</c> FIRST, internally, so every override below is a plain
/// <c>AddSingleton</c> registered AFTER that method's own <c>TryAddSingleton</c> defaults — "last
/// registration wins" for a single (non-enumerable) resolution, the exact override-after-the-default
/// idiom <c>AddGenWavePersonaRanking</c>/<c>AddGenWaveRequestFulfillment</c> already document one
/// call over.
/// </para>
///
/// <para>
/// <b><see cref="IContextPatterFactSource"/> (review ruling): plain <c>AddSingleton</c>, never
/// <c>TryAdd</c>.</b> <c>GenWave.Tts.TtsServiceCollectionExtensions</c> already registered
/// <c>TryAddSingleton&lt;IContextPatterFactSource, NoOpContextPatterFactSource&gt;</c> — a
/// <c>TryAdd</c> here would silently lose to that earlier default (TryAdd only adds when nothing is
/// registered yet), leaving <c>LlmCopyWriter</c>'s patter lane permanently reading "no fact due"
/// with a green test suite and zero facts ever airing. Plain <c>AddSingleton</c> is what actually
/// wins the "last registration wins" resolution described above — this method MUST also run after
/// <c>.AddGenWaveTts()</c> for that reason too.
/// </para>
/// </summary>
static class ContextHostServiceCollectionExtensions
{
    public static IServiceCollection AddGenWaveContextHost(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddGenWaveContext();

        // Real Options-backed bindings — override GenWave.Context's own TryAddSingleton NoOp
        // defaults for the two seams it registers, and add the one it does NOT (Context:{Key}:* has
        // no default binding anywhere; ContextPipeline requires a real IContextSettingsProvider).
        services.AddSingleton<IStationLocationProvider, OptionsMonitorStationLocationProvider>();
        services.AddSingleton<IContextCacheRootProvider, OptionsMonitorContextCacheRootProvider>();
        services.AddSingleton<IContextSettingsProvider, ConfigurationContextSettingsProvider>();

        // SPEC F110.1/F110.3 (PLAN T230): overrides AddGenWaveOrchestration's own
        // TryAddSingleton<IStationImagingSettingsProvider, NoOpStationImagingSettingsProvider>
        // default — same "override after the default" idiom the three bindings above already use.
        // ClockAnchoredImagingProducer itself needs no override here (no NoOp-replacement semantics —
        // that method's own AddSingleton is the only registration it ever gets).
        services.AddSingleton<IStationImagingSettingsProvider, OptionsMonitorStationImagingProvider>();

        // The ticker's own polling cadence (see ContextTickerOptions' own remarks) — deployment
        // tuning, deliberately absent from StationSettingsAllowlist.
        services
            .AddOptions<ContextTickerOptions>()
            .Bind(configuration.GetSection(ContextTickerOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // The pipeline itself — one instance shared by the ticker (TickAsync) and the patter lane
        // (TryTakeDuePatterFact, via the IContextPatterFactSource override below). Resolves its
        // IEnumerable<IContextProvider> from whatever AddGenWaveContext registered above (Weather +
        // History today; any future provider joins the same fan-out with no change here).
        services.AddSingleton<ContextPipeline>();

        // Ruling #1 — see this class's own remarks: AddSingleton, never TryAdd.
        services.AddSingleton<IContextPatterFactSource>(sp => sp.GetRequiredService<ContextPipeline>());

        // The one wall-clock actor (SPEC F107.3, and additively F110.1/F110.3): advances the pipeline
        // AND calls ClockAnchoredImagingProducer.Produce() each tick, feeding the SAME
        // SpeechDeferralQueue AddGenWaveOrchestration registered (both that producer and the queue
        // come from AddGenWaveOrchestration, called earlier in Program.cs) — see
        // ContextTickerService's own remarks for why it lives under GenWave.Host.Playout, not the
        // reserved GenWave.Host.Context.
        services.AddHostedService<ContextTickerService>();

        return services;
    }
}
