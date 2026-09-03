namespace GenWave.Plugins;

/// <summary>
/// Which part of a <c>plugin.json</c> manifest caused <see cref="PluginManifestParser.Parse"/> to
/// reject it (SPEC F156.2, STORY-385 AC5: "one WARN names the field"). Five values mirror the
/// manifest's own five required fields, in the order <see cref="PluginManifestParser"/> checks them;
/// <see cref="Document"/> is the sixth, whole-document case — malformed JSON, or a JSON value that is
/// not an object — which cannot be pinned to any one field. Composing the actual WARN text (SPEC
/// F156.2's "skips that directory with a WARN naming the field") is the loader's job (PLAN T392) — this
/// type only carries the structured fact of which field, never a formatted log line.
/// </summary>
public enum PluginManifestField
{
    /// <summary>The manifest text itself failed to parse as a JSON object.</summary>
    Document,

    /// <summary><see cref="PluginManifest.Name"/> — missing or blank.</summary>
    Name,

    /// <summary><see cref="PluginManifest.Version"/> — missing or blank.</summary>
    Version,

    /// <summary><see cref="PluginManifest.AssemblyFileName"/> — missing, blank, or naming a path
    /// rather than a bare file name.</summary>
    Assembly,

    /// <summary><see cref="PluginManifest.EntryType"/> — missing or blank.</summary>
    EntryType,

    /// <summary><see cref="PluginManifest.Abstractions"/> — missing or blank.</summary>
    Abstractions,
}
