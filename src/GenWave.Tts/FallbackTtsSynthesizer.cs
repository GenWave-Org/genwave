namespace GenWave.Tts;

using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// Executes the ordered TTS fallback resiliency chain (SPEC F70.1, F70.4, STORY-190, gh-#147):
/// primary (Kokoro) first, then each configured <see cref="TtsFallbackProfile"/> hop in order,
/// until one renders. Sits BELOW <see cref="NormalizingTtsSynthesizer"/> — that decorator wraps
/// THIS one, never the other way round — so <see cref="SpeechText.Normalize"/> runs exactly once,
/// before this decorator ever sees the text: every engine receives identical already-normalized
/// copy, and every render flows through the exact same <see cref="TtsSegmentSource"/>
/// measure/cue/cache pipeline one seam up (F70.4) — nothing downstream of
/// <see cref="ITtsSynthesizer"/> needs to know which engine actually rendered a clip.
///
/// The chain is resolved fresh per render from <see cref="TtsFallbackChain.Resolve"/> (see its
/// remarks for the Profiles-vs-legacy-flat-keys precedence). An EMPTY chain — no profiles, no
/// legacy <c>Tts:Fallback:Endpoint</c> — makes this decorator a transparent pass-through to the
/// primary: no health read, no retry, no second exception in the log; a Kokoro failure propagates
/// exactly as it did before any fallback feature existed (F70.1's "empty = zero behavior change").
/// This short-circuit runs BEFORE the per-kind lookup below, so an operator cannot pin a kind to
/// an engine while no chain exists — there is nothing to route to.
///
/// Routing rule (F70.1, F70.2): reads <see cref="IDependencyHealth"/>'s CACHED Kokoro verdict —
/// never probes here, so a render-time decision costs zero network round trips beyond whichever
/// synthesis calls it makes (STORY-187 AC2, "no health check executes inside the render window").
/// A cached <c>unhealthy</c> verdict skips the primary and starts the chain at hop 1; a
/// <c>healthy</c> verdict, or no verdict yet (the brief startup window before the first probe
/// cycle completes), tries the primary first and walks the chain on failure. Per hop, an operator
/// may opt into the same cached-verdict gate (<see cref="TtsFallbackProfile.SkipWhenUnhealthy"/>,
/// default off — the shipped single-hop default always attempts its Piper hop, exactly the
/// pre-gh-#147 behavior) and a per-hop render budget
/// (<see cref="TtsFallbackProfile.TimeoutSeconds"/>, surfacing as an ordinary hop failure).
///
/// Total chain failure — every attempted engine threw — rethrows whichever exception was actually
/// last attempted, original stack preserved (<see cref="ExceptionDispatchInfo"/>); this decorator
/// adds no exception wrapping of its own. <see cref="TtsSegmentSource"/>'s existing render-ahead
/// catch turns it into a loud <c>LogWarning</c> and a skipped segment; music keeps playing
/// (STORY-190 AC4 — the never-silent posture). Each inter-engine transition logs the same
/// greppable WARN class as the original single hop ("Kokoro render failed; retrying once via
/// Piper fallback" and "routing render straight to Piper fallback" both survive verbatim as
/// substrings on the default chain — Loki dashboards watch these).
///
/// Per-kind override interplay (SPEC F70.3, STORY-191): an optional <c>Tts:EngineByKind</c> map
/// lets an operator PIN a speech kind to an engine. A pin to a non-primary engine routes the
/// render to the FIRST chain hop of that engine kind, first — it decides what is tried first,
/// nothing more. Resilience stays symmetric: if the pinned hop throws, the primary is retried
/// (with no health read, mirroring the pre-gh-#147 pinned path), then any remaining hops in chain
/// order. A pin to <c>"kokoro"</c>, an unmapped kind, or a pinned engine no chain hop runs all
/// take the untouched health-based path — an empty map stays byte-identical to pre-feature
/// routing (F70.3 AC3).
/// </summary>
public sealed class FallbackTtsSynthesizer(
    ITtsSynthesizer primary,
    IEnumerable<IFallbackProfileRenderer> hopRenderers,
    IDependencyHealth health,
    IOptionsMonitor<TtsFallbackOptions> fallbackOptions,
    ILogger<FallbackTtsSynthesizer> logger,
    TtsEngineByKindProvider? engineOverrides = null) : ITtsSynthesizer
{
    readonly IReadOnlyDictionary<string, IFallbackProfileRenderer> renderers =
        hopRenderers.ToDictionary(r => r.Engine, StringComparer.OrdinalIgnoreCase);

    public Task<string> SynthesizeAsync(string text, string voice, CancellationToken ct) =>
        SynthesizeAsync(new TtsRenderContext(text, voice, Kind: null), ct);

    /// <summary>
    /// Kind-aware overload (SPEC F70.3, STORY-191) — see the class remarks for the full per-kind
    /// interplay with the health-based chain execution.
    /// </summary>
    public async Task<string> SynthesizeAsync(TtsRenderContext context, CancellationToken ct)
    {
        var chain = TtsFallbackChain.Resolve(fallbackOptions.CurrentValue);
        if (chain.IsEmpty)
        {
            // No fallback configured — identical to pre-T34 behavior (F70.1). The per-kind map is
            // moot when there is no chain to route to.
            return await primary.SynthesizeAsync(context.Text, context.Voice, ct);
        }

        var mappedEngine = context.Kind is { } kind
            ? (engineOverrides?.Current ?? TtsEngineOverrideMap.Empty).Resolve(kind)
            : null;

        // A "kokoro" pin is legal but not a distinct path (F70.3): the primary is already tried
        // first on the health-based path below. Any other pinned engine targets the first chain
        // hop of that kind; no such hop (an operator pinned an engine their chain doesn't run)
        // falls through to the same default path.
        var pinnedIndex = mappedEngine is not null
            && !string.Equals(mappedEngine, DependencyNames.Kokoro, StringComparison.OrdinalIgnoreCase)
                ? chain.IndexOfFirstEngine(mappedEngine)
                : -1;

        if (pinnedIndex < 0)
            return await ExecuteChainAsync(chain, context, skipHopIndex: -1, consultPrimaryHealth: true, ct);

        // Forward-direction pre-emption (F70.3): go straight to the pinned hop without consulting
        // the cached Kokoro verdict. Resilience stays symmetric — a pinned-hop failure still falls
        // back to the primary (again without a health read, exactly the pre-gh-#147 pinned path),
        // then to whatever remains of the chain.
        var pinned = chain.Hops[pinnedIndex];
        try
        {
            return await RenderHopAsync(pinned, context, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "{Engine} render failed for kind {Kind} mapped by Tts:EngineByKind; retrying once via Kokoro",
                DisplayName(pinned.Engine),
                context.Kind);
        }

        return await ExecuteChainAsync(chain, context, skipHopIndex: pinnedIndex, consultPrimaryHealth: false, ct);
    }

    /// <summary>
    /// The primary-then-hops sequence. <paramref name="skipHopIndex"/> removes an already-attempted
    /// pinned hop from the walk; <paramref name="consultPrimaryHealth"/> is false on the pinned
    /// path, where the primary is the retry of last resort regardless of its cached verdict.
    /// </summary>
    async Task<string> ExecuteChainAsync(
        TtsFallbackChain chain,
        TtsRenderContext context,
        int skipHopIndex,
        bool consultPrimaryHealth,
        CancellationToken ct)
    {
        ExceptionDispatchInfo? lastFailure = null;
        var firstHopIndex = NextHopIndex(chain, fromExclusive: -1, skipHopIndex);

        var verdict = consultPrimaryHealth ? health.GetVerdict(DependencyNames.Kokoro) : null;
        if (verdict is { Healthy: false } && firstHopIndex >= 0)
        {
            logger.LogWarning(
                "Kokoro cached verdict is unhealthy ({Reason}); routing render straight to {Engine} fallback",
                verdict.Reason,
                DisplayName(chain.Hops[firstHopIndex].Engine));
        }
        else
        {
            try
            {
                return await primary.SynthesizeAsync(context.Text, context.Voice, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception) when (firstHopIndex < 0)
            {
                // Nothing left to retry on (the pinned hop already consumed the whole chain) —
                // propagate unchanged, the same both-down shape as ever.
                throw;
            }
            catch (Exception ex)
            {
                lastFailure = ExceptionDispatchInfo.Capture(ex);
                logger.LogWarning(
                    ex,
                    "Kokoro render failed; retrying once via {Engine} fallback",
                    DisplayName(chain.Hops[firstHopIndex].Engine));
            }
        }

        for (var i = 0; i < chain.Hops.Count; i++)
        {
            if (i == skipHopIndex)
                continue;

            var hop = chain.Hops[i];
            if (hop.SkipWhenUnhealthy && health.GetVerdict(hop.Engine) is { Healthy: false } hopVerdict)
            {
                logger.LogWarning(
                    "Skipping {Engine} fallback hop {Hop} of {Total}: cached verdict is unhealthy ({Reason})",
                    DisplayName(hop.Engine),
                    i + 1,
                    chain.Hops.Count,
                    hopVerdict.Reason);
                continue;
            }

            try
            {
                return await RenderHopAsync(hop, context, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastFailure = ExceptionDispatchInfo.Capture(ex);
                var next = NextHopIndex(chain, fromExclusive: i, skipHopIndex);
                if (next >= 0)
                {
                    logger.LogWarning(
                        ex,
                        "{Engine} render failed; retrying once via {Next} fallback",
                        DisplayName(hop.Engine),
                        DisplayName(chain.Hops[next].Engine));
                }
            }
        }

        lastFailure?.Throw();

        // Every attempt was gated off by a cached-unhealthy verdict without a single render being
        // tried — still fail loudly (never-silent posture): TtsSegmentSource's render-ahead catch
        // logs it and skips the segment; music keeps playing. Unreachable on the shipped default
        // chain (its one hop never opts into SkipWhenUnhealthy).
        throw new InvalidOperationException(
            "TTS fallback chain exhausted without attempting a render: every engine was skipped by a cached-unhealthy verdict");
    }

    async Task<string> RenderHopAsync(TtsFallbackProfile hop, TtsRenderContext context, CancellationToken ct)
    {
        if (!renderers.TryGetValue(hop.Engine, out var renderer))
        {
            // Startup validation (TtsFallbackOptionsValidator) makes this unreachable for bound
            // config; it guards hand-built options handed in by tests or tools.
            throw new InvalidOperationException(
                $"no renderer is registered for TTS fallback engine '{hop.Engine}'");
        }

        if (hop.TimeoutSeconds is not { } budgetSeconds)
            return await renderer.RenderAsync(hop, context.Text, context.Voice, ct);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(budgetSeconds));
        try
        {
            return await renderer.RenderAsync(hop, context.Text, context.Voice, cts.Token);
        }
        catch (OperationCanceledException oce) when (!ct.IsCancellationRequested)
        {
            // The hop's own budget elapsed — surface as an ordinary hop failure so the chain
            // moves on; a real caller cancellation rethrows as OperationCanceledException above.
            throw new TimeoutException(
                $"{hop.Engine} fallback hop at {hop.Endpoint} exceeded its {budgetSeconds}s render budget",
                oce);
        }
    }

    static int NextHopIndex(TtsFallbackChain chain, int fromExclusive, int skipHopIndex)
    {
        for (var i = fromExclusive + 1; i < chain.Hops.Count; i++)
        {
            if (i != skipHopIndex)
                return i;
        }

        return -1;
    }

    // Log-display casing only ("piper" → "Piper") — keeps the original warn text byte-stable on
    // the default chain for the Loki dashboards that grep it.
    static string DisplayName(string engine) =>
        engine.Length == 0 ? engine : char.ToUpperInvariant(engine[0]) + engine[1..];
}
