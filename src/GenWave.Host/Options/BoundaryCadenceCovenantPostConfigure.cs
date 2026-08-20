namespace GenWave.Host.Options;

using GenWave.Orchestration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Applies the SPEC F142 boundary cadence covenant's clamp-up (STORY-356, PLAN T327, closes
/// gh-#300) to <c>Station:BoundaryBias:LookaheadMinutes</c> — the pure rule itself lives in
/// <see cref="BoundaryCadenceCovenant"/>; this type is only the Host-side binding plus the one
/// WARN logged on clamp.
///
/// <para>
/// The repo's FIRST <see cref="IPostConfigureOptions{TOptions}"/>. The clamp does NOT belong in
/// <see cref="StationOptionsValidator"/> (an <see cref="IValidateOptions{TOptions}"/>): every guard
/// there is a pure predicate over an already-bound <see cref="StationOptions"/>, and
/// <c>Validate</c> is a predicate by contract — mutating a field from inside a method whose entire
/// return type is pass/fail is the wrong altitude for that mutation to live at.
/// <c>IPostConfigureOptions&lt;T&gt;.PostConfigure</c> is where the framework expects a bound
/// options instance to still be adjusted, and — this being the reason it earns a whole new
/// interface implementation rather than just a second <c>IValidateOptions</c> registration — it
/// runs BEFORE every <see cref="IValidateOptions{TOptions}"/> (this project's
/// <see cref="StationOptionsValidator"/> included) on EVERY <c>OptionsFactory&lt;StationOptions&gt;.Create</c>
/// call, not just once at process boot. That also strengthens the
/// <see cref="IOptionsMonitor{TOptions}"/> file-reload path: a config-provider reload that
/// reintroduces a covenant violation gets re-clamped and re-warned on that very reload, not just at
/// the process's first bind.
/// </para>
///
/// <para>
/// <paramref name="worstCasePullGap"/> is <c>PlayoutFeederService.PullInterval</c>
/// (<c>GenWave.Host.Playout</c>) — passed in rather than read directly, and registered from
/// <c>Program.cs</c> rather than alongside <see cref="StationOptionsValidator"/> in
/// <c>StationOptionsServiceCollectionExtensions</c> (T327 review A6): <c>Playout</c> already depends
/// on <c>Options</c> (this namespace) the other way, so this type reaching into <c>Playout</c>
/// itself — from ANY file in this namespace, not just this one — would open the cycle gh-#445's
/// namespace-cycle fitness law forbids. <c>Program.cs</c>, the composition root, is the one place
/// that already sees both without creating one.
/// </para>
/// </summary>
public sealed class BoundaryCadenceCovenantPostConfigure(
    ILogger<BoundaryCadenceCovenantPostConfigure> logger, TimeSpan worstCasePullGap)
    : IPostConfigureOptions<StationOptions>
{
    static readonly TimeSpan Grain = TimeSpan.FromMinutes(1);

    public void PostConfigure(string? name, StationOptions options)
    {
        // A negative LookaheadMinutes is StationOptionsValidator's own error to raise (Validate runs
        // AFTER every PostConfigure) — clamping it here first would silently repair the very
        // misconfiguration that guard exists to reject, so it never gets the chance to fire.
        if (options.BoundaryBias.LookaheadMinutes < 0)
            return;

        var covenant = BoundaryCadenceCovenant.Evaluate(
            configuredLookahead: TimeSpan.FromMinutes(options.BoundaryBias.LookaheadMinutes),
            signOffLeadTime: Orchestrator.SignOffLeadTime,
            worstCasePullGap: worstCasePullGap,
            grain: Grain);

        // Gated on WasClamped, not on WarningMessage's presence (T327 review A1) — behavior must
        // never hang off a log string; WarningMessage is only what gets logged once this is true.
        if (covenant.WasClamped)
        {
            // Derived from ticks, not a second TimeSpan.TotalMinutes expression of the same fact
            // (T327 review A5) — BoundLookahead and Grain are both already whole-minute values by
            // construction (BoundaryCadenceCovenant.Evaluate's own contract), so this is exact.
            options.BoundaryBias.LookaheadMinutes = (int)(covenant.BoundLookahead.Ticks / Grain.Ticks);
            logger.LogWarning("{CovenantWarning}", covenant.WarningMessage);
        }
    }
}
