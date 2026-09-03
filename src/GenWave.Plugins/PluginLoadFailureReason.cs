namespace GenWave.Plugins;

/// <summary>
/// WHY <see cref="PluginLoader"/> skipped one plugin whole (SPEC F156.4, STORY-385 AC5–AC8) — mirrors
/// <see cref="PluginManifestField"/>'s own "structured fact, never a formatted log line" shape: the
/// loader never logs (see <see cref="PluginLoadReport"/>'s own remarks — the report IS the surface),
/// so this only ever needs to carry WHICH stage failed, in the order the loader itself attempts them.
/// </summary>
public enum PluginLoadFailureReason
{
    /// <summary>The manifest file could not be read: it is a symlink (refused, fail-closed — the same
    /// posture <c>PluginManifestDiscovery</c> already takes for a symlinked plugin DIRECTORY), it
    /// exceeds the loader's bounded read size, or an I/O error occurred while reading it.</summary>
    ManifestUnreadable,

    /// <summary><see cref="PluginManifestParser.Parse"/> rejected the manifest document — see
    /// <see cref="PluginManifestParseResult.Field"/> (folded into this report's own
    /// <see cref="PluginLoadReport.Detail"/>).</summary>
    ManifestInvalid,

    /// <summary>The manifest's <c>assembly</c> file does not exist at the resolved path — its own
    /// typed reason (T392 review finding 2d), never folded into <see cref="AssemblyLoadFailed"/>: a
    /// plugin directory shipped with a manifest naming a file it never actually included is a
    /// different, earlier failure than a file that exists but is not a loadable .NET assembly.</summary>
    AssemblyFileMissing,

    /// <summary>The manifest's <c>assembly</c> file exists but is a symlink (refused, fail-closed —
    /// the same carry-forward-D posture as <see cref="ManifestUnreadable"/>'s own symlink case,
    /// applied here to the assembly file instead of the manifest file).</summary>
    AssemblyFileInvalid,

    /// <summary>The assembly file exists and is not a symlink, but failed to load as a .NET assembly
    /// (a corrupt or non-.NET file, SPEC F156.4's pinned "corrupt DLL" case) or failed for any other
    /// I/O reason while loading.</summary>
    AssemblyLoadFailed,

    /// <summary>The manifest's <c>entryType</c> names no type in the loaded assembly (resolved via
    /// <c>Assembly.GetType</c> against that assembly ONLY — never a global type-name probe).</summary>
    EntryTypeNotFound,

    /// <summary>The named <c>entryType</c> exists but does not implement the host's own (unified)
    /// <c>IGenWavePlugin</c> — checked by a real type-check against the unified interface, never a
    /// string comparison, so a plugin-carried Abstractions copy could never spoof this.</summary>
    EntryTypeNotAPlugin,

    /// <summary>The named <c>entryType</c> implements <c>IGenWavePlugin</c> but could not be
    /// constructed — most commonly, it has no public parameterless constructor (SPEC F156.2 requires
    /// one; PLAN T392's own activation contract), or its constructor itself threw.</summary>
    EntryTypeNotConstructible,

    /// <summary>The plugin's <c>Register(IPluginHost)</c> threw. Every registration it made before
    /// throwing is discarded — the buffer commits only after <c>Register</c> RETURNS (STORY-385
    /// AC8).</summary>
    RegisterThrew,

    /// <summary>A registered <c>IContextProvider.Key</c> is not lowercase ASCII letters, digits, and
    /// hyphens — <c>IContextProvider.Key</c>'s own contract, enforced here so a malformed key can never
    /// reach <c>ContextPipeline</c>'s own fail-fast constructor (F156.6).</summary>
    ContextProviderKeyInvalid,

    /// <summary>A registered <c>IContextProvider.Key</c> collides with a built-in provider's key, an
    /// earlier-loaded plugin's, or another key this SAME plugin registered — pre-validated here so
    /// <c>ContextPipeline</c>'s own fail-fast duplicate-key constructor is never the thing that
    /// discovers it (F156.6: that would down the station, violating F156.4).</summary>
    ContextProviderKeyCollision,

    /// <summary>Any other failure this loader did not anticipate by name — the safety net that keeps
    /// F156.4's "any failure skips the whole plugin, never down" true even for a failure mode no
    /// specific reason above names.</summary>
    Unexpected,

    /// <summary>The plugins root itself could not be enumerated (T392 review finding 1) — a
    /// permission-denied or vanished-mid-enumeration failure on <c>pluginsRoot</c> directly, before
    /// any candidate was ever identified. Distinct from every reason above, which all describe ONE
    /// candidate's own failure: this one names <see cref="PluginLoadReport"/>'s single, root-level
    /// report on a <see cref="PluginLoadState.RootUnreadable"/> outcome — <c>LoadAll</c>'s own
    /// "never throws" guarantee held even when the filesystem itself misbehaved at the top.</summary>
    RootUnreadable,
}
