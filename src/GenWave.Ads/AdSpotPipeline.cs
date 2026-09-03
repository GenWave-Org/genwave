using Microsoft.Extensions.Logging;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Ads;

/// <summary>
/// SPEC F158.2 (STORY-388, PLAN T396) — tries every registered <see cref="IAdSpotSource"/> in
/// REGISTRATION ORDER (the <c>ContextPipeline</c>/<c>IContextProvider</c> shape) and takes the first
/// non-null answer; a source that throws is WARN-logged (naming the source's own type — this
/// interface carries no <c>Key</c> the way <c>IContextProvider</c> does) and skipped, never faulting
/// the pipeline (F156.4's skip-never-down posture applies at runtime too). Home's own
/// <see cref="LibraryAdSpotSource"/> registers LAST via <see cref="AdsServiceCollectionExtensions.AddGenWaveAds"/>
/// — the floor — so a plugin source (Business real ads, someday) wins the break without ever
/// replacing anything.
///
/// <para>
/// <b>The locator jail (PLAN T390 review carry-forward 2).</b> Every candidate's
/// <c>MediaItem.Locator</c> is canonicalized (<see cref="Path.GetFullPath(string)"/>, collapsing any
/// <c>..</c>/<c>.</c> traversal) and checked against <see cref="AdSpotLocatorRoots"/> before this
/// pipeline ever returns it — an in-process plugin source is already full-trust (SPEC F156.3's own
/// AssemblyLoadContext isolation is about code identity, not path containment), so this is
/// deliberate defense-in-depth, not a substitute for that trust boundary: a locator outside both
/// roots is treated exactly like a null answer from that source — one WARN, fall through to the next
/// source — rather than ever reaching the caller. A source that only ever answers from the
/// authored/media roots (every shipped source, today and after T401) never trips it.
/// </para>
///
/// <para>
/// <b>Implements <see cref="IAdSpotVend"/> (SPEC F158.2/F158.3, PLAN T397).</b> This is the ONE
/// implementation of that seam — <c>AddGenWaveAds</c> registers it as
/// <see cref="IAdSpotVend"/> too, alongside its own concrete-type registration, so
/// <c>GenWave.Orchestration</c>'s <c>Orchestrator</c> can drain the whole fan-out through a single
/// method without ever referencing this project (see <see cref="IAdSpotVend"/>'s own remarks for the
/// full L10 acyclicity argument). <see cref="GetNextSpotAsync"/>'s own signature already matches that
/// interface's shape exactly — no adapter needed.
/// </para>
/// </summary>
public sealed class AdSpotPipeline : IAdSpotVend
{
    readonly IReadOnlyList<IAdSpotSource> sources;
    readonly IReadOnlyList<string> canonicalRoots;
    readonly ILogger<AdSpotPipeline> logger;

    public AdSpotPipeline(IEnumerable<IAdSpotSource> sources, AdSpotLocatorRoots roots, ILogger<AdSpotPipeline> logger)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(logger);

        this.sources = sources.ToList();
        canonicalRoots = [Canonicalize(roots.MediaRoot), Canonicalize(roots.AuthoredRoot)];
        this.logger = logger;
    }

    /// <summary>
    /// The next spot to air, or <see langword="null"/> when every registered source answered null (an
    /// empty pipeline is a normal day, F158.3 — logging that is the drain's own job, not this
    /// method's: see <c>PLAN T397</c>).
    /// </summary>
    public async Task<MediaItem?> GetNextSpotAsync(CancellationToken ct)
    {
        foreach (var source in sources)
        {
            MediaItem? candidate;
            try
            {
                candidate = await source.GetNextSpotAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // Caller cancellation, not a source fault — never skip-never-down input.
            }
            catch (Exception ex)
            {
                // F158.2/F156.4: one WARN naming the source's type and the exception's TYPE — never
                // ex.Message, the same "never echo third-party-authored text into a log line"
                // discipline ContextPipeline's own LogSkipOnce already holds for context providers.
                logger.LogWarning(
                    ex, "Ad spot source {SourceType} threw {ExceptionType}; skipping to the next source",
                    source.GetType().Name, ex.GetType().Name);
                continue;
            }

            if (candidate is null)
                continue; // This source has nothing this break — always a legal answer (F158.1).

            if (!IsWithinConfiguredRoots(candidate.Locator))
            {
                logger.LogWarning(
                    "Ad spot source {SourceType} vended a locator outside the configured media/authored " +
                    "roots; skipping to the next source (PLAN T390 review carry-forward 2)",
                    source.GetType().Name);
                continue;
            }

            return candidate;
        }

        return null;
    }

    bool IsWithinConfiguredRoots(string locator)
    {
        string canonical;
        try
        {
            canonical = Path.GetFullPath(locator);
        }
        catch (Exception)
        {
            // A malformed locator (empty string, invalid characters) fails the jail closed —
            // never reaches the caller.
            return false;
        }

        foreach (var root in canonicalRoots)
        {
            if (canonical == root || canonical.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    static string Canonicalize(string root) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
}
