namespace GenWave.Host.Options;

using Microsoft.Extensions.Options;
using GenWave.MediaLibrary.Options;

/// <summary>
/// Startup-time validator for <c>Library:Scan:QuarantineExemptRoots</c> (SPEC F154.3; STORY-379;
/// PLAN T381, gh-#529) — mirrors <see cref="StationOptionsValidator"/>'s own "documentation-only,
/// this validator is the real floor" story: <see cref="ScanOptions"/> is bound via a plain
/// <c>Configure&lt;ScanOptions&gt;</c> call inside <c>MediaLibraryServiceCollectionExtensions</c>
/// (never <c>ValidateDataAnnotations()</c>), so nothing else enforces this at boot.
///
/// A relative exempt root would otherwise reach <see cref="GenWave.MediaLibrary.Garden.FileActions.FileActionPlanner"/>'s
/// own <c>Path.GetFullPath</c> call, which resolves a relative path against the PROCESS's own
/// working directory — an accident of the container's launch directory would then decide which
/// files the gardener's jail treats as authored/exempt, exactly the "un-rooted target" hazard
/// <c>FileActionRule.OutsideRoot</c> already refuses for an operator-supplied move target
/// (<c>IFileActionPlanner.Plan</c>'s own remarks). Failing boot instead means a misconfigured exempt
/// root is caught before the gardener ever reasons about it, not discovered the first time a real
/// jail decision silently goes the wrong way.
///
/// Registered as a singleton and triggered by <c>ValidateOnStart()</c>, both directly in
/// <c>Program.cs</c> (<see cref="ScanOptions"/> already binds inside <c>AddMediaLibrary</c>, so no
/// second <c>.Bind()</c> call is needed here — only the validator + the trigger).
/// </summary>
public sealed class ScanOptionsValidator : IValidateOptions<ScanOptions>
{
    public ValidateOptionsResult Validate(string? name, ScanOptions options)
    {
        if (options.QuarantineExemptRoots.Any(root => !Path.IsPathRooted(root)))
            return ValidateOptionsResult.Fail(
                "Library:Scan:QuarantineExemptRoots entries must be absolute paths " +
                "(found one or more relative entries).");

        return ValidateOptionsResult.Success;
    }
}
