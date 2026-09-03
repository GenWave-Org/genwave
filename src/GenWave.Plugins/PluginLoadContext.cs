namespace GenWave.Plugins;

using System.Reflection;
using System.Runtime.Loader;

/// <summary>
/// One plugin's own, isolated <see cref="AssemblyLoadContext"/> (SPEC F156.3, STORY-385 AC3) — a
/// fresh instance per plugin, never <see cref="AssemblyLoadContext.Default"/>. Non-collectible: F156.1's
/// "no live toggle, changing the plugin set is a restart" already rules out ever needing to unload one
/// mid-process, so this constructor never passes <c>isCollectible: true</c>.
///
/// <para>
/// <b>Unification, not isolation, for <c>GenWave.Abstractions</c> and the BCL</b> — the opposite of
/// what a plugin's OWN third-party dependencies get. <see cref="Load"/> refuses, BY NAME, to ever
/// resolve <c>GenWave.Abstractions</c> from this plugin's own directory, even when the plugin carries
/// its own (possibly stale) copy of that DLL sitting right beside its main assembly — returning
/// <c>null</c> for that one name lets the CLR's own documented fallback behavior resolve it against
/// <see cref="AssemblyLoadContext.Default"/> instead, where the HOST's already-loaded copy lives. Every
/// contract type the plugin implements (<c>IGenWavePlugin</c>, <c>IContextProvider</c>,
/// <c>IAdSpotSource</c>) is declared in that same assembly, so this one name-match is what makes type
/// identity unify end to end — a plugin-carried copy is provably never loaded (pinned by
/// <c>AbstractionsTypesUnifyWithTheHost</c>).
/// </para>
///
/// <para>
/// <b>The BCL unifies the same way, with no special-case needed</b> — for the FRAMEWORK-DEPENDENT
/// build this loader expects (SPEC F156.2's <c>dotnet build</c>/<c>publish</c> shape; a self-contained
/// plugin publish, which bundles its own private BCL copy into its output directory, is not a shape
/// this loader is designed against): a plugin's own build output never copies <c>System.*</c>/
/// <c>Microsoft.*</c> BCL assemblies into its directory (they ship with the shared runtime, not the
/// plugin), so <see cref="AssemblyDependencyResolver"/> simply has no answer for those names either —
/// <see cref="Load"/> falls through to the same <c>return null</c> fallback path as
/// <c>GenWave.Abstractions</c>, and the CLR resolves them against Default. A real plugin's OUT-OF-BAND
/// third-party packages (a JSON library, an HTTP client extension, anything beyond the BCL and
/// Abstractions) DO get copied into the plugin's own directory by its own build, and DO load into
/// THIS context — the resolver's <c>&lt;assembly&gt;.deps.json</c> (or, absent one, its same-directory
/// fallback) places them there on purpose, and that is exactly the isolation this type exists to give
/// each plugin. It is safe specifically BECAUSE only Abstractions types (and the BCL) ever need to
/// cross the plugin/host boundary — every contract type a plugin implements or a host call passes
/// across that boundary lives in <c>GenWave.Abstractions</c>, so a plugin's own third-party dependency
/// graph staying entirely inside its own ALC never has anywhere it would need to be seen from the host
/// side.
/// </para>
/// </summary>
public sealed class PluginLoadContext : AssemblyLoadContext
{
    // Name-match only (AssemblyName.Name — never the full identity: version, culture, public key
    // token). A plugin built against any 5.x GenWave.Abstractions must still unify to whatever the
    // host has loaded; PluginManifest.Abstractions (the manifest's own declared build-time version) is
    // informational only and is never compared against this, so refusing on exact identity would
    // reject a plugin SPEC F156.2 never asked to be rejected.
    const string AbstractionsAssemblyName = "GenWave.Abstractions";

    readonly AssemblyDependencyResolver resolver;

    /// <param name="pluginAssemblyPath">
    /// The plugin's own manifest-named assembly file's full, on-disk path (SPEC F156.2's
    /// <c>assembly</c> field, resolved against the plugin's directory) — deliberately NOT a bare
    /// directory path, even though this type's own remarks describe it as "the plugin dir": every
    /// dependency <see cref="AssemblyDependencyResolver"/> resolves is anchored on this ONE file — its
    /// optional <c>&lt;name&gt;.deps.json</c> lookup and its same-directory fallback both key off the
    /// specific main assembly given to it, not merely the folder it happens to live in.
    /// </param>
    public PluginLoadContext(string pluginAssemblyPath) : base(isCollectible: false)
    {
        resolver = new AssemblyDependencyResolver(pluginAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // OrdinalIgnoreCase (T392 review finding 4): the CLR's own assembly-name binder matches
        // simple names case-insensitively, so a plugin's own AssemblyName casing quirk (build tooling,
        // an OS with a case-insensitive default filesystem) must never make this refusal miss —
        // Ordinal alone would let a differently-cased "GenWave.abstractions" slip past unrefused.
        if (string.Equals(assemblyName.Name, AbstractionsAssemblyName, StringComparison.OrdinalIgnoreCase))
            return null;

        var resolvedPath = resolver.ResolveAssemblyToPath(assemblyName);
        return resolvedPath is not null ? LoadFromAssemblyPath(resolvedPath) : null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var resolvedPath = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return resolvedPath is not null ? LoadUnmanagedDllFromPath(resolvedPath) : IntPtr.Zero;
    }
}
