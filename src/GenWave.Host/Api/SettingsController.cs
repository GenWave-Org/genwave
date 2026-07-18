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
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public sealed class SettingsController(
    IConfiguration configuration,
    IStationSettingsStore store,
    SettingValidator validator,
    ILogger<SettingsController> logger) : ControllerBase
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

        var items = StationSettingsAllowlist.All.Select(allowed =>
        {
            var rawValue  = allowed.Kind == SettingKind.NumberList
                ? GetNumberListJson(configuration, allowed.Key)
                : configuration[allowed.Key] ?? string.Empty;
            var source    = overrideKeys.ContainsKey(allowed.Key) ? "override" : "default";
            var applyMode = ApplyModeWireValue(allowed.ApplyMode);
            var kind      = allowed.Kind switch { SettingKind.Boolean => "boolean", SettingKind.NumberList => "number-list", SettingKind.String => "string", _ => "number" };
            return new SettingDto(allowed.Key, rawValue, source, applyMode, kind, allowed.Unit);
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

        // Validate all entries first — reject the entire request on the first error
        // so the caller gets a clear diagnostic and nothing is partially written.
        var errors = new List<string>();
        foreach (var update in updates)
        {
            if (string.IsNullOrWhiteSpace(update.Key))
            {
                errors.Add("Each entry must have a non-empty key.");
                continue;
            }

            var error = validator.Validate(update.Key, update.Value ?? string.Empty);
            if (error is not null)
                errors.Add(error);
        }

        // Cross-field check: run only when all per-key validations pass so error messages
        // are not conflated with parse failures.
        if (errors.Count == 0)
        {
            var batch = updates
                .Where(u => u.Key is not null)
                .ToDictionary(u => u.Key!, u => u.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);

            var crossFieldError = validator.ValidateBatch(batch);
            if (crossFieldError is not null)
                errors.Add(crossFieldError);
        }

        if (errors.Count > 0)
        {
            var problem = new ValidationProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title  = "One or more settings values are invalid.",
            };
            problem.Errors["settings"] = errors.ToArray();
            return BadRequest(problem);
        }

        // All valid — persist each one.  WriteAsync raises the reload token after each write;
        // IOptionsMonitor re-binds automatically so api-side live knobs take effect immediately.
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

            await store.WriteAsync(update.Key, valueToStore, ct);

            logger.LogInformation(
                "Setting persisted: key={Key} applyMode={ApplyMode}",
                update.Key,
                allowed.ApplyMode);
        }

        // Build the response so the caller knows the applyMode and kind/unit for each written key.
        var overrideKeys = await store.ReadAllAsync(ct);
        var result = updates.Select(u =>
        {
            var allowed   = StationSettingsAllowlist.ByKey[u.Key];
            var rawValue  = allowed.Kind == SettingKind.NumberList
                ? (GetNumberListJson(configuration, u.Key) is { Length: > 0 } json ? json : u.Value ?? string.Empty)
                : configuration[u.Key] ?? u.Value;
            var source    = overrideKeys.ContainsKey(u.Key) ? "override" : "default";
            var applyMode = ApplyModeWireValue(allowed.ApplyMode);
            var kind      = allowed.Kind switch { SettingKind.Boolean => "boolean", SettingKind.NumberList => "number-list", SettingKind.String => "string", _ => "number" };
            return new SettingDto(u.Key, rawValue, source, applyMode, kind, allowed.Unit);
        }).ToList();

        return Ok(result);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

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
