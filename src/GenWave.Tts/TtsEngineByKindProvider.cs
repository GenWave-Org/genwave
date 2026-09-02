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

                // PLAN T396 ruling (SPEC F158.3 — "no render at air time, ever"; T390 review carry-
                // forward 3): SegmentKind.Ad is a real, defined name (F158.1's Abstractions append),
                // so IsDefinedSegmentKindName above legally accepts the JSON key — and
                // SettingValidator.IsValidEngineByKindMap accepts it too (that check's own remarks
                // pin why it stays generic). But an ad spot NEVER reaches this router's per-kind
                // routing at all: it renders OFFLINE, through the widened crosstalk-mixer assembler
                // (PLAN T401), never through FallbackTtsSynthesizer. An operator-set "Ad" override
                // would therefore sit here FOREVER, accepted and silently never consulted — worse
                // than the unknown-kind WARN just above, which at least tells the operator their key
                // was rejected outright. This provider is the one place that actually KNOWS whether a
                // kind has a live per-kind TTS consumer, so it is the one place that rejects; teaching
                // the generic, shape-only settings validator this same domain fact would mean every
                // future SegmentKind that opts out of per-kind TTS routing needs a SettingValidator
                // edit just to keep accepting a config value that is genuinely, harmlessly inert.
                if (kind == SegmentKind.Ad)
                {
                    logger.LogWarning(
                        "Tts:EngineByKind entry for '{Kind}' is accepted by settings validation but never " +
                        "applies — ad spots render offline (SPEC F158.3), never through per-kind TTS " +
                        "routing; ignoring it", kind);
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
