using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using GenWave.Host.Configuration;

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
    // Resolves each SettingDto's label/help/group/choice copy for the request's culture (SPEC
    // F205.3). Required: a missing registration fails at activation instead of silently serving
    // label = key.
    SettingCopy settingCopy,
    // Resolves every choice-kind key's live choice list (SPEC F205.7, STORY-479, PLAN T580).
    // Required (T580 review finding F2): a dropped DI registration must fail at activation, never
    // silently degrade every choice-kind key to ResolvedChoices.Failed forever with nothing logged
    // — the same fail-closed posture as settingCopy immediately above. Program.cs registers the
    // real SettingChoiceResolver together with every IChoiceProbe/ThemeCatalog/IIconPackStore it
    // needs; unit tests pass GenWave.Host.Tests.Support.TestSettingChoiceResolver.Default() (a real
    // resolver with zero registered probes) instead of relying on a controller-owned fallback.
    ISettingChoiceResolver choiceResolver) : ControllerBase
{
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
        var currentValues = StationSettingsAllowlist.All.ToDictionary(
            a => a.Key, RawValue, StringComparer.OrdinalIgnoreCase);
        var resolvedChoices = await choiceResolver.ResolveAsync(currentValues, ct);

        var items = StationSettingsAllowlist.All.Select(allowed =>
        {
            var source  = overrideKeys.ContainsKey(allowed.Key) ? "override" : "default";
            var version = versions.GetValueOrDefault(allowed.Key, 0);
            return BuildDto(allowed, currentValues[allowed.Key], source, version, resolvedChoices);
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
        var currentValues = updates.ToDictionary(
            u => u.Key,
            u => RawValueAfterWrite(StationSettingsAllowlist.ByKey[u.Key], u.Value),
            StringComparer.OrdinalIgnoreCase);
        var resolvedChoices = await choiceResolver.ResolveAsync(currentValues, ct);

        var result = updates.Select(u =>
        {
            var allowed = StationSettingsAllowlist.ByKey[u.Key];
            var source  = overrideKeys.ContainsKey(u.Key) ? "override" : "default";
            var version = versions.GetValueOrDefault(u.Key, 0);
            return BuildDto(allowed, currentValues[u.Key], source, version, resolvedChoices);
        }).ToList();

        return Ok(result);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds one <see cref="SettingDto"/> row — the ONE place GET's and PUT's response shapes are
    /// assembled (SPEC F205.3, STORY-477, PLAN T574), so the two can never drift apart the way
    /// <see cref="ApplyModeWireValue"/>/<see cref="KindWireValue"/> already guarantee for the fields
    /// they cover. <paramref name="rawValue"/>/<paramref name="source"/>/<paramref name="version"/>
    /// are resolved by the caller (GET reads every allowlisted key; PUT reads only the keys just
    /// written) — everything else (label/help/group copy, range, resolved choice labels) is read
    /// straight off <paramref name="allowed"/> and <see cref="settingCopy"/>, identically for both.
    /// </summary>
    SettingDto BuildDto(
        AllowedSetting allowed, string rawValue, string source, long version,
        IReadOnlyDictionary<string, ResolvedChoices> resolvedChoices)
    {
        var resolved = resolvedChoices.GetValueOrDefault(allowed.Key);
        return new(
            allowed.Key,
            rawValue,
            source,
            ApplyModeWireValue(allowed.ApplyMode),
            KindWireValue(allowed.Kind),
            allowed.Unit,
            settingCopy.Label(allowed.Key),
            settingCopy.Help(allowed.Key),
            new SettingGroupDto(settingCopy.GroupId(allowed.Group), settingCopy.GroupLabel(allowed.Group)),
            allowed.Min,
            allowed.Max,
            resolved?.Choices,
            version,
            resolved?.Stale ?? false,
            resolved?.Failed ?? false);
    }

    /// <summary>
    /// The key's current effective value, straight off <see cref="configuration"/> — the same shape
    /// <see cref="Get"/> has always reported, now also reused to build the <c>currentValues</c> map
    /// <see cref="choiceResolver"/> needs to decide whether a saved choice value has fallen off its
    /// live source's list (SPEC F205.7a, STORY-479, PLAN T580).
    /// </summary>
    string RawValue(AllowedSetting allowed) =>
        allowed.Kind == SettingKind.NumberList
            ? GetNumberListJson(configuration, allowed.Key)
            : configuration[allowed.Key] ?? string.Empty;

    /// <summary>
    /// <see cref="RawValue"/>'s PUT-time sibling: <c>store.WriteAsync</c> raises the reload token
    /// asynchronously, so <see cref="configuration"/> is not guaranteed to already reflect a write
    /// this same request just made — <paramref name="proposedValue"/> (the value just accepted by
    /// <see cref="Put"/>'s own validation) is the fallback instead of <see cref="RawValue"/>'s empty
    /// string, so the response always echoes back what was actually written.
    /// </summary>
    string RawValueAfterWrite(AllowedSetting allowed, string proposedValue) =>
        allowed.Kind == SettingKind.NumberList
            ? (GetNumberListJson(configuration, allowed.Key) is { Length: > 0 } json ? json : proposedValue)
            : configuration[allowed.Key] ?? proposedValue;

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
