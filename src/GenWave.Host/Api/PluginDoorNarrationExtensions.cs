using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Logging;
using GenWave.Plugins;

namespace GenWave.Host.Api;

/// <summary>
/// Narrates the plugin door's own boot outcome (SPEC F156.4/F156.7, STORY-385 AC1/AC5, STORY-386 AC4,
/// PLAN T394) — the <c>ILogger</c> WARN/INFO lines and the booth-log narrative rows
/// <see cref="PluginLoadReport"/>'s own class remarks assign to "the wiring task" (<c>PluginLoader</c>
/// never logs — see its own remarks). Called from Program.cs AFTER <c>builder.Build()</c>, mirroring
/// <c>AdminStartupWarningExtensions.WarnIfAdminPasswordMissing</c>'s own shape one file over: resolved
/// through the built host's own DI logging pipeline, not a bootstrap logger, so it reaches every
/// registered <see cref="ILoggerProvider"/> including test doubles.
///
/// <para>
/// <b>Every third-party string is stripped before it reaches either surface</b> (T392 review finding
/// 1, pinned at T394): <see cref="PluginLoadReport.Name"/>/<see cref="PluginLoadReport.Version"/> are
/// raw manifest text, and <see cref="PluginLoadReport.Detail"/> — though already control-character-
/// neutralized inside <c>GenWave.Plugins</c> — is still third-party derived, so
/// <see cref="LogSanitize.Strip"/> runs on all three here, for BOTH the <c>ILogger</c> line and the
/// booth-log <c>Summary</c>. <c>GET /api/status</c>'s <c>plugins[]</c> array
/// (<see cref="StatusController"/>) is the one surface that carries Name/Version verbatim — the JSON
/// serializer escapes them, and "verbatim" is the whole point of a machine-readable contract (see
/// <c>IGenWavePlugin.Name</c>'s own remarks).
/// </para>
///
/// <para>
/// <b>A booth-log write failure never downs the boot.</b> Mirrors
/// <c>GenWave.MediaLibrary.Station.BoothLogDrainService.ProcessAsync</c>'s own posture exactly (a DB
/// outage there never crashes the drain loop): SPEC F156.4's "never down" promise is the plugin door's
/// whole reason to exist, and this narration step running synchronously in Program.cs, before
/// <c>app.Run()</c>, would otherwise be a NEW way for that promise to break — a Postgres hiccup at the
/// exact instant this runs must degrade to a dropped booth row, never a failed boot.
/// </para>
/// </summary>
static class PluginDoorNarrationExtensions
{
    public static async Task NarratePluginDoorAsync(this WebApplication app, CancellationToken ct)
    {
        var status = app.Services.GetRequiredService<PluginStatusAccessor>();

        if (status.MissingKnobNote is { } missingKnobNote)
        {
            // STORY-385 AC1: one INFO line, nothing loaded — no PluginLoadReport was ever produced,
            // so there is nothing for the booth log to narrate. The note embeds Plugins:Root's own
            // configured value (an operator/config-controlled string, but stripped at this same
            // choke point regardless — the house rule this whole type's own remarks state: every
            // string reaching a log line here is stripped, no exceptions carved out for "probably
            // trusted").
            app.Logger.LogInformation("{Note}", LogSanitize.Strip(missingKnobNote));
            return;
        }

        if (status.Reports.Count == 0)
            return; // Neither knob (F156.1) — truly nothing happened, not even an INFO line.

        var boothLog = app.Services.GetRequiredService<IBoothLogAppender>();
        foreach (var report in status.Reports)
            await NarrateOneAsync(report, app.Logger, boothLog, ct);
    }

    static async Task NarrateOneAsync(PluginLoadReport report, ILogger logger, IBoothLogAppender boothLog, CancellationToken ct)
    {
        // T394 review HIGH-1: Slug is filesystem-derived third-party text (an operator-or-attacker
        // chosen directory name) exactly like Name/Version/Detail — it reaches this fallback on the
        // COMMON skip path (Name is null for every ManifestUnreadable/ManifestInvalid report, since
        // the manifest never parsed far enough to read it), so it gets the SAME Strip every other
        // string on this surface gets, never an unstripped exception for "it's just a directory name".
        var name = LogSanitize.Strip(report.Name);
        var slug = LogSanitize.Strip(report.Slug);
        var displayName = name.Length > 0 ? name : (slug.Length > 0 ? slug : "(plugins root)");

        var (kind, summary) = report.State == PluginLoadState.Loaded
            ? BuildLoadedNarrative(report, displayName, logger)
            : BuildSkippedNarrative(report, displayName, logger);

        try
        {
            await boothLog.AppendAsync(new BoothLogAppendRequest(kind, summary, PersonaId: null), ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Booth log write failed for {Kind} — entry dropped, boot unaffected", kind);
        }
    }

    static (string Kind, string Summary) BuildLoadedNarrative(PluginLoadReport report, string displayName, ILogger logger)
    {
        var version = LogSanitize.Strip(report.Version);
        var contracts = string.Join(", ", report.Contracts);
        var summary = $"Plugin \"{displayName}\" {version} loaded ({contracts}).";

        logger.LogInformation(
            "Plugin {PluginName} {PluginVersion} loaded ({Contracts})", displayName, version, contracts);

        return ("plugin-loaded", summary);
    }

    static (string Kind, string Summary) BuildSkippedNarrative(PluginLoadReport report, string displayName, ILogger logger)
    {
        // PluginLoadReport.Reason only throws for a Loaded report — both Skipped and RootUnreadable
        // (this branch's only two possible states) answer it cleanly (that type's own remarks).
        var reason = report.Reason.ToString();
        var detail = LogSanitize.Strip(report.Detail);
        var summary = $"Plugin \"{displayName}\" skipped ({reason}): {detail}";

        logger.LogWarning(
            "Plugin {PluginName} skipped ({Reason}): {Detail}", displayName, reason, detail);

        return ("plugin-skipped", summary);
    }
}
