using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using GenWave.Core.Abstractions;
using GenWave.Host.Configuration;
using GenWave.Host.Theming;

namespace GenWave.Host.Api;

/// <summary>
/// Operator settings endpoints. Exposes the allowlisted subset of station configuration for
/// inspection (<c>GET /api/settings</c>) and live editing (<c>PUT /api/settings</c>).
///
/// Security contract:
///   • Only keys present in <see cref="StationSettingsAllowlist"/> are ever read or written.
///   • Secrets (<c>Admin:Password</c>, connection strings, passwords) are not on the allowlist
///     and are therefore unreachable through this API.
///   • Cookie auth: covered by the deny-by-default authorization policy when <c>Admin:Password</c>
///     is set (same policy as <see cref="MediaController"/>).
///   • PUT requires <c>Content-Type: application/json</c> as a CSRF guard (415 otherwise).
///   • Invalid or non-allowlisted keys → 400 ProblemDetails; nothing is persisted.
/// </summary>
[ApiController]
[Route("api")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.Settings)]
public sealed class SettingsController(
    IConfiguration configuration,
    IStationSettingsStore store,
    SettingValidator validator,
    ILogger<SettingsController> logger,
    IIconPackStore iconPackStore,
    ThemeCatalog? injectedThemeCatalog = null) : ControllerBase
{
    /// <summary>
    /// <c>Station:Theme</c>'s choices widen to the DI-registered <see cref="ThemeCatalog"/>'s
    /// current shipped ∪ owner set (SPEC F103.7, STORY-271, PLAN T183) — every production instance
    /// gets the real singleton automatically (registered in <c>Program.cs</c>; DI resolves it by
    /// type regardless of constructor-parameter ordering). The trailing optional
    /// <c>injectedThemeCatalog</c> parameter/fallback exists ONLY so the many existing
    /// <c>new SettingsController(...)</c> unit tests exercising every OTHER allowlisted key keep
    /// compiling and passing unchanged, falling back to <see cref="ThemeCatalog.LoadShipped"/> —
    /// the exact shipped-only set this controller reported for <c>Station:Theme</c> before T183.
    /// </summary>
    readonly ThemeCatalog themeCatalog = injectedThemeCatalog ?? ThemeCatalog.LoadShipped();

    /// <summary>
    /// GET /api/settings — returns one <see cref="SettingDto"/> per allowlisted key.
    ///
    /// <c>source</c> is <c>"override"</c> when a DB override row exists for the key;
    /// <c>"default"</c> when the effective value comes from env/appsettings.
    ///
    /// <c>kind</c> and <c>unit</c> come from the allowlist metadata so the admin UI can
    /// render the appropriate input control without hard-coding per-key knowledge.
    /// </summary>
    [HttpGet("settings")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var overrideKeys = await store.ReadAllAsync(ct);
        var versions = await store.ReadVersionsAsync(ct);
        var iconPackChoices = await IconPackChoicesAsync(ct);

        var items = StationSettingsAllowlist.All.Select(allowed =>
        {
            var rawValue  = allowed.Kind == SettingKind.NumberList
                ? GetNumberListJson(configuration, allowed.Key)
                : configuration[allowed.Key] ?? string.Empty;
            var source    = overrideKeys.ContainsKey(allowed.Key) ? "override" : "default";
            var applyMode = ApplyModeWireValue(allowed.ApplyMode);
            var kind      = KindWireValue(allowed.Kind);
            var version   = versions.GetValueOrDefault(allowed.Key, 0);
            return new SettingDto(allowed.Key, rawValue, source, applyMode, kind, allowed.Unit, ChoicesFor(allowed, iconPackChoices), version);
        }).ToList();

        return Ok(items);
    }

    /// <summary>
    /// PUT /api/settings — validate and persist one or more key/value pairs.
    ///
    /// All-or-nothing per request: if any key/value fails validation the entire request is
    /// rejected with 400 and nothing is written.
    ///
    /// Engine-restart keys are persisted and reflected in GET <c>source=override</c> immediately
    /// but take effect only after the Liquidsoap engine is restarted. The response includes
    /// per-key <c>applyMode</c> so the caller knows which keys need a restart.
    /// </summary>
    [HttpPut("settings")]
    [Consumes("application/json")]
    public async Task<IActionResult> Put(
        [FromBody] IReadOnlyList<SettingUpdateRequest> updates,
        CancellationToken ct)
    {
        if (updates.Count == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title  = "No updates supplied.",
                Detail = "The request body must contain at least one { key, value } entry.",
            });
        }

        // Validate all entries first — reject the entire request on the first error so the
        // caller gets a clear diagnostic and nothing is partially written. Errors are keyed by
        // the setting key they belong to (gh-#425): one bucket per offending key, so a single
        // invalid entry in a multi-entry batch no longer paints its message under every other
        // key. An empty-key entry names no setting to attribute the message to, so it — like the
        // cross-field check below — lands in ASP.NET's own conventional keyless bucket, "".
        var fieldErrors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        void AddError(string key, string message)
        {
            if (!fieldErrors.TryGetValue(key, out var messages))
            {
                messages = [];
                fieldErrors[key] = messages;
            }
            messages.Add(message);
        }

        foreach (var update in updates)
        {
            if (string.IsNullOrWhiteSpace(update.Key))
            {
                AddError(string.Empty, "Each entry must have a non-empty key.");
                continue;
            }

            var error = validator.Validate(update.Key, update.Value ?? string.Empty);
            if (error is not null)
                AddError(update.Key, error);
        }

        // Cross-field check: run only when all per-key validations pass so error messages are
        // not conflated with parse failures. Its message names two keys at once, so — like an
        // empty-key entry above — it belongs in the keyless "" bucket, not either individual key.
        if (fieldErrors.Count == 0)
        {
            var batch = updates
                .Where(u => u.Key is not null)
                .ToDictionary(u => u.Key!, u => u.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);

            var crossFieldError = validator.ValidateBatch(batch);
            if (crossFieldError is not null)
                AddError(string.Empty, crossFieldError);
        }

        if (fieldErrors.Count > 0)
        {
            var problem = new ValidationProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title  = "One or more settings values are invalid.",
            };
            foreach (var (key, messages) in fieldErrors)
                problem.Errors[key] = messages.ToArray();
            return BadRequest(problem);
        }

        // All valid — persist each one.  WriteAsync raises the reload token after each write;
        // IOptionsMonitor re-binds automatically so api-side live knobs take effect immediately.
        //
        // gh-#486: an update that carries ExpectedVersion goes through the version-guarded write
        // instead — a mismatch rejects the whole request with 409 before any LATER entry is
        // attempted (an EARLIER entry in this same loop may already have committed; this endpoint
        // was already non-transactional across keys before gh-#486, and stays that way). An update
        // with no ExpectedVersion is unaffected — the exact unconditional last-write-wins write this
        // endpoint always did.
        foreach (var update in updates)
        {
            // NumberList keys arrive as a JSON-encoded array string (e.g. "[2]").
            // Deserialize to long[] so JsonSerializer in the store persists the JSONB array,
            // not a JSON-encoded string-of-array (double-encoding).
            var allowed = StationSettingsAllowlist.ByKey[update.Key];
            object valueToStore = allowed.Kind == SettingKind.NumberList
                ? (object)(JsonSerializer.Deserialize<long[]>(update.Value ?? "[]") ?? Array.Empty<long>())
                : update.Value ?? string.Empty;

            // F25.2: warn when the operator explicitly clears SafeScope (non-empty → empty).
            // An empty SafeScope means drain events fall back to mksafe silence (F4.4 degraded mode).
            if (allowed.Kind == SettingKind.NumberList
                && update.Key.Equals("Station:SafeScope:LibraryIds", StringComparison.OrdinalIgnoreCase)
                && valueToStore is long[] newIds
                && newIds.Length == 0
                && configuration.GetSection("Station:SafeScope:LibraryIds").GetChildren().Any())
            {
                logger.LogWarning(
                    "SafeScope emptied by operator — drain events play mksafe silence (F4.4 degraded mode)");
            }

            if (update.ExpectedVersion is { } expectedVersion)
            {
                var outcome = await store.WriteIfVersionMatchesAsync(update.Key, valueToStore, expectedVersion, ct);
                if (outcome == SettingsWriteOutcome.Conflict)
                {
                    logger.LogInformation(
                        "Setting write conflict: key={Key} expectedVersion={ExpectedVersion}",
                        update.Key, expectedVersion);
                    return Conflict(VersionConflictProblem(update.Key));
                }
            }
            else
            {
                await store.WriteAsync(update.Key, valueToStore, ct);
            }

            logger.LogInformation(
                "Setting persisted: key={Key} applyMode={ApplyMode}",
                update.Key,
                allowed.ApplyMode);
        }

        // Build the response so the caller knows the applyMode and kind/unit for each written key.
        var overrideKeys = await store.ReadAllAsync(ct);
        var versions = await store.ReadVersionsAsync(ct);
        var iconPackChoices = await IconPackChoicesAsync(ct);
        var result = updates.Select(u =>
        {
            var allowed   = StationSettingsAllowlist.ByKey[u.Key];
            var rawValue  = allowed.Kind == SettingKind.NumberList
                ? (GetNumberListJson(configuration, u.Key) is { Length: > 0 } json ? json : u.Value ?? string.Empty)
                : configuration[u.Key] ?? u.Value;
            var source    = overrideKeys.ContainsKey(u.Key) ? "override" : "default";
            var applyMode = ApplyModeWireValue(allowed.ApplyMode);
            var kind      = KindWireValue(allowed.Kind);
            var version   = versions.GetValueOrDefault(u.Key, 0);
            return new SettingDto(u.Key, rawValue, source, applyMode, kind, allowed.Unit, ChoicesFor(allowed, iconPackChoices), version);
        }).ToList();

        return Ok(result);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Choices to present for one allowlisted entry, THIS request — <c>Station:Theme</c> widens to
    /// <see cref="themeCatalog"/>'s current shipped ∪ owner set (SPEC F103.7, STORY-271, PLAN T183)
    /// and <c>Station:IconPack</c> widens to <paramref name="iconPackChoices"/>, every currently
    /// installed pack (SPEC F130.4, STORY-337, PLAN T303 — the SECOND branch this comment's own prior
    /// YAGNI note anticipated: "a second one is free to earn its own branch… when it exists"), rather
    /// than the static snapshot baked into <see cref="AllowedSetting.Choices"/> at this process's
    /// first touch of <see cref="StationSettingsAllowlist"/>. Every other allowlisted key's choices
    /// pass through unchanged.
    /// </summary>
    IReadOnlyList<SettingChoice>? ChoicesFor(AllowedSetting allowed, IReadOnlyList<SettingChoice> iconPackChoices) =>
        allowed.Key switch
        {
            "Station:Theme" => StationSettingsAllowlist.ThemeChoices(themeCatalog),
            "Station:IconPack" => iconPackChoices,
            _ => allowed.Choices,
        };

    /// <summary>
    /// Fetches <see cref="iconPackStore"/>'s current installed-pack SLUG set ONCE per request (SPEC
    /// F130.4, PLAN T303 review finding F2 — <see cref="IIconPackStore.GetAllSlugsAsync"/>, never the
    /// full-row <see cref="IIconPackStore.GetAllAsync"/>: the settings hot path needs nothing past the
    /// slug) and shapes it via <see cref="StationSettingsAllowlist.IconPackChoices"/> —
    /// <see cref="Get"/>/<see cref="Put"/> each call this exactly once, before building their own
    /// per-key <see cref="SettingDto"/> list, rather than a per-entry re-fetch inside
    /// <see cref="ChoicesFor"/> (which runs once per ALLOWLISTED KEY, not once per request).
    ///
    /// <para>
    /// <see cref="iconPackStore"/> ITSELF IS REQUIRED (review finding F5 — no nullable/optional
    /// fallback: <c>Program.cs</c>'s own <c>StationSettingsHostingExtensions.AddIconPackStore</c> call
    /// registers the real, Postgres-backed <see cref="IIconPackStore"/> singleton unconditionally, so a
    /// genuinely missing registration surfaces as a DI resolution failure the moment ASP.NET Core
    /// activates this controller for its first request, same as every other constructor dependency
    /// here). This method's own try/catch instead handles a REACHABLE-but-failing store — a transient
    /// <c>station.icon_pack</c> outage — by degrading <c>Station:IconPack</c>'s own choices to
    /// house-icons-only rather than letting the whole settings page 500 (
    /// <see cref="StationSettingsAllowlist.IconPackChoices"/>'s own house-icons-first choice, see that
    /// method's own remarks, is what makes this a WORKING admin-ui dropdown on the degrade path rather
    /// than the admin-ui <c>ChoiceSettingControl</c>'s own "no choices available" alert; mirrors
    /// <see cref="ThemeCatalog.ReloadOwnerThemesAsync"/>'s own "an unreachable store degrades,
    /// WARN-logged" offline-floor posture, applied here per-request instead of once at boot since
    /// <see cref="IIconPackStore"/> carries no in-memory warm cache of its own — SPEC F130's own "ships
    /// dark, thin repository" shape, unlike <see cref="ThemeCatalog"/>). Every OTHER allowlisted key
    /// must still read/write normally even on a transient <c>station.icon_pack</c> outage — an operator
    /// fixing an unrelated setting has no business being blocked by one unavailable pack listing;
    /// <c>Station:IconPack</c>'s own choices are simply narrowed to house icons alone that request,
    /// exactly the same shape a fresh station with zero packs installed already renders.
    /// </para>
    /// </summary>
    async Task<IReadOnlyList<SettingChoice>> IconPackChoicesAsync(CancellationToken ct)
    {
        try
        {
            return StationSettingsAllowlist.IconPackChoices(await iconPackStore.GetAllSlugsAsync(ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Icon pack listing unavailable for Station:IconPack's own choices — degrading to house icons only");
            return StationSettingsAllowlist.IconPackChoices([]);
        }
    }

    /// <summary>
    /// The 409 body for a version-guard conflict (gh-#486) — <see cref="SettingsProblemTypes.VersionConflict"/>
    /// lets the admin UI tell this apart from any other failure shape without parsing
    /// <see cref="ProblemDetails.Detail"/> text, and refetch + tell the operator their view was
    /// stale rather than silently merging.
    /// </summary>
    static ProblemDetails VersionConflictProblem(string key) => new()
    {
        Type   = SettingsProblemTypes.VersionConflict,
        Status = StatusCodes.Status409Conflict,
        Title  = "Setting changed since you loaded it.",
        Detail = $"'{key}' was saved by another editor while this request was in flight. Reload and try again.",
    };

    /// <summary>
    /// Maps <see cref="SettingApplyMode"/> to the wire string the admin UI badges on (SPEC F44.3
    /// amends the F19.5 two-value enumeration to three): <c>"live"</c>, <c>"engine-restart"</c>, or
    /// <c>"enrichment"</c> ("applies at next enrichment").
    /// </summary>
    static string ApplyModeWireValue(SettingApplyMode mode) => mode switch
    {
        SettingApplyMode.Live => "live",
        SettingApplyMode.Enrichment => "enrichment",
        _ => "engine-restart",
    };

    /// <summary>
    /// Maps <see cref="SettingKind"/> to the wire string the admin UI dispatches its input
    /// control on. Shared by GET and PUT so the two response shapes can never drift apart.
    /// </summary>
    static string KindWireValue(SettingKind kind) => kind switch
    {
        SettingKind.Boolean => "boolean",
        SettingKind.NumberList => "number-list",
        SettingKind.String => "string",
        SettingKind.Choice => "choice",
        _ => "number",
    };

    /// <summary>
    /// Reads a NumberList setting from configuration by collecting the ASP.NET Core indexed
    /// child keys (<c>key:0</c>, <c>key:1</c>, …) and serialising them as a JSON array string
    /// (e.g. <c>"[1,2]"</c>).
    ///
    /// <see cref="IConfiguration"/> represents arrays as indexed sub-keys, not as a single
    /// scalar at the parent key.  <c>configuration[key]</c> therefore returns null for a list;
    /// this helper reconstructs the array for display in <c>GET /api/settings</c> and the
    /// PUT response body.
    ///
    /// Returns <see cref="string.Empty"/> when the section has no children (no override and no
    /// default configured via indexed keys) so the UI can detect an empty/unconfigured list.
    /// </summary>
    static string GetNumberListJson(IConfiguration configuration, string key)
    {
        var children = configuration.GetSection(key).GetChildren().ToList();
        if (children.Count == 0) return string.Empty;

        var values = children
            .Where(c => long.TryParse(c.Value, out _))
            .Select(c => long.Parse(c.Value!))
            .ToList();

        return values.Count == 0 ? string.Empty : JsonSerializer.Serialize(values);
    }
}
