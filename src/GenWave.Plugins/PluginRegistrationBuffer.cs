namespace GenWave.Plugins;

using GenWave.Core.Abstractions;

/// <summary>
/// The <see cref="IPluginHost"/> one plugin's <c>Register</c> call actually sees (SPEC F156.4/F156.6,
/// STORY-385 AC8) — every <c>Add*</c> call only BUFFERS; nothing here reaches
/// <see cref="PluginLoadResult"/> until <see cref="PluginLoader"/> has confirmed <c>Register</c>
/// returned without throwing AND every buffered <see cref="IContextProvider.Key"/> passed
/// pre-validation. A throw partway through — one provider added, then an exception — leaves this
/// buffer populated, but the LOADER is what decides never to read from it in that case: "commit only
/// after Register returns" is enforced by the caller's control flow, not by anything this type does to
/// itself (there is no rollback here, only a promise the loader keeps never to look).
///
/// <para>
/// <b>The retention case</b> (T392 review finding 3): <see cref="IGenWavePlugin.Register"/>'s own
/// contract forbids a plugin from doing anything beyond constructing and handing over its
/// implementations, but nothing stops a MISBEHAVING plugin from squirrelling this same
/// <see cref="IPluginHost"/> reference away and calling an <c>Add*</c> method on it later — from a
/// retained field, a background thread, a captured closure — after <c>Register</c> has already
/// returned. <see cref="Seal"/> is what turns that late call into a loud
/// <see cref="InvalidOperationException"/> instead of silent, post-commit corruption of a report the
/// loader already handed out: <see cref="PluginLoader"/> calls it the instant <c>Register</c> returns,
/// before it ever reads <see cref="ContextProviders"/>/<see cref="AdSpotSources"/>/<see cref="Contracts"/>
/// to validate or commit, and every <c>Add*</c> call shares the SAME lock <see cref="Seal"/> takes — so
/// a late call can never land between the loader's seal and its snapshot read; it either completed
/// (and is visible) strictly before the seal, or it throws.
/// </para>
/// </summary>
internal sealed class PluginRegistrationBuffer : IPluginHost
{
    readonly object gate = new();
    readonly List<IContextProvider> contextProviders = new();
    readonly List<IAdSpotSource> adSpotSources = new();
    readonly List<string> contracts = new();
    readonly string settingsPrefix;
    readonly Func<string, string?> settingReader;
    bool sealedForRegistration;

    /// <param name="pluginSlug">
    /// The plugin's own directory name — used as the <c>Plugins:{name}:{key}</c> settings segment
    /// (SPEC F157.2). A deliberate reading of "{name}" as the SLUG, not the manifest's own untrusted
    /// <c>name</c> field: <see cref="PluginManifest.Slug"/>'s own remarks are exactly why — an
    /// operator names the settings segment they configure (<c>Plugins__my-plugin__ApiKey</c>) after
    /// the folder THEY mounted, never after arbitrary author-supplied display text a plugin could
    /// change release to release, or that could contain characters (spaces, colons) a configuration
    /// path segment can't carry cleanly. The same precedent <c>IContextProvider.Key</c>'s own
    /// <c>Context:{Key}:*</c> settings-segment convention already established for a
    /// structurally-anchored identity over a display one.
    /// </param>
    /// <param name="settingReader">
    /// Reads one fully-qualified configuration key and returns its value, or null when unset — supplied
    /// by <see cref="PluginLoader"/>'s own constructor, which itself takes this as a
    /// <c>Func&lt;string,string?&gt;</c> rather than an <c>IConfiguration</c> reference, keeping this
    /// whole project <c>Microsoft.Extensions</c>-free (the csproj's own reference-rationale comment);
    /// the real reader, backed by the host's actual <c>IConfiguration</c>, is supplied at PLAN T394.
    /// </param>
    public PluginRegistrationBuffer(string pluginSlug, Func<string, string?> settingReader)
    {
        ArgumentNullException.ThrowIfNull(pluginSlug);
        ArgumentNullException.ThrowIfNull(settingReader);

        settingsPrefix = $"Plugins:{pluginSlug}:";
        this.settingReader = settingReader;
    }

    public IReadOnlyList<IContextProvider> ContextProviders => contextProviders;

    public IReadOnlyList<IAdSpotSource> AdSpotSources => adSpotSources;

    /// <summary>The contract names added, in the exact order <c>Register</c> called <c>Add*</c> —
    /// <see cref="PluginLoadReport.Contracts"/>'s own source.</summary>
    public IReadOnlyList<string> Contracts => contracts;

    public void AddContextProvider(IContextProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        lock (gate)
        {
            ThrowIfSealed();
            contextProviders.Add(provider);
            contracts.Add(nameof(IContextProvider));
        }
    }

    public void AddAdSpotSource(IAdSpotSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (gate)
        {
            ThrowIfSealed();
            adSpotSources.Add(source);
            contracts.Add(nameof(IAdSpotSource));
        }
    }

    /// <summary>
    /// SPEC F157.2; null/blank-<paramref name="key"/> decision pinned at PLAN T394 (the T390 r2 review
    /// note). A null or blank <paramref name="key"/> is never guarded specially — C#'s string
    /// concatenation treats a null <paramref name="key"/> as empty, so the lookup collapses to this
    /// plugin's own bare <c>Plugins:{slug}:</c> segment prefix, which no real deployment ever
    /// configures a value for directly. The real, <c>IConfiguration</c>-backed setting reader
    /// PLAN T394 supplies never throws for an unusual key either (its own indexer's documented
    /// contract) — so the answer for a null/blank key is null, exactly the same "nothing configured"
    /// answer any other unset key gets, never an exception.
    /// </summary>
    public string? Setting(string key) => settingReader(settingsPrefix + key);

    /// <summary>
    /// Closes this buffer to further registration (this type's own class remarks, "the retention
    /// case") — called by <see cref="PluginLoader"/> the instant <c>Register</c> returns, before it
    /// ever reads <see cref="ContextProviders"/>/<see cref="AdSpotSources"/>/<see cref="Contracts"/>.
    /// Idempotent: sealing an already-sealed buffer is a no-op, never an error.
    /// </summary>
    public void Seal()
    {
        lock (gate)
        {
            sealedForRegistration = true;
        }
    }

    /// <summary>Must be called with <see cref="gate"/> already held.</summary>
    void ThrowIfSealed()
    {
        if (sealedForRegistration)
        {
            throw new InvalidOperationException(
                "This plugin's Register(IPluginHost) call already returned — a registration buffer " +
                "never accepts a late Add* call from a retained IPluginHost reference (SPEC F156.4's " +
                "\"commit only after Register returns\" contract).");
        }
    }
}
