namespace ExamplePlugin;

using GenWave.Core.Abstractions;

/// <summary>
/// The plugin's single entry point (SPEC F156/F157.1) — the manifest-named <c>entryType</c>
/// (<c>plugin.json</c>'s own <c>entryType</c> field). <c>GenWave.Plugins.PluginLoader</c> activates
/// exactly one instance of this type per boot and calls <see cref="Register"/> exactly once, before
/// the host finishes building.
/// </summary>
public sealed class DiceRollPlugin : IGenWavePlugin
{
    public string Name => "Dice Roll Example Plugin";

    public void Register(IPluginHost host)
    {
        // The additive surface, in its entirety: this plugin adds exactly one IContextProvider and
        // does nothing else — no I/O, no threads, Register must be inert (IGenWavePlugin's own
        // remarks). host.Setting is handed to the provider as a plain delegate, not the host itself
        // (see DiceRollContextProvider's own remarks on why).
        host.AddContextProvider(new DiceRollContextProvider(host.Setting));

        // IPluginHost exposes a SECOND seam this reference plugin deliberately leaves unused:
        //     host.AddAdSpotSource(new MyAdSpotSource());
        // registers an IAdSpotSource the same way — one more Add* call, additive, nothing to
        // replace or intercept. Left out here so a newcomer reading this file sees exactly one
        // clean, complete example rather than two half-explained ones.
    }
}
