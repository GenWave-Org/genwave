namespace GenWave.Plugins;

/// <summary>
/// One plugin's typed outcome (SPEC F156.4/F156.7, STORY-385) — the surface <see cref="PluginLoader"/>
/// hands back for EVERY candidate it attempted, loaded or skipped. Mirrors
/// <see cref="PluginManifestParseResult"/>'s own idiom: a structured fact, never a pre-formatted log
/// line — <b>this project never logs</b> (no <c>ILogger</c> reference anywhere in it; see the csproj's
/// own reference-rationale comment), so composing the actual WARN/INFO booth-log line, and routing
/// <see cref="Detail"/> through <c>GenWave.Core.Logging.LogSanitize.Strip</c> one more time on the way
/// there, is the wiring task's job (PLAN T394) — this report IS the surface it projects onto
/// <c>GET /api/status</c>'s <c>plugins[]</c> array and the booth log alike, from the one place both
/// reads happen.
/// </summary>
public sealed class PluginLoadReport
{
    readonly PluginLoadFailureReason failureReason;
    readonly string detail;

    PluginLoadReport(
        string slug, string? name, string? version, IReadOnlyList<string> contracts,
        PluginLoadState state, PluginLoadFailureReason failureReason, string detail)
    {
        Slug = slug;
        Name = name;
        Version = version;
        Contracts = contracts;
        State = state;
        this.failureReason = failureReason;
        this.detail = detail;
    }

    /// <summary>The plugin directory's own name — always known, even when the manifest itself never
    /// parsed (<see cref="PluginManifest.Slug"/>'s own remarks: filesystem-sourced, never
    /// author-claimed). Empty on a <see cref="PluginLoadState.RootUnreadable"/> report: that failure
    /// happens before any plugin directory is ever identified, so there is no slug to carry — the
    /// plugins root path itself is folded into <see cref="Detail"/> instead.</summary>
    public string Slug { get; }

    /// <summary>The manifest's own <c>name</c> field — null only when the manifest never parsed far
    /// enough to read it (a <see cref="PluginLoadFailureReason.ManifestUnreadable"/> or the earliest
    /// <see cref="PluginLoadFailureReason.ManifestInvalid"/> rejects). Third-party-authored, untrusted
    /// text — see <c>IGenWavePlugin.Name</c>'s own remarks on why a consumer must sanitize before
    /// logging it.</summary>
    public string? Name { get; }

    /// <summary>The manifest's own <c>version</c> field — null under the same circumstances as
    /// <see cref="Name"/>.</summary>
    public string? Version { get; }

    /// <summary>The contracts this plugin actually added (e.g. <c>"IContextProvider"</c>), in
    /// registration order — always empty on a <see cref="PluginLoadState.Skipped"/> report (STORY-385
    /// AC8's no-partial guarantee: a throwing or rejected plugin contributes nothing, so there is
    /// nothing to name here).</summary>
    public IReadOnlyList<string> Contracts { get; }

    public PluginLoadState State { get; }

    /// <summary>Which stage failed. Throws when read on a <see cref="PluginLoadState.Loaded"/>
    /// report — mirrors <see cref="PluginManifestParseResult.Field"/>'s own shape.</summary>
    public PluginLoadFailureReason Reason => State == PluginLoadState.Loaded
        ? throw new InvalidOperationException("Cannot read Reason of a loaded report.")
        : failureReason;

    /// <summary>
    /// Human-readable detail naming the specific cause — already control-character-neutralized
    /// (<see cref="ControlCharacterNeutralizer"/>, the same choke point
    /// <see cref="PluginManifestParseResult.Detail"/> itself uses) so a crafted plugin name, manifest
    /// field, or exception message can never split this into more than one line. Still third-party
    /// derived, still not certified fully log-safe by that alone — the consumer's own
    /// <c>LogSanitize.Strip</c> pass before an actual log line remains required (this type's own
    /// class remarks). Throws when read on a <see cref="PluginLoadState.Loaded"/> report.
    /// </summary>
    public string Detail => State == PluginLoadState.Loaded
        ? throw new InvalidOperationException("Cannot read Detail of a loaded report.")
        : detail;

    public static PluginLoadReport Loaded(string slug, string name, string version, IReadOnlyList<string> contracts) =>
        new(slug, name, version, contracts, PluginLoadState.Loaded, default, string.Empty);

    public static PluginLoadReport Skipped(
        string slug, string? name, string? version, PluginLoadFailureReason reason, string detail) =>
        new(slug, name, version, Array.Empty<string>(), PluginLoadState.Skipped, reason, ControlCharacterNeutralizer.Strip(detail));

    /// <summary>
    /// The one report <see cref="PluginLoader.LoadAll"/> returns when <c>pluginsRoot</c> itself could
    /// not be enumerated (T392 review finding 1) — <see cref="Slug"/>/<see cref="Name"/>/
    /// <see cref="Version"/> are all empty/null, since no candidate directory was ever reached;
    /// <paramref name="detail"/> is expected to name the root path itself, since <see cref="Slug"/>
    /// cannot.
    /// </summary>
    public static PluginLoadReport RootUnreadable(string detail) =>
        new(
            string.Empty, null, null, Array.Empty<string>(), PluginLoadState.RootUnreadable,
            PluginLoadFailureReason.RootUnreadable, ControlCharacterNeutralizer.Strip(detail));

    public override string ToString() => State switch
    {
        PluginLoadState.Loaded => $"Loaded({Slug}: {Name} {Version}, contracts=[{string.Join(", ", Contracts)}])",
        PluginLoadState.Skipped => $"Skipped({Slug}: {Reason} — {Detail})",
        PluginLoadState.RootUnreadable => $"RootUnreadable({Detail})",
        _ => throw new InvalidOperationException($"Unknown {nameof(PluginLoadState)}: {State}."),
    };
}
