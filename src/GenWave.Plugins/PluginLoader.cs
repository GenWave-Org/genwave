namespace GenWave.Plugins;

using System.Reflection;
using System.Text.RegularExpressions;
using GenWave.Core.Abstractions;

/// <summary>
/// Loads every plugin a manifest names beneath a plugins root (SPEC F156.2–F156.4/F156.6, STORY-385) —
/// the type PLAN T392 exists to build. One <see cref="PluginLoadContext"/> per plugin, an
/// <c>entryType</c> activation, a buffered <c>Register(IPluginHost)</c> call that commits only when it
/// returns cleanly AND every context-provider key it registered pre-validates, and a typed
/// <see cref="PluginLoadReport"/> for every candidate either way. Never throws out of
/// <see cref="LoadAll"/>: SPEC F156.4's "WARN + skip, never down" is this type's entire reason to
/// exist, so a single misbehaving plugin can never take the loader — or the plugins loaded before or
/// after it — down with it.
///
/// <para>
/// <b>This project never logs</b> (no <c>ILogger</c> reference anywhere in <c>GenWave.Plugins</c> —
/// the csproj's own reference-rationale comment): every <see cref="PluginLoadReport"/> this type
/// produces is the surface the wiring task (PLAN T394) reads to compose the ACTUAL booth-log WARN/INFO
/// lines and <c>GET /api/status</c>'s <c>plugins[]</c> array, from one place, once.
/// </para>
/// </summary>
public sealed partial class PluginLoader
{
    // Mirrors GenWave.Context.ContextPipeline's own KeyPattern exactly (IContextProvider.Key's
    // contract: lowercase ASCII letters, digits, and hyphens) — duplicated here, not referenced,
    // because GenWave.Plugins deliberately stays off GenWave.Context (the csproj's own
    // reference-rationale comment): this loader must be able to reject an invalid key BEFORE any
    // provider ever reaches that pipeline's constructor (F156.6), so it needs its own copy of the same
    // rule, not a dependency on the type it exists to protect.
    [GeneratedRegex("^[a-z0-9-]+\\z")]
    private static partial Regex ContextProviderKeyPattern();

    /// <summary>The bounded manifest read (PLAN T392, the T391-deferred size bound): 64 KiB is
    /// generous for a five-field JSON document — a manifest this large is itself a signal something is
    /// wrong, and reading an operator-mounted file with no bound at all is exactly the
    /// resource-exhaustion vector F156.4's WARN+skip posture exists to contain. The assembly FILE gets
    /// no such bound — see <see cref="LoadOne"/>'s own remarks at that call site for why.</summary>
    const int ManifestMaxBytes = 64 * 1024;

    readonly Func<string, string?> settingReader;

    /// <param name="settingReader">
    /// Reads one fully-qualified configuration key and returns its value, or null when unset — handed
    /// straight through to every plugin's own <see cref="PluginRegistrationBuffer"/> (that type's own
    /// remarks: a <c>Func&lt;string,string?&gt;</c>, not <c>IConfiguration</c>, keeps this whole
    /// project <c>Microsoft.Extensions</c>-free; the real, config-backed reader is supplied at PLAN
    /// T394).
    /// </param>
    public PluginLoader(Func<string, string?> settingReader)
    {
        ArgumentNullException.ThrowIfNull(settingReader);
        this.settingReader = settingReader;
    }

    /// <summary>
    /// Loads every candidate <see cref="PluginManifestDiscovery.EnumerateCandidates"/> finds beneath
    /// <paramref name="pluginsRoot"/>, in ascending slug order (F156.6's own tiebreak for "earlier
    /// plugin") — one report per candidate, never throwing regardless of how badly any individual
    /// candidate fails.
    /// </summary>
    /// <param name="pluginsRoot">The mounted plugins directory (<c>Plugins:Root</c>).</param>
    /// <param name="builtInContextProviderKeys">
    /// Every <c>IContextProvider.Key</c> the host registers WITHOUT plugins (e.g. <c>"weather"</c>,
    /// <c>"history"</c>) — a plugin claiming one of these is pre-validated out (F156.6) before it can
    /// ever reach <c>ContextPipeline</c>'s own constructor.
    /// </param>
    public PluginLoadResult LoadAll(string pluginsRoot, IReadOnlySet<string> builtInContextProviderKeys)
    {
        ArgumentNullException.ThrowIfNull(pluginsRoot);
        ArgumentNullException.ThrowIfNull(builtInContextProviderKeys);

        var reports = new List<PluginLoadReport>();
        var contextProviders = new List<ValidatedContextProvider>();
        var adSpotSources = new List<IAdSpotSource>();

        // Seeded with the built-ins, then grows with each plugin that commits (F156.6's "earlier
        // plugin" tiebreak — candidates already arrive in ascending slug order from Discovery, so
        // growing this set as we go IS that tiebreak). Copied, never the caller's own set: LoadAll
        // must never mutate what it was handed.
        var keysInUse = new HashSet<string>(builtInContextProviderKeys, StringComparer.Ordinal);

        // T392 review finding 1: PluginManifestDiscovery.EnumerateCandidates is a lazy iterator —
        // Directory.EnumerateDirectories underneath it does not actually touch the filesystem until
        // the first MoveNext(), so a `foreach` directly over it would let a permission-denied or
        // vanished-mid-walk pluginsRoot throw OUTSIDE every per-candidate try below, taking LoadAll's
        // own "never throws" promise down with it (proven: a chmod-000 root raises
        // UnauthorizedAccessException here). Materializing with ToList() INSIDE this try forces the
        // whole walk to happen right here, where a root-level failure becomes one typed
        // RootUnreadable report instead of an unhandled exception — LoadOne's own per-candidate
        // safety net (its outer catch) is a different, narrower promise: it only ever covers a SINGLE
        // candidate already found, never the walk that finds candidates in the first place.
        List<PluginManifestCandidate> candidates;
        try
        {
            candidates = PluginManifestDiscovery.EnumerateCandidates(pluginsRoot).ToList();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            reports.Add(PluginLoadReport.RootUnreadable($"plugins root \"{pluginsRoot}\" could not be enumerated: {ex.Message}"));
            return new PluginLoadResult(reports, contextProviders, adSpotSources);
        }

        foreach (var candidate in candidates)
            reports.Add(LoadOne(candidate, keysInUse, contextProviders, adSpotSources));

        return new PluginLoadResult(reports, contextProviders, adSpotSources);
    }

    /// <summary>
    /// Every stage of one plugin's load, in order — bounded manifest read, manifest parse, assembly
    /// file validation, dedicated-ALC load, entryType activation, <c>Register</c>, context-key
    /// pre-validation, commit. The outer catch is the unconditional safety net: each stage above ALSO
    /// catches its own most-likely failure at the point it happens, so the report names the RIGHT
    /// <see cref="PluginLoadFailureReason"/> — but nothing this plugin's own code does, including a
    /// failure mode none of those specific catches anticipated, is ever allowed to escape and take the
    /// loader, or the plugins after it, down too (SPEC F156.4).
    /// </summary>
    PluginLoadReport LoadOne(
        PluginManifestCandidate candidate, HashSet<string> keysInUse,
        List<ValidatedContextProvider> contextProviders, List<IAdSpotSource> adSpotSources)
    {
        string? name = null;
        string? version = null;
        try
        {
            if (!TryReadManifestBounded(candidate.ManifestPath, out var manifestJson, out var readFailureDetail))
                return PluginLoadReport.Skipped(candidate.Slug, name, version, PluginLoadFailureReason.ManifestUnreadable, readFailureDetail);

            var parseResult = PluginManifestParser.Parse(candidate.Slug, manifestJson);
            if (!parseResult.Succeeded)
            {
                return PluginLoadReport.Skipped(
                    candidate.Slug, name, version, PluginLoadFailureReason.ManifestInvalid,
                    $"{parseResult.Field}: {parseResult.Detail}");
            }

            var manifest = parseResult.Manifest;
            name = manifest.Name;
            version = manifest.Version;

            var pluginDirectory = Path.GetDirectoryName(candidate.ManifestPath)
                ?? throw new InvalidOperationException($"Manifest path \"{candidate.ManifestPath}\" has no directory.");
            var assemblyPath = Path.Combine(pluginDirectory, manifest.AssemblyFileName);

            if (!File.Exists(assemblyPath))
            {
                return PluginLoadReport.Skipped(
                    candidate.Slug, name, version, PluginLoadFailureReason.AssemblyFileMissing,
                    $"assembly file \"{manifest.AssemblyFileName}\" does not exist");
            }

            // Carry-forward D (T391 r2 review): the same fail-closed symlink refusal
            // PluginManifestDiscovery already applies to a plugin DIRECTORY, and TryReadManifestBounded
            // (below) applies to the manifest FILE, applied here to the ASSEMBLY file too — an
            // operator-mounted plugins root can smuggle a symlink at any of these three levels. Size is
            // deliberately NOT bounded here (unlike the manifest read): the assembly is operator-mounted
            // CODE, already trusted with process-level execution the instant it loads — a byte-count
            // ceiling on it buys nothing a corrupt-image load failure (below) wouldn't also catch, and
            // real plugin assemblies (with their own third-party dependencies) have no reason to stay
            // small.
            if (new FileInfo(assemblyPath).LinkTarget is not null)
            {
                return PluginLoadReport.Skipped(
                    candidate.Slug, name, version, PluginLoadFailureReason.AssemblyFileInvalid,
                    $"assembly file \"{manifest.AssemblyFileName}\" is a symlink, refused");
            }

            Assembly pluginAssembly;
            try
            {
                var loadContext = new PluginLoadContext(assemblyPath);
                pluginAssembly = loadContext.LoadFromAssemblyPath(assemblyPath);
            }
            catch (Exception ex) when (ex is BadImageFormatException or IOException or UnauthorizedAccessException)
            {
                return PluginLoadReport.Skipped(candidate.Slug, name, version, PluginLoadFailureReason.AssemblyLoadFailed, ex.Message);
            }

            // asm.GetType only — never Type.GetType, which probes every already-loaded assembly
            // globally and could resolve a same-named type from somewhere this plugin never shipped.
            var entryType = pluginAssembly.GetType(manifest.EntryType, throwOnError: false);
            if (entryType is null)
            {
                return PluginLoadReport.Skipped(
                    candidate.Slug, name, version, PluginLoadFailureReason.EntryTypeNotFound,
                    $"entryType \"{manifest.EntryType}\" was not found in \"{manifest.AssemblyFileName}\"");
            }

            // A real type-check against the HOST's own (unified) interface — never a string compare
            // against entryType's name or its interfaces' names. Had Abstractions unification ever
            // broken (PluginLoadContext's own remarks), a plugin-carried copy's own "IGenWavePlugin"
            // would be a DIFFERENT CLR type entirely, and this check would correctly reject it here as
            // EntryTypeNotAPlugin instead of silently succeeding on a name match.
            if (!typeof(IGenWavePlugin).IsAssignableFrom(entryType))
            {
                return PluginLoadReport.Skipped(
                    candidate.Slug, name, version, PluginLoadFailureReason.EntryTypeNotAPlugin,
                    $"entryType \"{manifest.EntryType}\" does not implement IGenWavePlugin");
            }

            IGenWavePlugin plugin;
            try
            {
                // Requires a public parameterless constructor (PLAN T392's own activation contract):
                // Activator.CreateInstance(Type) only ever invokes a PUBLIC parameterless constructor,
                // throwing MissingMethodException when the type has none — caught below like any other
                // construction failure, never a separate reflection probe of the type's constructors.
                if (Activator.CreateInstance(entryType) is not IGenWavePlugin instance)
                {
                    return PluginLoadReport.Skipped(
                        candidate.Slug, name, version, PluginLoadFailureReason.EntryTypeNotConstructible,
                        $"entryType \"{manifest.EntryType}\" could not be constructed");
                }

                plugin = instance;
            }
            catch (Exception ex)
            {
                return PluginLoadReport.Skipped(candidate.Slug, name, version, PluginLoadFailureReason.EntryTypeNotConstructible, ex.Message);
            }

            var buffer = new PluginRegistrationBuffer(candidate.Slug, settingReader);
            try
            {
                plugin.Register(buffer);
            }
            catch (Exception ex)
            {
                // STORY-385 AC8: Register threw, so this buffer is discarded outright — the method
                // returns here, before anything buffer holds is ever read.
                return PluginLoadReport.Skipped(candidate.Slug, name, version, PluginLoadFailureReason.RegisterThrew, ex.Message);
            }
            finally
            {
                // T392 review finding 3: sealed the instant Register returns OR throws — a plugin
                // that retained this buffer's IPluginHost reference and calls an Add* method later
                // (a background thread, a captured closure) gets InvalidOperationException, never a
                // silent, post-commit mutation of a report already handed out (PluginRegistrationBuffer's
                // own class remarks, "the retention case").
                buffer.Seal();
            }

            // T392 review finding 3: snapshot the PROVIDER OBJECTS once, right here — every read
            // below (key pre-validation, the committed collections) walks this ONE array, never a
            // second live read of buffer.ContextProviders/AdSpotSources. Real copies (ToArray()),
            // not another IReadOnlyList VIEW aliasing the buffer's own mutable lists — buffer is
            // already sealed above, but a fresh array also means nothing downstream could ever alias
            // the buffer's own storage even if sealing were somehow bypassed.
            //
            // T392 review finding B2: snapshotting the OBJECTS is not enough on its own —
            // IContextProvider.Key is a third-party GETTER, and nothing stops one from returning a
            // DIFFERENT value on a second call. TryValidateContextProviderKeys below reads each
            // provider's Key EXACTLY ONCE and hands the validated VALUES back in validatedKeys; the
            // commit loop reuses those strings verbatim rather than calling .Key a second time — a
            // provider that answered "safe-key" during validation, then a built-in's own name on a
            // later read, can never smuggle that second answer into keysInUse.
            var contextProviderSnapshot = buffer.ContextProviders.ToArray();
            var adSpotSourceSnapshot = buffer.AdSpotSources.ToArray();

            if (!TryValidateContextProviderKeys(
                    contextProviderSnapshot, keysInUse, out var validatedKeys, out var keyFailureReason, out var keyFailureDetail))
            {
                return PluginLoadReport.Skipped(candidate.Slug, name, version, keyFailureReason, keyFailureDetail);
            }

            // Commit: every buffered registration joins the aggregate, and every VALIDATED key (never
            // a re-read) joins the running set the NEXT candidate's own pre-validation checks against
            // (F156.6's "earlier plugin" tiebreak).
            foreach (var key in validatedKeys)
                keysInUse.Add(key);

            // T394 review HIGH-2: pair each provider with its VALIDATED key here, at commit — never
            // a bare provider list a caller might later re-read .Key from. validatedKeys and
            // contextProviderSnapshot are index-aligned (TryValidateContextProviderKeys' own
            // contract: "hands the validated strings back... in the same order"), so this zip is the
            // one and only place a provider and its validated identity are ever joined.
            for (var i = 0; i < contextProviderSnapshot.Length; i++)
                contextProviders.Add(new ValidatedContextProvider(validatedKeys[i], contextProviderSnapshot[i]));

            adSpotSources.AddRange(adSpotSourceSnapshot);

            // T392 review advisory 5: allocated here, after the validation gate, not alongside the
            // two snapshots above — a rejected plugin (the common early-exit above) never pays for a
            // Contracts copy it will never return.
            var contractsSnapshot = buffer.Contracts.ToArray();
            return PluginLoadReport.Loaded(candidate.Slug, manifest.Name, manifest.Version, contractsSnapshot);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return PluginLoadReport.Skipped(candidate.Slug, name, version, PluginLoadFailureReason.Unexpected, ex.Message);
        }
    }

    /// <summary>
    /// Reads <paramref name="manifestPath"/> whole, refusing a symlink (carry-forward D) and a file
    /// past <see cref="ManifestMaxBytes"/> before ever opening a reader over it.
    /// </summary>
    static bool TryReadManifestBounded(string manifestPath, out string manifestJson, out string failureDetail)
    {
        if (new FileInfo(manifestPath).LinkTarget is not null)
        {
            manifestJson = string.Empty;
            failureDetail = "manifest file is a symlink, refused";
            return false;
        }

        try
        {
            using var stream = File.OpenRead(manifestPath);
            if (stream.Length > ManifestMaxBytes)
            {
                manifestJson = string.Empty;
                failureDetail = $"manifest exceeds the {ManifestMaxBytes}-byte read bound";
                return false;
            }

            using var reader = new StreamReader(stream);
            manifestJson = reader.ReadToEnd();
            failureDetail = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            manifestJson = string.Empty;
            failureDetail = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Pre-validates every buffered <see cref="IContextProvider.Key"/> — format first
    /// (<see cref="IContextProvider.Key"/>'s own contract), then collision against
    /// <paramref name="keysInUse"/> (built-ins plus every earlier-committed plugin) AND against every
    /// OTHER key this SAME plugin buffered, so two providers within one plugin sharing a key can never
    /// reach <c>ContextPipeline</c>'s own constructor either. Stops at the first offending key —
    /// mirrors <c>PluginManifestParser</c>'s own first-rule-wins shape.
    ///
    /// <para>
    /// Reads each <paramref name="providers"/> entry's <see cref="IContextProvider.Key"/> EXACTLY
    /// ONCE (T392 review finding B2) and hands the validated strings back through
    /// <paramref name="validatedKeys"/>, in the same order — a third-party getter is not guaranteed
    /// to be pure, so the CALLER must commit these exact values, never call <c>.Key</c> a second time
    /// to "confirm" what was already validated here.
    /// </para>
    /// </summary>
    static bool TryValidateContextProviderKeys(
        IReadOnlyList<IContextProvider> providers, IReadOnlySet<string> keysInUse,
        out IReadOnlyList<string> validatedKeys,
        out PluginLoadFailureReason failureReason, out string failureDetail)
    {
        var seenThisPlugin = new HashSet<string>(StringComparer.Ordinal);
        var keys = new List<string>(providers.Count);
        foreach (var provider in providers)
        {
            var key = provider.Key;
            if (key is null || !ContextProviderKeyPattern().IsMatch(key))
            {
                validatedKeys = Array.Empty<string>();
                failureReason = PluginLoadFailureReason.ContextProviderKeyInvalid;
                failureDetail = $"context provider key \"{key}\" is not lowercase ASCII letters, digits, and hyphens";
                return false;
            }

            if (keysInUse.Contains(key) || !seenThisPlugin.Add(key))
            {
                validatedKeys = Array.Empty<string>();
                failureReason = PluginLoadFailureReason.ContextProviderKeyCollision;
                failureDetail = $"context provider key \"{key}\" is already registered";
                return false;
            }

            keys.Add(key);
        }

        validatedKeys = keys;
        failureReason = default;
        failureDetail = string.Empty;
        return true;
    }
}
