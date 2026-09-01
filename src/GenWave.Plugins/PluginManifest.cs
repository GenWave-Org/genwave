namespace GenWave.Plugins;

/// <summary>
/// One validated <c>plugin.json</c> manifest (SPEC F156.2, STORY-385). Only
/// <see cref="PluginManifestParser.Parse"/> constructs one — every value here has already passed that
/// parser's blank/shape checks, so a caller (the loader, PLAN T392) never re-validates.
/// </summary>
/// <param name="Slug">
/// The plugin directory's own name, beneath <c>Plugins:Root</c> — sourced from the FILESYSTEM
/// (<see cref="PluginManifestDiscovery"/>), never from the manifest body itself, so a plugin can never
/// claim an identity other than the folder it was mounted under. The loader combines this with
/// <see cref="AssemblyFileName"/> to resolve the plugin's actual assembly path. Operator-filesystem-
/// controlled, not third-party-plugin-authored — but still UNVALIDATED here (this parser never checks
/// its shape, since <see cref="PluginManifestDiscovery"/> hands it over already resolved from a real
/// directory entry) and still not a value this codebase treats as inherently log-safe: it must pass
/// <c>GenWave.Core.Logging.LogSanitize.Strip</c> before it ever reaches a log line, exactly like
/// <see cref="Name"/>'s own note below.
/// </param>
/// <param name="Name">
/// The plugin's own display name (SPEC F156.7's <c>plugins[].name</c>) — untrusted, third-party-
/// authored text (see <c>GenWave.Core.Abstractions.IGenWavePlugin.Name</c>'s own remarks on why the
/// loader sanitizes it before it ever reaches a log line).
/// </param>
/// <param name="Version">
/// The plugin's own version string. SPEC F156.2 does not require semver — this parser accepts any
/// non-blank string, since nothing downstream parses or compares it (a semver rule here would be
/// enforcement with no consumer yet — the "don't over-validate" lean).
/// </param>
/// <param name="AssemblyFileName">
/// A bare file name — never a path (SPEC F156.2: "a file name, no path separators — reject
/// otherwise"). <see cref="PluginManifestParser"/> rejects any value containing <c>/</c>, <c>\</c>, or
/// <c>..</c>; it does not additionally require a <c>.dll</c> extension, since SPEC F156.2 does not rule
/// that, and requiring it would buy nothing a corrupt or wrong-extension file wouldn't also fail at
/// load time anyway (the loader's own F156.4 WARN+skip posture, PLAN T392).
/// </param>
/// <param name="EntryType">
/// The full name of the <c>IGenWavePlugin</c> implementation the loader activates (SPEC F156.2). Only
/// checked non-blank here — the loader is what discovers whether it names a real, loadable type (PLAN
/// T392); this parser has no assembly loaded yet to check it against.
/// </param>
/// <param name="Abstractions">
/// The <c>GenWave.Abstractions</c> contract version the plugin was built against (SPEC F156.2).
/// Accepted as any non-blank string for the same "no consumer yet" reason <see cref="Version"/>
/// documents — nothing compares it against the host's own Abstractions version today.
/// </param>
public sealed record PluginManifest(
    string Slug, string Name, string Version, string AssemblyFileName, string EntryType, string Abstractions);
