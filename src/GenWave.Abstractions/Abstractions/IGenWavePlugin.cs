namespace GenWave.Core.Abstractions;

/// <summary>
/// SPEC F156/F157.1 (STORY-384, gh-#417, gh-#380 epic) — a third-party plugin's single entry point.
/// The loader (<c>GenWave.Plugins</c>, PLAN T392) activates the manifest-named
/// <c>entryType</c> once per plugin, inside that plugin's own non-collectible
/// <see cref="System.Runtime.Loader.AssemblyLoadContext"/>, and calls <see cref="Register"/> exactly
/// once before the host finishes building — the loader is the ONLY caller this contract ever expects.
/// </summary>
public interface IGenWavePlugin
{
    /// <summary>
    /// The plugin's display name — a third-party-authored string, so the house never treats it as
    /// trusted input. Surfaced verbatim on <c>GET /api/status</c>'s <c>plugins[]</c> array (F156.7),
    /// the machine-readable JSON contract, where "verbatim" is the whole point. The loader's
    /// one-line-per-outcome booth-log narrative (F156.4) is a DIFFERENT surface: free text never
    /// enters a log line raw in this codebase (the house <c>LogSanitize</c> rule), so the loader
    /// (<c>GenWave.Plugins</c>, PLAN T392) sanitizes this value before it reaches the booth log —
    /// this doc comment is not license to skip that step. Not a registration key: nothing in the
    /// host cross-references this against another plugin, so two plugins sharing a name is cosmetic,
    /// never a load failure.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Adds this plugin's implementations to <paramref name="host"/> — the plugin's only chance to
    /// participate in the station, since <see cref="IPluginHost"/>'s members are exclusively
    /// <c>Add*</c> (F156.5: additive-only BY CONSTRUCTION, no replace, no unload, no interception).
    ///
    /// <b>Must be inert: no I/O, no threads, no blocking work.</b> The loader invokes this
    /// synchronously, once, at boot, before the host is built (F156.8) — anything this method does
    /// beyond constructing and handing over implementations delays every other plugin's load and the
    /// station's own boot. Fetching a remote catalog, opening a socket, or spawning a background loop
    /// belongs inside the registered <see cref="IContextProvider"/>/<see cref="IAdSpotSource"/>
    /// itself, invoked lazily once the station is actually running — never here.
    /// </summary>
    /// <param name="host">
    /// The one surface this plugin may register against; see <see cref="IPluginHost"/>'s own remarks
    /// for why its shape can only ever grow, never change underfoot.
    /// </param>
    void Register(IPluginHost host);
}
