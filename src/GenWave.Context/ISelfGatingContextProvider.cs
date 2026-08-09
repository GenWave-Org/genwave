namespace GenWave.Context;

/// <summary>
/// Opt-in capability (F2 fix, T227 review) for an <see cref="Core.Abstractions.IContextProvider"/>
/// that can tell <see cref="ContextPipeline"/>, WITHOUT calling
/// <see cref="Core.Abstractions.IContextProvider.FetchAsync"/>, that it currently has nothing to
/// produce — e.g. <see cref="Weather.WeatherContextProvider"/>'s F108.1 fail-closed coordinate
/// check. The pipeline treats <see cref="IsAvailable"/> EXACTLY like the settings-driven
/// <c>ContextProviderSettings.Enabled</c> flag it already gates on — literally, both lanes, not just
/// one (a review-round fix: checking it in only <see cref="ContextPipeline.TickAsync"/> left
/// <see cref="ContextPipeline.TryTakeDuePatterFact"/> free to keep vending a provider's last-fetched
/// content for up to its own <see cref="Core.Domain.ContextContent.FreshUntil"/> after the provider
/// went unavailable, exactly the staleness this interface exists to prevent):
/// <see langword="false"/> means zero <see cref="Core.Abstractions.IContextProvider.FetchAsync"/>
/// calls this tick, zero patter vends, the provider's cached content cleared (so no third caller can
/// read stale content back either), and one edge-triggered
/// <see cref="Microsoft.Extensions.Logging.LogLevel.Information"/> line naming the provider (never
/// the reason — a provider that wants to name a specific cause, e.g. which config key is blank, does
/// that itself, in its own <c>FetchAsync</c>, for a caller that reaches it directly), then silence
/// for as long as it stays unavailable. The zero-fetch part is also what keeps
/// <c>ContextPipeline.EnsureFetchedAsync</c>'s own per-slot "fetch returned no content" line —
/// accurate for a GENUINE null reply from a real fetch attempt — from also firing, every slot,
/// forever, for a cause that was never actually a fetch attempt at all.
///
/// <para>
/// A provider that does not implement this interface is always treated as available — today's
/// unchanged pipeline behavior for every provider that has no cheap availability check to offer.
/// This interface is a logging/efficiency/freshness refinement, never a second source of truth: an
/// implementer's own <c>FetchAsync</c> MUST remain independently correct (return null, or throw) if
/// this property and the provider's underlying state ever briefly disagree — e.g. a race between two
/// separate reads of the same live, operator-editable config between this check and the fetch that
/// would have followed it. See <see cref="Weather.WeatherContextProvider"/>'s own remarks for that
/// defense-in-depth framing.
/// </para>
/// </summary>
interface ISelfGatingContextProvider
{
    /// <summary>Whether this provider can currently produce content — evaluated fresh on every call
    /// (never cached), synchronous, and free of I/O (no network, no disk), so the pipeline can afford
    /// to call it on every tick at no real cost.</summary>
    bool IsAvailable { get; }
}
