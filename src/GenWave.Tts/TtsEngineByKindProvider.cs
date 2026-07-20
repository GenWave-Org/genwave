namespace GenWave.Tts;

using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GenWave.Core.Domain;

/// <summary>
/// Live settings subscriber for <c>Tts:EngineByKind</c> (SPEC F70.3, STORY-191). Subscribes to
/// <see cref="IOptionsMonitor{TOptions}.OnChange"/> once at construction and rebuilds an immutable
/// <see cref="TtsEngineOverrideMap"/> snapshot on every change, mirroring
/// <see cref="SpeechCorrectionProvider"/>'s own shape — a map saved through
/// <c>PUT /api/settings</c> reaches the very next render with no api restart, and the (rare)
/// malformed-JSON/unknown-key/null-or-blank-engine/unknown-engine case degrades that one entry (or
/// the whole map, for malformed JSON) to empty with one WARN, rather than throwing or re-logging on
/// every subsequent render.
///
/// <see cref="Current"/> is a plain field read (backed by <see langword="volatile"/>) — every
/// render reads it fresh; nothing here ever hands out a stale snapshot captured at some earlier
/// point in the process lifetime. Registered as a singleton
/// (<see cref="TtsServiceCollectionExtensions.AddGenWaveTts"/>) so the one subscription lives for
/// the process lifetime.
/// </summary>
public sealed class TtsEngineByKindProvider : IDisposable
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    readonly IDisposable? subscription;

    volatile TtsEngineOverrideMap current;

    public TtsEngineByKindProvider(
        IOptionsMonitor<TtsEngineByKindOptions> optionsMonitor,
        ILogger<TtsEngineByKindProvider> logger)
    {
        current = Build(optionsMonitor.CurrentValue, logger);
        subscription = optionsMonitor.OnChange(updated => current = Build(updated, logger));
    }

    /// <summary>The current immutable snapshot of the operator's per-kind engine overrides.</summary>
    public TtsEngineOverrideMap Current => current;

    static TtsEngineOverrideMap Build(TtsEngineByKindOptions options, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(options.EngineByKind))
            return TtsEngineOverrideMap.Empty;

        try
        {
            // string? on the value: STJ deserializes a JSON null property value ({"StationId":null})
            // into a CLR null despite the non-nullable annotation — a Dictionary<string, string> here
            // would let that null slip past catch(JsonException) and NRE at engine.Trim() below.
            var raw = JsonSerializer.Deserialize<Dictionary<string, string?>>(options.EngineByKind, JsonOptions);
            if (raw is null)
                return TtsEngineOverrideMap.Empty;

            var byKind = new Dictionary<SegmentKind, string>();
            foreach (var (kindName, engine) in raw)
            {
                // SettingValidator already guards this shape on the write path (F70.3) — parse
                // defensively here too, so a value that somehow bypassed it (or a stale DB row from
                // before the validator existed) degrades this ONE entry rather than the whole map.
                // IsDefinedSegmentKindName rejects numeric strings ("0") that Enum.TryParse alone
                // would silently accept as the underlying int value.
                if (!IsDefinedSegmentKindName(kindName) || !Enum.TryParse<SegmentKind>(kindName, ignoreCase: true, out var kind))
                {
                    logger.LogWarning(
                        "Tts:EngineByKind entry '{KindName}' is not a known speech kind; ignoring it", kindName);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(engine))
                {
                    logger.LogWarning(
                        "Tts:EngineByKind entry for '{Kind}' has no engine value; ignoring it", kind);
                    continue;
                }

                var normalizedEngine = engine.Trim().ToLowerInvariant();
                if (normalizedEngine is not (DependencyNames.Kokoro or DependencyNames.Piper))
                {
                    logger.LogWarning(
                        "Tts:EngineByKind entry for '{Kind}' names an unknown engine '{Engine}'; ignoring it",
                        kind, engine);
                    continue;
                }

                byKind[kind] = normalizedEngine;
            }

            return TtsEngineOverrideMap.Create(byKind);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(
                ex, "Tts:EngineByKind is not valid JSON; no per-kind engine overrides applied until it is fixed");
            return TtsEngineOverrideMap.Empty;
        }
    }

    // Enum.TryParse<SegmentKind> alone accepts numeric strings ("0" parses to SegmentKind.StationId,
    // its underlying int value) — reject anything that isn't one of the enum's actual NAMES first,
    // mirroring SettingValidator.IsValidEngineByKindMap's write-path guard.
    static bool IsDefinedSegmentKindName(string name) =>
        Enum.GetNames<SegmentKind>().Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));

    public void Dispose() => subscription?.Dispose();
}
