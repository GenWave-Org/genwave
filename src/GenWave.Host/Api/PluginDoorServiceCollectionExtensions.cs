using GenWave.Core.Abstractions;
using GenWave.Plugins;

namespace GenWave.Host.Api;

/// <summary>
/// Wires SPEC F156's plugin door (STORY-385/386, PLAN T394): the two-knob opt-in
/// (<c>Plugins:Enabled</c> plus a mounted <c>Plugins:Root</c>, F156.1), the loader run, and the DI
/// registrations a committed plugin earns. Called ONCE from Program.cs, after the <c>AddGenWave*</c>
/// sequence and before <c>builder.Build()</c> — SPEC F156.8's own ordering requirement (a plugin's
/// <c>Register</c> call, and therefore every registration it buffers, must land before the host
/// finishes building).
///
/// <para>
/// <b>Closed-door inertness (F156.8).</b> Neither knob present: this method registers
/// <see cref="PluginStatusAccessor"/> (always, empty — see that type's own remarks on why that alone
/// never touches SEAMS.md) and returns having done nothing else — no plugin construction, zero new
/// <c>IContextProvider</c>/<c>IAdSpotSource</c> registrations, no filesystem read beyond the one
/// <see cref="Directory.Exists(string)"/> probe below. Exactly one knob present: same, plus
/// <see cref="PluginStatusAccessor.RecordMissingKnob"/> names which half is missing — logged/
/// booth-narrated only AFTER <c>Build()</c> (<c>PluginDoorNarrationExtensions.NarratePluginDoorAsync</c>),
/// since no <c>ILogger</c>/<c>IBoothLogAppender</c> exists yet at this point in Program.cs. Both knobs
/// present: the loader runs for real.
/// </para>
/// </summary>
static class PluginDoorServiceCollectionExtensions
{
    /// <summary>SPEC F156.1's own default for <c>Plugins:Root</c> — the compose overlay's own mount
    /// target (<c>compose.plugins.yaml</c>).</summary>
    const string DefaultRoot = "/plugins";

    public static IServiceCollection AddGenWavePluginDoor(this IServiceCollection services, IConfiguration configuration)
    {
        var status = new PluginStatusAccessor();
        services.AddSingleton(status);

        var enabled = configuration.GetValue<bool>("Plugins:Enabled");
        var configuredRoot = configuration["Plugins:Root"];
        var root = string.IsNullOrWhiteSpace(configuredRoot) ? DefaultRoot : configuredRoot;

        // "Root missing" IS "nothing is mounted there" (the build-surface note this task was scoped
        // against) — Plugins:Root always resolves to SOME path (configured or the default above), so
        // the second knob is never "was Plugins:Root set" but "does that directory actually exist",
        // exactly the observable fact the compose.plugins.yaml overlay flips.
        var mounted = Directory.Exists(root);

        if (!enabled && !mounted)
            return services; // Neither knob — F156.1's "does nothing observable" floor.

        if (enabled != mounted)
        {
            status.RecordMissingKnob(enabled
                ? $"Plugins:Enabled is set, but no plugin root is mounted at \"{root}\" — the plugin door stays closed (see compose.plugins.yaml)."
                : $"A plugin root is mounted at \"{root}\", but Plugins:Enabled is not set — the plugin door stays closed.");
            return services;
        }

        // Both knobs present (F156.1) — the loader runs for real. F156.6's pre-registration key gate
        // needs every built-in IContextProvider.Key the host registers WITHOUT plugins:
        // WeatherContextProvider.Key/HistoryContextProvider.Key (GenWave.Context) are both plain,
        // hardcoded, always-"weather"/"history" expression-bodied getters that never touch the typed
        // HttpClient behind them merely by being read — but resolving those singletons here, this
        // early, would still be the first place Program.cs's own composition-root narrative ever
        // RESOLVES anything rather than just registering it (every AddGenWave* call above is a pure
        // registration step). The literal pair below is that same fact, taken on faith rather than a
        // live read — a future rename of either Key getter is a one-line diff here, caught the moment
        // it lands a plugin claiming the freed key (F156.6's own collision check would simply stop
        // firing for it), not silently: PluginLoadReport.State would flip Loaded for what used to
        // collide, visible on GET /api/status/the booth log the very next boot.
        var builtInContextProviderKeys = new HashSet<string>(StringComparer.Ordinal) { "weather", "history" };

        var loader = new PluginLoader(key => configuration[key]);
        var result = loader.LoadAll(root, builtInContextProviderKeys);
        status.Record(result.Reports);

        // Wrap BEFORE the provider ever reaches DI, never inside GenWave.Plugins itself
        // (KeyMemoizedContextProvider's own class remarks explain why: wrapping at commit would break
        // two of that project's own AssemblyLoadContext-isolation facts). The pair's own ValidatedKey
        // — never pair.Provider.Key — is what the wrapper freezes (T394 review HIGH-2).
        foreach (var pair in result.ContextProviders)
            services.AddSingleton<IContextProvider>(new KeyMemoizedContextProvider(pair.ValidatedKey, pair.Provider));

        // Additive, forward-compatible: no consumer resolves IAdSpotSource until GenWave.Ads' own
        // AdSpotPipeline lands (PLAN T396) — IEnumerable<IAdSpotSource> resolves cleanly today
        // regardless (empty unless a plugin registered one), so registering committed sources now is
        // harmless and saves a second wiring pass later.
        foreach (var source in result.AdSpotSources)
            services.AddSingleton<IAdSpotSource>(source);

        return services;
    }
}
