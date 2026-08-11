namespace GenWave.Host.Shows;

using System.Text.Json;
using GenWave.Context;
using GenWave.Core.Domain;

/// <summary>
/// Parses and validates ONE show import manifest document (SPEC F118.1/F118.2, PLAN T254) — the show
/// kind's sibling of <see cref="Theming.ThemeManifestParser"/>, generalized down to
/// <see cref="ShowManifest"/>'s simpler <c>{name, tagline, flavor}</c> shape (see that type's own
/// remarks: a show manifest carries no embedded slug, unlike a theme's). <see cref="Api.ShowsController.Import"/>
/// is the only caller; every failure throws a <see cref="ShowManifestException"/> naming the manifest's
/// own <see cref="ShowManifestSource.Name"/> — deserialization IS the validation (SPEC F118.2's own F79
/// posture), so a malformed or over-budget body never becomes a persisted row.
///
/// <para>
/// <b>SCHEMA-MAJOR (SPEC F118.2) — a caller concern, not this method's.</b> Unlike
/// <see cref="Theming.ThemeManifestParser.Parse"/> (which has no separate shared gate type to lean on,
/// since Show has only ONE write route — see <see cref="Api.ShowsController"/>'s own F115.5 remarks on
/// why it needs no <c>ThemeWriteGate</c>-shaped type of its own), <see cref="ExtractSchemaVersion"/>
/// below IS still split out as its own step, mirroring <c>ThemeSchemaVersionGate</c>'s own two-parse
/// trick: <see cref="Api.ShowsController.Import"/> reads the raw <c>schemaVersion</c> field BEFORE ever
/// calling <see cref="Parse"/>, so a newer-major manifest is refused naming both versions even when the
/// rest of its shape is ALSO structurally invalid (a newer major is free to look nothing like today's
/// v1 shape) — the exact edge case a simpler "parse first, check <c>SchemaVersion</c> after" ordering
/// (<c>PersonaController.Import</c>'s own posture) would report as a generic structural-parse failure
/// instead. Kept as a small, Show-owned duplicate of that extraction rather than a shared cross-kind
/// type: Show and Theme version their OWN formats independently (both happen to be at major 1 today,
/// coincidentally, not because they share one version space), and Show has no second write route to
/// justify hoisting a shared gate the way <c>ThemeWriteGate</c> earned one (PLAN T207's own "genuinely
/// multi-phase pipeline… a hand-copy between two files had already drifted once" reasoning does not
/// apply here — there is only ever the one file).
/// </para>
///
/// <para>
/// <b>THE 2× HARD CAP (SPEC F115.1, F118.4) — STRICTLY greater-than, never <c>&gt;=</c>.</b>
/// <see cref="ShowBudgets"/> pins the AUTHORED-write 1× ceiling (name ≤60, tagline ≤120, flavor ≤400);
/// an import gets DOUBLE that headroom before this parser refuses it outright — a field over 2× its
/// budget is rejected, but a field sitting at EXACTLY 2× is accepted here. genwave-catalog's own
/// <c>tools/lint.py</c> (<c>check_show_field_budget</c>) enforces a stricter, INCLUSIVE <c>&gt;= 2×</c>
/// HARD tier (its own remarks record the deliberate asymmetry, PLAN T253) — a manifest sitting at
/// EXACTLY 2× a budget therefore fails catalog CI (so no legitimately PUBLISHED catalog entry can ever
/// actually reach this parser's own boundary) but would still be accepted by a direct file-upload
/// import, one character under this parser's own line. This asymmetry is intentional, not a bug to
/// reconcile: this parser's own hard line is the APP's independent floor, catalog CI's is the
/// CATALOG's — widening this parser to match lint.py's inclusive boundary would be duplicating a rule
/// that belongs to the other repo, not fixing a real gap (a hostile or hand-crafted file upload landing
/// EXACTLY on 2× is still bounded, just one character more generously than a published catalog entry
/// could ever legally be). ONE MORE ASYMMETRY (F3 review finding): this parser measures length via C#
/// <see cref="string.Length"/> — UTF-16 CODE UNITS — while <c>lint.py</c> measures via Python's
/// <c>len(str)</c> — UNICODE CODE POINTS; an astral character (outside the Basic Multilingual Plane,
/// e.g. most emoji) counts as ONE code point but TWO UTF-16 code units, so this parser's own count can
/// run higher than catalog CI's for the identical text. This fails CLOSED, never open: the app is
/// STRICTER on that class of input, not more permissive, so a manifest heavy on astral characters may
/// still refuse here (400) even though it passed catalog CI clean — a false rejection, never a false
/// acceptance, and therefore not a security gap, only a UX rough edge worth knowing about if this ever
/// needs reconciling.
/// </para>
///
/// <para>
/// <b>FLAVOR/NAME HYGIENE — the T249-recorded constraint
/// (<c>LlmPromptBuilder.BuildShowFlavorPatterLine</c>'s own remarks), decided HERE.</b> That method's
/// prompt line is DELIBERATELY UNFENCED (no <c>&lt;&lt;&lt;…&gt;&gt;&gt;</c> data delimiter) on the
/// "owner-authored, reviewed-before-save" trust posture the Shows editor's own authored write already
/// earns — a posture an imported manifest's <see cref="ShowManifest.Name"/>/<see cref="ShowManifest.Flavor"/>
/// do NOT automatically inherit, since they arrive from a third-party catalog manifest with no operator
/// keystroke behind them. SPEC F118.2's ruled posture is the F90 FULL-CARD CONFIRM (an operator reviews
/// the whole card, flavor included, before adopting it — PLAN T255's modal) rather than a
/// <see cref="ContextFactSanitizer"/>-style content rewrite; that confirm is what makes the imported
/// text TRUSTED going forward, the identical bar owner-typed flavor already clears. But the confirm
/// alone does not neutralize a STRUCTURAL injection primitive: both fields reach
/// <c>LlmPromptBuilder.BuildShowFlavorPatterLine</c>'s AND <c>BuildShowLine</c>'s own unfenced
/// interpolation (<c>ShowFlavorLineGate.TryTakeDueShowLine</c> builds a <see cref="ShowFlavorFact"/>
/// straight off <c>Show.Name</c>/<c>Show.Flavor</c> — BOTH fields, not flavor alone), where a raw
/// newline could open a fresh, attacker-authored "prompt line" the operator never actually saw open
/// (the confirm shows the TEXT, not a simulation of where every control character lands once
/// interpolated), and a literal run of <c>&lt;&lt;&lt;</c>/<c>&gt;&gt;&gt;</c> could forge the fence
/// delimiter <c>BuildPatterFactLine</c>/<c>BuildContextFactsLine</c> use ELSEWHERE in the SAME prompt.
/// DECISION: run <see cref="ContextFactSanitizer.Sanitize"/> — the house's own established
/// "flatten control/whitespace, then collapse angle-bracket runs" neutralizer, ALREADY proven safe for
/// exactly this "make third-party text safe for an unfenced or fenced prompt position, without
/// rewriting its words" job — over <see cref="ShowManifest.Name"/> and <see cref="ShowManifest.Flavor"/>
/// ONLY, never <see cref="ShowManifest.Tagline"/> (spectator/admin-only text, confirmed to never reach
/// any LLM prompt position). This keeps the text VERBATIM at the word level (sanitizing never rewrites
/// or truncates a word — it only flattens control characters to spaces, collapses whitespace runs, and
/// collapses a run of 3+ identical angle brackets down to 1) while making it structurally impossible for
/// either field to open a new prompt line or reproduce a fence delimiter, WITHOUT wrapping either field
/// in a fence of its own (matching <c>BuildShowFlavorPatterLine</c>'s existing unfenced shape exactly —
/// this decision changes what gets STORED, never how the existing, unmodified T249 prompt code renders
/// it). Runs BEFORE the budget check below, so what gets measured against the 2× ceiling is what
/// actually gets persisted and later rendered, not the pre-sanitized raw length.
/// </para>
/// </summary>
internal static class ShowManifestParser
{
    /// <summary>The one schema major this parser currently accepts (mirrors
    /// <c>ThemeSchemaVersionGate.CurrentSchemaVersion</c> — see this type's own "SCHEMA-MAJOR" remarks
    /// for why Show keeps an independent copy rather than sharing that constant).</summary>
    public const int CurrentSchemaVersion = 1;

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Reads the optional top-level <c>schemaVersion</c> field off <paramref name="root"/> — the exact
    /// three-outcome contract <c>ThemeSchemaVersionGate.ExtractSchemaVersion</c> already establishes
    /// (ABSENT ⇒ <c>(null, false)</c>, treated as <see cref="CurrentSchemaVersion"/>; PRESENT and a
    /// readable <see cref="int"/> ⇒ <c>(version, false)</c>; PRESENT but unreadable — a string, a
    /// fraction, an overflow — ⇒ <c>(null, true)</c>, a refusal rather than a silent "treat as
    /// absent"), called by <see cref="Api.ShowsController.Import"/> BEFORE <see cref="Parse"/> ever
    /// runs. See this type's own "SCHEMA-MAJOR" remarks for why Show keeps its own copy of this
    /// extraction rather than sharing Theme's.
    /// </summary>
    public static (int? Version, bool Unreadable) ExtractSchemaVersion(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schemaVersion", out var property))
            return (null, false);

        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var version)
            ? (version, false)
            : (null, true);
    }

    public static ShowManifest Parse(ShowManifestSource source)
    {
        ShowManifestJson? document;
        try
        {
            document = JsonSerializer.Deserialize<ShowManifestJson>(source.Json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ShowManifestException($"show manifest '{source.Name}' is malformed JSON ({ex.Message})");
        }

        if (document is null)
            throw new ShowManifestException($"show manifest '{source.Name}' is empty");

        if (string.IsNullOrWhiteSpace(document.Name))
            throw new ShowManifestException($"show manifest '{source.Name}' is missing a name");

        // See this type's own "FLAVOR/NAME HYGIENE" remarks — control/whitespace flattening plus
        // angle-bracket-run collapsing, applied before the budget check below so what gets measured is
        // what actually gets persisted.
        var name = ContextFactSanitizer.Sanitize(document.Name);
        var tagline = document.Tagline ?? "";
        var flavor = ContextFactSanitizer.Sanitize(document.Flavor ?? "");

        // A name that was non-blank RAW can still sanitize down to nothing (e.g. a lone control
        // character, which IsNullOrWhiteSpace above does not itself catch — char.IsWhiteSpace and
        // char.IsControl are different predicates) — re-checked here rather than trusting the
        // pre-sanitize guard alone.
        if (string.IsNullOrWhiteSpace(name))
            throw new ShowManifestException($"show manifest '{source.Name}' is missing a name");

        ValidateImportBudget(source.Name, "name", name.Length, ShowBudgets.NameMaxChars);
        ValidateImportBudget(source.Name, "tagline", tagline.Length, ShowBudgets.TaglineMaxChars);
        ValidateImportBudget(source.Name, "flavor", flavor.Length, ShowBudgets.FlavorMaxChars);

        return new ShowManifest(name, tagline, flavor);
    }

    /// <summary>See this type's own "THE 2× HARD CAP" remarks: STRICTLY greater than 2× the authored
    /// 1× budget, never <c>&gt;=</c>.</summary>
    static void ValidateImportBudget(string sourceName, string field, int length, int budget)
    {
        var importCeiling = budget * 2;
        if (length > importCeiling)
            throw new ShowManifestException(
                $"show manifest '{sourceName}' {field} is {length} chars, over the {importCeiling}-char " +
                $"import ceiling (2x the {budget}-char authored budget)");
    }

    /// <summary>Ephemeral JSON projection of the untrusted show manifest document — mirrors
    /// <c>ThemeManifestParser</c>'s own <c>*Json</c> idiom: nothing here is trusted until checked field
    /// by field above, then discarded in favour of the immutable <see cref="ShowManifest"/>.</summary>
    sealed record ShowManifestJson
    {
        public string? Name { get; init; }
        public string? Tagline { get; init; }
        public string? Flavor { get; init; }
    }
}
