namespace GenWave.Host.Options;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Startup-time validator for <see cref="StationOptions"/> that enforces invariants beyond
/// what <c>DataAnnotations</c> can express.
///
/// <para>
/// Guards <c>Station:SafeScope:LibraryIds</c>: each present library id must be positive.
/// An empty safe scope is permitted — the station falls back to mksafe silence during
/// main-source drain events (F4.4 degraded mode); a WARN log is emitted at boot so the
/// operator is aware. A non-positive id is nonsensical and indicates a configuration error.
/// </para>
///
/// <para>
/// Guards <c>Station:Safe:*</c> (F27 config table): <c>SeedMessage</c> must not be blank
/// (it seeds the boot announcement, F27.6) and <c>BedPadSeconds</c> must not be negative
/// (it pads an offline ffmpeg mix duration, F27.4).
/// </para>
///
/// <para>
/// Guards <c>Station:Cadence:StationIdEveryNUnits</c> (SPEC F42.2),
/// <c>Station:Rotation:RecentWindow</c> / <c>Station:Rotation:ArtistSeparation</c> (SPEC F41.6),
/// and <c>Station:BoundaryBias:LookaheadMinutes</c> (SPEC F74.3): each must be non-negative (0
/// disables the corresponding behavior). These properties carry a DataAnnotations
/// <c>[Range(0, int.MaxValue)]</c> attribute as documentation, but
/// <c>ValidateDataAnnotations()</c> on the root <see cref="StationOptions"/> in <c>Program.cs</c>
/// does NOT recurse into nested option classes — so this validator is the only thing that
/// actually enforces the floor at boot.
/// </para>
///
/// <para>
/// Guards <c>Station:Envelope:EnergyMin</c>/<c>EnergyMax</c> (SPEC F80.1, F81.1, STORY-212): both
/// must fall within <c>[0, 1]</c> (same "documentation-only [Range], this validator is the real
/// floor" story as the nested knobs above) and <c>EnergyMin</c> must not exceed <c>EnergyMax</c> —
/// mirrors <see cref="GenWave.Abstractions.Playout.EnergyRange"/>'s own construction-time invariant,
/// so a misconfigured station never boots into a self-contradictory energy band.
/// </para>
///
/// <para>
/// Guards <c>Station:Requests:WindowMinutes</c> (SPEC F87.6, STORY-224): must be a positive integer
/// (same "documentation-only [Range], this validator is the real floor" story as the nested knobs
/// above) — a request that expires the instant it arrives (or before) makes fulfillment impossible.
/// </para>
///
/// <para>
/// Guards <c>Station:Shows:PatterCadenceMinutes</c> (SPEC F116.3, STORY-308, PLAN T249): must be
/// non-negative (same "documentation-only [Range], this validator is the real floor" story as the
/// nested knobs above) — 0 legally disables the show-flavor line entirely.
/// </para>
///
/// <para>
/// Guards <c>Station:Ads:EveryNUnits</c> (SPEC F158.3, STORY-388, PLAN T397): must be non-negative
/// (0 disables the ad cadence trigger) — same "documentation-only [Range], this validator is the
/// real floor" story as the nested knobs above, the <c>Station:Cadence:StationIdEveryNUnits</c>
/// precedent one field over.
/// </para>
///
/// <para>
/// Guards <c>Station:Imaging:TimeAnnouncementBudgetSeconds</c> (SPEC F124.4/F141.1, PLAN T269/T326):
/// must be a positive integer (same "documentation-only <c>[Range(1, int.MaxValue)]</c>, this
/// validator is the real floor" story as the nested knobs above) — UNLIKE every "0 disables" knob
/// elsewhere in this validator, 0 here is not a legal off-switch: it would drop every single
/// <c>TimeDate</c> deferral undrained, silently killing F110.3 rather than disabling anything.
/// <c>SettingValidator</c> mirrors this same floor (plus an F53.1 ceiling) on the live-edit path.
/// </para>
///
/// <para>
/// Does NOT enforce SPEC F142's boundary cadence covenant (STORY-356, PLAN T327, closes gh-#300):
/// unlike every guard above, that rule MUTATES <c>Station:BoundaryBias:LookaheadMinutes</c> (clamps
/// it up, fail-safe, with one WARN) rather than merely accepting or rejecting the value already
/// bound — the wrong altitude for a pure <see cref="IValidateOptions{TOptions}"/> predicate.
/// <c>BoundaryCadenceCovenantPostConfigure</c> (an <see cref="IPostConfigureOptions{TOptions}"/>,
/// registered separately in <c>Program.cs</c> — see that type's own remarks for why it can't share
/// this project's <c>StationOptionsServiceCollectionExtensions</c> — but run by the framework
/// BEFORE this validator on every bind regardless of where it was registered) owns that rule; see
/// that type's own remarks for why, and <see cref="BoundaryCadenceCovenant"/> for the pure math it
/// wraps. This validator still guards <c>Station:BoundaryBias:LookaheadMinutes</c>'s plain
/// non-negativity floor immediately below, same as every nested knob above.
/// </para>
///
/// <para>
/// Registered as a singleton and triggered by <c>ValidateOnStart()</c> in
/// <c>Program.cs</c>.
/// </para>
/// </summary>
public sealed class StationOptionsValidator(ILogger<StationOptionsValidator> logger)
    : IValidateOptions<StationOptions>
{
    public ValidateOptionsResult Validate(string? name, StationOptions options)
    {
        if (options.SafeScope.LibraryIds.Any(id => id <= 0))
            return ValidateOptionsResult.Fail(
                "Station:SafeScope:LibraryIds must contain only positive library ids " +
                "(found one or more ids ≤ 0).");

        if (string.IsNullOrWhiteSpace(options.Safe.SeedMessage))
            return ValidateOptionsResult.Fail(
                "Station:Safe:SeedMessage must not be blank.");

        if (options.Safe.BedPadSeconds < 0)
            return ValidateOptionsResult.Fail(
                "Station:Safe:BedPadSeconds must be non-negative " +
                "(found a negative value).");

        if (options.Cadence.StationIdEveryNUnits < 0)
            return ValidateOptionsResult.Fail(
                "Station:Cadence:StationIdEveryNUnits must be non-negative " +
                "(0 disables station IDs).");

        if (options.Ads.EveryNUnits < 0)
            return ValidateOptionsResult.Fail(
                "Station:Ads:EveryNUnits must be non-negative " +
                "(0 disables ad spots).");

        if (options.Rotation.RecentWindow < 0)
            return ValidateOptionsResult.Fail(
                "Station:Rotation:RecentWindow must be non-negative " +
                "(0 disables anti-repeat).");

        if (options.Rotation.ArtistSeparation < 0)
            return ValidateOptionsResult.Fail(
                "Station:Rotation:ArtistSeparation must be non-negative " +
                "(0 disables artist separation).");

        if (options.BoundaryBias.LookaheadMinutes < 0)
            return ValidateOptionsResult.Fail(
                "Station:BoundaryBias:LookaheadMinutes must be non-negative " +
                "(0 disables boundary-aware selection bias).");

        if (options.Envelope.EnergyMin is < 0.0 or > 1.0)
            return ValidateOptionsResult.Fail(
                "Station:Envelope:EnergyMin must be within [0, 1].");

        if (options.Envelope.EnergyMax is < 0.0 or > 1.0)
            return ValidateOptionsResult.Fail(
                "Station:Envelope:EnergyMax must be within [0, 1].");

        if (options.Envelope.EnergyMin > options.Envelope.EnergyMax)
            return ValidateOptionsResult.Fail(
                "Station:Envelope:EnergyMin must not exceed Station:Envelope:EnergyMax.");

        if (options.Requests.WindowMinutes < 1)
            return ValidateOptionsResult.Fail(
                "Station:Requests:WindowMinutes must be a positive integer.");

        if (options.Shows.PatterCadenceMinutes < 0)
            return ValidateOptionsResult.Fail(
                "Station:Shows:PatterCadenceMinutes must be non-negative " +
                "(0 disables the show-flavor line).");

        if (options.Imaging.TimeAnnouncementBudgetSeconds < 1)
            return ValidateOptionsResult.Fail(
                "Station:Imaging:TimeAnnouncementBudgetSeconds must be a positive integer " +
                "(0 would drop every TimeDate deferral, not disable the feature).");

        if (options.SafeScope.LibraryIds.Count == 0)
        {
            logger.LogWarning(
                "SafeScope empty — drain events play mksafe silence (F4.4 degraded mode)");
        }

        return ValidateOptionsResult.Success;
    }
}
