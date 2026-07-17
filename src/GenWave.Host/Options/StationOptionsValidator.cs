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
/// Guards <c>Station:Persona:ActiveId</c> (SPEC F35.2): must be zero or a positive persona id.
/// Existence is NOT checked here (a stale id is legal and degrades at read time, F35.5) — only
/// the sign, which a negative value could never legitimately be.
/// </para>
///
/// <para>
/// Guards <c>Station:Cadence:StationIdEveryNUnits</c> (SPEC F42.2) and
/// <c>Station:Rotation:RecentWindow</c> / <c>Station:Rotation:ArtistSeparation</c> (SPEC F41.6):
/// each must be non-negative (0 disables the corresponding behavior). These three properties
/// carry a DataAnnotations <c>[Range(0, int.MaxValue)]</c> attribute as documentation, but
/// <c>ValidateDataAnnotations()</c> on the root <see cref="StationOptions"/> in <c>Program.cs</c>
/// does NOT recurse into nested option classes — so this validator is the only thing that
/// actually enforces the floor at boot.
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

        if (options.Persona.ActiveId < 0)
            return ValidateOptionsResult.Fail(
                "Station:Persona:ActiveId must be zero (none) or a positive persona id " +
                "(found a negative value).");

        if (options.Cadence.StationIdEveryNUnits < 0)
            return ValidateOptionsResult.Fail(
                "Station:Cadence:StationIdEveryNUnits must be non-negative " +
                "(0 disables station IDs).");

        if (options.Rotation.RecentWindow < 0)
            return ValidateOptionsResult.Fail(
                "Station:Rotation:RecentWindow must be non-negative " +
                "(0 disables anti-repeat).");

        if (options.Rotation.ArtistSeparation < 0)
            return ValidateOptionsResult.Fail(
                "Station:Rotation:ArtistSeparation must be non-negative " +
                "(0 disables artist separation).");

        if (options.SafeScope.LibraryIds.Count == 0)
        {
            logger.LogWarning(
                "SafeScope empty — drain events play mksafe silence (F4.4 degraded mode)");
        }

        return ValidateOptionsResult.Success;
    }
}
