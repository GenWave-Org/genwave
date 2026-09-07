namespace GenWave.Host.Catalog;

using System.Text.Json;
using System.Text.RegularExpressions;
using GenWave.Host.Configuration;
using GenWave.Tts;

/// <summary>
/// The hardened, null-tolerant deserializer for a <see cref="CatalogVoicePackManifest"/> (SPEC
/// F164.1/F164.3/F164.5, STORY-395/396/398, PLAN T413) — mirrors
/// <see cref="CatalogAvatarPackManifestSerializer"/>'s own idiom: reads into an ephemeral, all-nullable
/// projection first, rather than deserializing straight into non-nullable properties (which
/// <c>System.Text.Json</c> would silently leave default for a missing field despite the C# type saying
/// otherwise). A malformed document, or ANY declared field failing its own shape/length gate, degrades
/// the WHOLE manifest to <see langword="null"/> (never throws, never partially admits a pack) — the
/// same "a pack IS its files" all-or-nothing posture every sibling manifest parser already holds.
///
/// <para>
/// <b>engine is a SHAPE gate only here, never a closed-set membership check (T413 review round 2
/// finding B2 — reversing round 1 finding F2's own narrowing).</b> <c>engine</c> only has to look like
/// a safe lowercase token (<see cref="EngineFormat"/>) within <see cref="MaxEngineLength"/> — the
/// declared value is PRESERVED on <see cref="CatalogVoicePackManifest.Engine"/> verbatim, never
/// narrowed to <see cref="DependencyNames.Kokoro"/>. Two SEPARATE refusals both need the SAME
/// machine-readable <c>not_supported_engine</c> 400 SPEC F164.2 requires — "this database can't store
/// this engine at all" (mirrors db/45's own <c>check (engine in ('kokoro'))</c>) and "this station's
/// actual primary engine doesn't match" — and only <c>Api.VoicePackController</c> can produce that one
/// shared body for both; a parse-time <see langword="null"/> here would instead degrade EITHER case to
/// the wrong "malformed manifest" 400. So both checks now live at the controller, right next to each
/// other, right before the collision check. Likewise <c>preview</c> is validated here only for its own
/// SHAPE (a safe <c>&lt;slug-like-stem&gt;.preview.mp3</c> file name) — the controller is the one that
/// knows the catalog entry's actual slug, so it alone can confirm <c>preview</c> equals
/// <c>&lt;slug&gt;.preview.mp3</c> exactly, the same "context this parser doesn't have" reasoning
/// <c>Api.AvatarPackController.BuildRawItems</c> already applies to cross-checking declared items
/// against fetched assets.
/// </para>
///
/// <para>
/// <b>synthetic/sourceRef (SPEC F164.3) are gate conditions, not stored fields.</b> <c>synthetic</c>
/// must be present and <see langword="true"/>; <c>sourceRef</c> must be absent or JSON <c>null</c> —
/// either violated degrades the whole manifest to <see langword="null"/> rather than surfacing as a
/// property on <see cref="CatalogVoicePackManifest"/>, because nothing downstream of a successfully
/// parsed manifest ever needs to re-ask "was this synthetic".
/// </para>
///
/// <para>
/// NO <c>Serialize</c> (mirrors every sibling manifest serializer's own asymmetry): this app never
/// WRITES a voice-pack manifest — packs are catalog-authored content this app only ever reads through
/// the guarded proxy door (SPEC F90.2-F90.4). The durable write <c>Api.VoicePackController.Install</c>
/// performs stores this manifest's own already-validated JSON verbatim as
/// <c>station.voice_pack.definition</c>, never a re-serialized copy built from this type.
/// </para>
/// </summary>
public static partial class CatalogVoicePackManifestSerializer
{
    /// <summary>A pack name is a short display label, never a sentence.</summary>
    public const int MaxPackNameLength = 64;

    /// <summary>SPEC F164.1 — a pack ships at most this many voices.</summary>
    public const int MaxVoicesPerPack = 16;

    /// <summary>One voice blends in at most this many others.</summary>
    public const int MaxBlendPerVoice = 8;

    /// <summary>
    /// The install-time cap <c>SettingValidator.VoiceIdFormat()</c>'s own comment documents as a PROMISE
    /// this parser must land — see <c>SettingValidator.cs</c>'s remarks on that regex. The database's own
    /// <c>voice_id</c> CHECK (db/45) has no length cap by design; this is purely filename hygiene at the
    /// one layer that turns a voice id into a path segment.
    /// </summary>
    public const int MaxVoiceIdLength = 64;

    /// <summary>Defense in depth (T413 review round 1 finding F6): an untrusted, attacker-controlled
    /// field never gets an unbounded length before this parser even reaches <see cref="EngineFormat"/>'s
    /// own shape check — a length gate belongs beside every OTHER untrusted string field this parser
    /// reads, never only the ones some LATER check happens to shorten first (T413 review round 2
    /// finding B2: that later check is now the controller's own closed-set gate, which never even sees
    /// a token this parser has already refused).</summary>
    public const int MaxEngineLength = 32;

    /// <summary>A safe lowercase engine token — SHAPE only (T413 review round 2 finding B2): whether
    /// this specific value is one <c>Api.VoicePackController</c> actually supports is that controller's
    /// own closed-set gate, not this parser's concern. Starts with a letter so an engine name can never
    /// collide with a bare digit/hyphen run.</summary>
    [GeneratedRegex("\\A[a-z][a-z0-9-]*\\z")]
    private static partial Regex EngineFormat();

    /// <summary>A safe preview file name — a slug-like stem plus the fixed <c>.preview.mp3</c> suffix
    /// (SPEC F164.4). The controller separately confirms the stem equals the catalog entry's own slug;
    /// this only confirms the SHAPE is one a filesystem/URL can safely carry.</summary>
    [GeneratedRegex("\\A[a-z0-9]+(-[a-z0-9]+)*\\.preview\\.mp3\\z")]
    private static partial Regex PreviewFormat();

    static readonly string[] ValidGenders = ["female", "male", "neutral"];
    static readonly string[] ValidAges = ["child", "young", "adult", "senior"];

    /// <summary>Case-insensitive read options (mirrors every sibling manifest serializer's own untrusted-parsing options) — leniency on the READ side only.</summary>
    static readonly JsonSerializerOptions ParseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static CatalogVoicePackManifest? Deserialize(string json)
    {
        CatalogVoicePackManifestJson? raw;
        try
        {
            raw = JsonSerializer.Deserialize<CatalogVoicePackManifestJson>(json, ParseOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (raw is null)
            return null;

        if (raw is not { PackName: { Length: > 0 and <= MaxPackNameLength } packName } || string.IsNullOrWhiteSpace(packName))
            return null;

        // SHAPE only (T413 review round 2 finding B2) — closed-set membership (db/45's own
        // `check (engine in ('kokoro'))`) and the station's-own-primary-engine match are BOTH the
        // controller's job now, so a "piper" manifest parses cleanly here and gets the SAME
        // not_supported_engine 400 body an engine-mismatch does, never this parser's own "malformed
        // manifest" 400.
        if (raw is not { Engine: { Length: > 0 and <= MaxEngineLength } engine } || !EngineFormat().IsMatch(engine))
            return null;

        // synthetic must be DECLARED true (SPEC F164.3) — a missing field defaults to null here, not
        // false, so an omitted synthetic is refused exactly like an explicit `false`.
        if (raw.Synthetic is not true)
            return null;

        // sourceRef must be absent or JSON null (SPEC F164.3) — System.Text.Json's Nullable<JsonElement>
        // converter already folds a JSON `null` literal to a non-present Nullable<JsonElement>, so
        // "absent" and "explicit null" are indistinguishable here, and both are accepted; ANY other
        // value (string, object, number, array, even an empty string) fails the whole manifest.
        if (raw.SourceRef is not null)
            return null;

        if (raw is not { Preview: { Length: > 0 } preview } || !PreviewFormat().IsMatch(preview))
            return null;

        if (raw.Voices is not { Count: > 0 and <= MaxVoicesPerPack } rawVoices)
            return null;

        var voices = new List<CatalogVoicePackVoice>(rawVoices.Count);
        var seenVoiceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rawVoice in rawVoices)
        {
            if (TryParseVoice(rawVoice) is not { } voice)
                return null;

            if (!seenVoiceIds.Add(voice.VoiceId))
                return null; // duplicate voice id within one manifest

            voices.Add(voice);
        }

        return new CatalogVoicePackManifest(packName, engine, preview, voices);
    }

    static CatalogVoicePackVoice? TryParseVoice(CatalogVoicePackVoiceJson? raw)
    {
        if (!TryParseVoiceId(raw?.VoiceId, out var voiceId))
            return null;

        var expectedFile = voiceId + ".pt";

        // `file` is OPTIONAL on the wire — absent/blank derives to VoiceId + ".pt"; PRESENT means it
        // must equal that exact derivation, never silently corrected to some other declared name.
        if (!string.IsNullOrWhiteSpace(raw?.File) && !string.Equals(raw.File, expectedFile, StringComparison.Ordinal))
            return null;

        if (raw?.Gender is { Length: > 0 } gender && !ValidGenders.Contains(gender))
            return null;

        var normalizedGender = string.IsNullOrWhiteSpace(raw?.Gender) ? null : raw.Gender;

        if (raw?.Age is { Length: > 0 } age && !ValidAges.Contains(age))
            return null;

        var normalizedAge = string.IsNullOrWhiteSpace(raw?.Age) ? null : raw.Age;

        IReadOnlyList<CatalogVoicePackBlend>? blend = null;
        if (raw?.Blend is { } rawBlend)
        {
            if (rawBlend.Count is 0 or > MaxBlendPerVoice)
                return null;

            var parsedBlend = new List<CatalogVoicePackBlend>(rawBlend.Count);
            foreach (var rawBlendVoice in rawBlend)
            {
                if (TryParseBlend(rawBlendVoice) is not { } blendVoice)
                    return null;

                parsedBlend.Add(blendVoice);
            }

            blend = parsedBlend;
        }

        return new CatalogVoicePackVoice(voiceId, expectedFile, normalizedGender, normalizedAge, blend);
    }

    static CatalogVoicePackBlend? TryParseBlend(CatalogVoicePackBlendJson? raw)
    {
        if (!TryParseVoiceId(raw?.VoiceId, out var voiceId))
            return null;

        // (0, 1] — a zero or negative weight contributes nothing (or inverts), and a weight above 1
        // would let one blend component dominate beyond a normalized mix; NaN/Infinity fail every
        // comparison below and so are refused along with everything else out of range.
        if (raw?.Weight is not { } weight || weight <= 0.0 || weight > 1.0)
            return null;

        return new CatalogVoicePackBlend(voiceId, weight);
    }

    /// <summary>
    /// Shared by a top-level voice and a blend component: <see cref="SettingValidator.VoiceIdFormat"/>
    /// (T417's own regex, reused rather than re-declared — see this class's own remarks and T413's
    /// builder brief addendum) plus this parser's own explicit <see cref="MaxVoiceIdLength"/> cap.
    /// </summary>
    static bool TryParseVoiceId(string? raw, out string voiceId)
    {
        if (raw is { Length: > 0 and <= MaxVoiceIdLength } candidate && SettingValidator.VoiceIdFormat().IsMatch(candidate))
        {
            voiceId = candidate;
            return true;
        }

        voiceId = string.Empty;
        return false;
    }

    /// <summary>Ephemeral, all-nullable projection of an untrusted voice-pack manifest document.</summary>
    sealed record CatalogVoicePackManifestJson
    {
        public string? PackName { get; init; }
        public string? Engine { get; init; }
        public bool? Synthetic { get; init; }
        public JsonElement? SourceRef { get; init; }
        public string? Preview { get; init; }
        public IReadOnlyList<CatalogVoicePackVoiceJson>? Voices { get; init; }
    }

    /// <summary>Ephemeral, all-nullable projection of one raw <c>voices[]</c> element.</summary>
    sealed record CatalogVoicePackVoiceJson
    {
        public string? VoiceId { get; init; }
        public string? File { get; init; }
        public string? Gender { get; init; }
        public string? Age { get; init; }
        public IReadOnlyList<CatalogVoicePackBlendJson>? Blend { get; init; }
    }

    /// <summary>Ephemeral, all-nullable projection of one raw <c>blend[]</c> element.</summary>
    sealed record CatalogVoicePackBlendJson
    {
        public string? VoiceId { get; init; }
        public double? Weight { get; init; }
    }
}
