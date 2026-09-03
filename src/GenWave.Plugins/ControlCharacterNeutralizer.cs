namespace GenWave.Plugins;

/// <summary>
/// The one shared choke point for stripping control characters out of a third-party-derived string
/// before it can reach <see cref="PluginManifestParseResult.Detail"/> or
/// <see cref="PluginLoadReport.Detail"/> (CWE-117 log forging) — lifted out at T392 so the loader's
/// own Detail values get the IDENTICAL treatment <c>PluginManifestParser</c> already proved at T391,
/// rather than a second hand-rolled copy of the same three lines. Strips, never replaces (both
/// callers' own remarks): a crafted value still earns a single-line Detail, not a swapped-out generic
/// one, and the caller is still the one naming which field/plugin/cause it belongs to.
///
/// <para>
/// GenWave.Plugins deliberately stays off <c>GenWave.Core</c> (see the csproj's own
/// reference-rationale comment), so this is a small, self-contained floor — not a substitute for a
/// consuming caller's own <c>GenWave.Core.Logging.LogSanitize.Strip</c> pass before an actual log
/// line (the loader itself never logs; see <see cref="PluginLoadReport"/>'s own remarks on why the
/// report IS the surface).
/// </para>
/// </summary>
internal static class ControlCharacterNeutralizer
{
    public static string Strip(string value) =>
        value.Any(char.IsControl) ? new string(value.Where(c => !char.IsControl(c)).ToArray()) : value;
}
