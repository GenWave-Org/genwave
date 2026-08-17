using System.Data.Common;
using System.Text.Json;
using GenWave.MediaLibrary.Station;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace GenWave.Host.Configuration;

/// <summary>
/// <see cref="IConfigurationProvider"/> that reads allowlisted keys from <c>station.settings</c>
/// (the overlay DB store) via the <c>station_svc</c> connection. Registered after env/appsettings
/// so stored values win over file/env defaults.
///
/// A stored value is exposed as a flat configuration key (e.g. <c>Loudness:TargetLufs</c>)
/// whose string representation comes from the JSONB scalar value.
///
/// Call <see cref="Reload"/> (or expose via <see cref="IStationSettingsStore.WriteAsync"/>) to
/// raise the change token and trigger <see cref="Microsoft.Extensions.Options.IOptionsMonitor{T}"/>
/// re-binding without an API restart.
///
/// gh-#406 slice 5: the raw <c>station.settings</c> row I/O this class used to open directly via
/// <c>NpgsqlConnection</c> now lives in <see cref="StationSettingsRepository"/>
/// (<see cref="StationSettingsRepository.ReadAllForBoot"/> documents why that method — not
/// <see cref="StationSettingsRepository.ReadAllAsync"/> — is the one this class calls). Every other
/// behavior below (allowlist filtering, array expansion, the degrade-to-empty-overlay posture,
/// stderr diagnostics) stays exactly as it was.
/// </summary>
public class StationSettingsConfigurationProvider : IConfigurationProvider, IDisposable
{
    readonly string connectionString;
    readonly bool expectNoStore;
    readonly StationSettingsRepository repository;

    // The mutable data bag surfaced to IConfiguration.
    IDictionary<string, string?> data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    // Change-token infrastructure: we swap a new CancellationTokenSource each reload.
    CancellationTokenSource cts = new();

    // expectNoStore (gh-#412): true only when the host explicitly declared — via
    // StationSettingsHostingExtensions.ExpectNoStoreKey — that running without a Station store is
    // deliberate (e.g. the SEAMS.md generator's DB-free composition snapshot), so the
    // empty-connection-string stderr diagnostic below is suppressed. Failures with a REAL
    // connection string still print regardless.
    public StationSettingsConfigurationProvider(string connectionString, bool expectNoStore = false)
    {
        this.connectionString = connectionString;
        this.expectNoStore = expectNoStore;

        // Constructed directly here, not resolved through DI: Load() (below) is called by the
        // configuration system while Microsoft.Extensions.Configuration.IConfigurationBuilder itself
        // is being built, before WebApplicationBuilder.Build() ever creates a container capable of
        // constructing — let alone injecting — a repository. StationSettingsRepository's
        // plain-connection-string ctor exists precisely for this pre-DI boot path (see that class's
        // own remarks, gh-#406 slice 3/4/5).
        repository = new StationSettingsRepository(connectionString);
    }

    // ── IConfigurationProvider ─────────────────────────────────────────────

    public bool TryGet(string key, out string? value)
        => data.TryGetValue(key, out value);

    public void Set(string key, string? value)
        => data[key] = value;

    public IChangeToken GetReloadToken()
        => new CancellationChangeToken(cts.Token);

    public IEnumerable<string> GetChildKeys(IEnumerable<string> earlierKeys, string? parentPath)
    {
        var prefix = parentPath is null ? string.Empty : parentPath + ConfigurationPath.KeyDelimiter;
        return data
            .Where(kv => kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(kv =>
            {
                var rest = kv.Key[prefix.Length..];
                var sep = rest.IndexOf(ConfigurationPath.KeyDelimiter, StringComparison.Ordinal);
                return sep < 0 ? rest : rest[..sep];
            })
            .Concat(earlierKeys)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Called by the configuration system on startup (and again after each <see cref="Reload"/>).
    /// Loads all allowlisted rows from <c>station.settings</c>. Failures are swallowed so a
    /// cold-start without the DB (or without the station schema yet) never crashes the host —
    /// env/appsettings defaults survive.
    ///
    /// <c>virtual</c> so tests can override with a seeded in-memory implementation.
    /// </summary>
    public virtual void Load()
    {
        // Empty connection string means no station DB is configured (e.g. local dev or tests).
        // Treat the same as a DB that is temporarily unreachable: the overlay is empty and
        // env/appsettings defaults apply.  This guard prevents the Npgsql library from throwing
        // InvalidOperationException ("ConnectionString not initialized") before attempting a
        // TCP connection — an exception type that the DbException catch below does not cover.
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Match the DbException catch below: surface the degradation so an accidentally
            // empty Station connection string in a real deploy is observable, not silent —
            // unless the host explicitly opted out (expectNoStore, gh-#412), in which case
            // the absence is by design and the line is pure noise.
            if (!expectNoStore)
                Console.Error.WriteLine("[station-settings] no Station connection string; using config defaults");
            data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        var loaded = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var rows = repository.ReadAllForBoot();
            foreach (var (key, jsonValue) in rows)
            {
                if (!StationSettingsAllowlist.ByKey.ContainsKey(key))
                    continue;   // paranoid guard: skip anything not on the allowlist

                var scalar = ExtractScalar(jsonValue);
                if (scalar is not null)
                    loaded[key] = scalar;
                else
                    ExtractArrayItems(loaded, key, jsonValue);
            }
        }
        catch (DbException ex)
        {
            // No station schema yet, wrong password, DB down — none of these should prevent boot.
            // Defaults from env/appsettings continue to apply; the overlay is empty until the
            // DB is reachable and station.settings is populated.
            // A logger is not injectable here (provider builds before DI), so we surface the
            // failure via stderr so operators can diagnose without exposing connection secrets.
            //
            // Catches System.Data.Common.DbException — the provider-neutral ADO.NET base type
            // Npgsql.NpgsqlException itself derives from — rather than NpgsqlException directly:
            // this class carries no Npgsql reference at all now that StationSettingsRepository owns
            // the Postgres specifics (gh-#406 slice 5), and DbException catches every failure the
            // original catch did (and nothing broader).
            Console.Error.WriteLine($"[station-settings] overlay load failed; using config defaults: {ex.Message}");
        }

        data = loaded;
    }

    // ── Reload ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Reloads the settings from the database and raises the change token so
    /// <see cref="Microsoft.Extensions.Options.IOptionsMonitor{T}"/> re-binds.
    /// Safe to call from any thread.
    /// </summary>
    public void Reload()
    {
        Load();
        RaiseChangeToken();
    }

    void RaiseChangeToken()
    {
        var old = Interlocked.Exchange(ref cts, new CancellationTokenSource());
        old.Cancel();
        old.Dispose();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts a JSONB scalar (number, bool, string) to its configuration string representation.
    /// Returns null for complex types (objects/arrays) which are not valid config scalars.
    /// </summary>
    static string? ExtractScalar(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return root.ValueKind switch
            {
                JsonValueKind.String => root.GetString(),
                JsonValueKind.Number => root.GetRawText(),
                JsonValueKind.True   => "true",
                JsonValueKind.False  => "false",
                _ => null,   // objects/arrays/null are not valid flat config values
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Expands a JSONB array into indexed IConfiguration keys so the ASP.NET Core options
    /// binder can bind <c>IList&lt;T&gt;</c> properties such as <c>SafeScope.LibraryIds</c>.
    ///
    /// For example, a stored value of <c>[2, 3]</c> for key
    /// <c>Station:SafeScope:LibraryIds</c> is expanded to:
    /// <list type="bullet">
    ///   <item><c>Station:SafeScope:LibraryIds:0</c> = <c>"2"</c></item>
    ///   <item><c>Station:SafeScope:LibraryIds:1</c> = <c>"3"</c></item>
    /// </list>
    /// Non-array JSON or parse errors are silently skipped (the key simply has no overlay value).
    /// </summary>
    static void ExtractArrayItems(Dictionary<string, string?> target, string key, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array) return;

            var index = 0;
            foreach (var element in root.EnumerateArray())
            {
                var elementValue = element.ValueKind switch
                {
                    JsonValueKind.Number => element.GetRawText(),
                    JsonValueKind.String => element.GetString(),
                    JsonValueKind.True   => "true",
                    JsonValueKind.False  => "false",
                    _ => null,
                };
                if (elementValue is not null)
                    target[$"{key}:{index}"] = elementValue;
                index++;
            }
        }
        catch (JsonException) { }
    }

    public void Dispose() => cts.Dispose();
}
