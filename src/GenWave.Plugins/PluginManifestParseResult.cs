namespace GenWave.Plugins;

/// <summary>
/// Outcome of <see cref="PluginManifestParser.Parse"/> (SPEC F156.2, STORY-385 AC2/AC5). Success
/// carries the parsed <see cref="PluginManifest"/>; failure carries WHICH manifest field caused the
/// reject plus a detail message — never a pre-formatted log line, since composing the actual WARN is
/// the loader's job (PLAN T392), not this parser's. A third, unrepresentable state
/// (both success and failure at once) is ruled out by the private constructor — only the static
/// factories below can create one. Mirrors <c>GenWave.Tts.SafeSegmentAuthorResult</c>'s own
/// typed-failure shape (the house idiom for "which stage failed" results).
/// </summary>
public sealed class PluginManifestParseResult
{
    readonly PluginManifest? manifest;
    readonly PluginManifestField failedField;
    readonly string detail;

    PluginManifestParseResult(PluginManifest? manifest, PluginManifestField failedField, string detail)
    {
        this.manifest = manifest;
        this.failedField = failedField;
        this.detail = detail;
    }

    public bool Succeeded => manifest is not null;

    /// <summary>The parsed manifest. Throws when read on a failed result.</summary>
    public PluginManifest Manifest => manifest
        ?? throw new InvalidOperationException($"Cannot read Manifest of a failed result: {failedField} — {detail}");

    /// <summary>Which manifest field caused the reject. Throws when read on a successful result.</summary>
    public PluginManifestField Field => Succeeded
        ? throw new InvalidOperationException("Cannot read Field of a successful result.")
        : failedField;

    /// <summary>
    /// Detail message describing the reject. Control-character-neutralized by
    /// <see cref="PluginManifestParser"/> — a crafted manifest value or a raw <c>JsonException.Message</c>
    /// can never split this into more than one line (CWE-117 log forging) — but this is still
    /// third-party-derived text, not a value this type or the parser certifies as fully log-safe.
    /// Callers still route it through <c>GenWave.Core.Logging.LogSanitize.Strip</c> before it reaches an
    /// actual log line, exactly as <see cref="GenWave.Core.Abstractions.IGenWavePlugin.Name"/>'s own
    /// remarks require of every other plugin-authored string (the loader, PLAN T392, is where that
    /// call happens). Throws when
    /// read on a successful result.
    /// </summary>
    public string Detail => Succeeded
        ? throw new InvalidOperationException("Cannot read Detail of a successful result.")
        : detail;

    public static PluginManifestParseResult Success(PluginManifest manifest) =>
        new(manifest, default, string.Empty);

    public static PluginManifestParseResult Failure(PluginManifestField failedField, string detail) =>
        new(null, failedField, detail);

    public override string ToString() =>
        Succeeded ? $"Success({Manifest})" : $"Failure({Field}: {Detail})";
}
