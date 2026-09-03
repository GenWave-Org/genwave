namespace GenWave.Core.Abstractions;

/// <summary>
/// SPEC F156.5/F157.1 (STORY-384, gh-#417, gh-#380 epic) — the plugin's only host surface, handed to
/// <see cref="IGenWavePlugin.Register"/>. Deliberately additive-only BY CONSTRUCTION: every member is
/// an <c>Add*</c> call that hands the host one more implementation of an expected contract — there is
/// no replace, no unload, no interception, so a plugin can only ever widen what the station does, never
/// narrow or redirect it. v1's expected set is <see cref="IContextProvider"/> and
/// <see cref="IAdSpotSource"/> (F156.5); a future contract joining that set is one new <c>Add*</c>
/// method — additive to this interface, hence a minor <c>GenWave.Abstractions</c> bump, never a
/// breaking one.
///
/// BCL-only, like every type this package ships (no <c>Microsoft.Extensions.*</c>,
/// no <c>IServiceCollection</c>): a plugin author never needs to reference ASP.NET Core or the generic
/// host to compile against this contract.
/// </summary>
public interface IPluginHost
{
    /// <summary>
    /// Registers <paramref name="provider"/> with the host's context pipeline — the exact seam
    /// <c>GenWave.Context.ContextPipeline</c> already runs the built-in weather/history providers
    /// through (F107.1). A plugin <see cref="IContextProvider.Key"/> that collides with a built-in or
    /// an earlier plugin's is caught pre-registration and that WHOLE plugin is skipped with a WARN
    /// (F156.6) — the pipeline's own fail-fast duplicate-key constructor must never be the thing that
    /// discovers the collision, since that would down the station (F156.4).
    /// </summary>
    /// <param name="provider">The plugin's context provider implementation.</param>
    void AddContextProvider(IContextProvider provider);

    /// <summary>
    /// Registers <paramref name="source"/> with the host's ad-spot pipeline (F158.2): every registered
    /// source is tried in registration order and the first non-null answer wins, so a plugin
    /// registered here competes ahead of Home's own <c>LibraryAdSpotSource</c>, which always registers
    /// last — the floor a plugin can win the break over without replacing anything.
    /// </summary>
    /// <param name="source">The plugin's ad-spot source implementation.</param>
    void AddAdSpotSource(IAdSpotSource source);

    /// <summary>
    /// Reads <c>Plugins:{name}:{key}</c> from the host's own configuration (F157.2) — the same
    /// generic-read shape <c>IContextProvider.Key</c>'s settings-segment-prefix convention already
    /// established for built-in providers. Plugin settings are env/compose-only in v1: never on the
    /// settings allowlist, never persisted to <c>station.settings</c> — there is no live-reload path
    /// for a value this method returns, so a plugin that needs config to change without a restart must
    /// poll this itself.
    /// </summary>
    /// <param name="key">The setting's name beneath this plugin's own <c>Plugins:{name}:</c> segment. A
    /// null or blank <paramref name="key"/> is never a fault (T390 r2 review, pinned at PLAN T394): it
    /// resolves the plugin's own bare <c>Plugins:{name}:</c> segment prefix, which nothing configures
    /// directly, so the answer is null exactly like any other unset key — never an exception.</param>
    /// <returns>The configured value, or null when nothing is set for <paramref name="key"/> (including
    /// a null or blank <paramref name="key"/> itself).</returns>
    string? Setting(string key);
}
