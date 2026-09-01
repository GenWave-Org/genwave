namespace GenWave.Plugins;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

/// <summary>
/// Parses and validates ONE <c>plugin.json</c> manifest document (SPEC F156.2, STORY-385 AC2/AC5).
/// Pure: no filesystem I/O, no logging. <see cref="PluginManifestDiscovery"/> is what finds a
/// candidate's file on disk and reads its text; the loader (<c>GenWave.Plugins</c>, PLAN T392) is the
/// only caller that turns a <see cref="PluginManifestParseResult.Field"/> into an actual WARN log line
/// — this type only ever returns a STRUCTURED reason, never a message meant to be logged verbatim (the
/// <c>GenWave.Tts.SafeSegmentAuthorResult</c>/<c>GenWave.Tts.CrosstalkScriptParser</c> typed-failure
/// idiom).
///
/// <para>
/// <b>First-rule-wins, whole-manifest reject</b> (the <c>CrosstalkScriptParser</c> shape): the first
/// field that fails its own rule aborts the whole parse — no partial manifest, no salvage. Checked in
/// the field order SPEC F156.2 itself states: <c>name</c>, <c>version</c>, <c>assembly</c>,
/// <c>entryType</c>, <c>abstractions</c>.
/// </para>
///
/// <para>
/// <b>Unknown fields are accepted, deliberately</b> — the same forward-compat lean
/// <c>GenWave.MediaLibrary</c>'s catalog kind-drop takes: a manifest written for a newer host version
/// that has grown a sixth field must still parse on an older host, not hard-fail on it. Duplicate keys
/// resolve last-wins, System.Text.Json's own default. The file-read size bound (a hostile or corrupt
/// <c>plugin.json</c> of unbounded length) belongs to the loader (PLAN T392) — this type never opens a
/// file, so it has no size to bound.
/// </para>
/// </summary>
public static class PluginManifestParser
{
    // Exact-case field matching (no PropertyNameCaseInsensitive): SPEC F156.2 names the five fields
    // in lowercase (name/version/assembly/entryType/abstractions) — camelCase is the ONE case-binding
    // mechanism this parser needs, so an upper- or mixed-case key (e.g. "NAME") is simply an unknown
    // field (see the class remarks above) and the manifest still rejects on the missing lowercase
    // 'name', exactly as if the field had been omitted outright.
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

    /// <summary>
    /// Parses and fully validates <paramref name="manifestJson"/> for the plugin directory named
    /// <paramref name="slug"/> — the manifest's own directory name, supplied by the caller (never read
    /// from the JSON body itself: SPEC F156.2 names no <c>slug</c>/<c>id</c> field, and a plugin must
    /// never be able to claim an identity other than the folder it was mounted under).
    /// </summary>
    public static PluginManifestParseResult Parse(string slug, string manifestJson)
    {
        PluginManifestJson? document;
        try
        {
            document = JsonSerializer.Deserialize<PluginManifestJson>(manifestJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            // ex.Message is NOT trustworthy log input: System.Text.Json echoes an attacker-controlled
            // JSON property name straight back into its own exception text uncensored (a manifest
            // whose skipped/unrecognized property name embeds a JSON-escaped CR/LF, positioned next to
            // a syntax error, proven at T391 review to survive verbatim into JsonException.Message) —
            // Reject below is what neutralizes it before it reaches Detail.
            return Reject(PluginManifestField.Document, $"manifest is malformed JSON ({ex.Message})");
        }

        if (document is null)
            return Reject(PluginManifestField.Document, "manifest is empty");

        // Read every field into a local up front: IsBlank's [NotNullWhen(false)] then tracks each
        // local's non-null state precisely through its own guard below, the same way it would for a
        // bare variable anywhere else in the codebase — no null-forgiving `!` needed on the
        // PluginManifest construction at the bottom.
        var name = document.Name;
        var version = document.Version;
        var assembly = document.Assembly;
        var entryType = document.EntryType;
        var abstractions = document.Abstractions;

        if (IsBlank(name))
            return Reject(PluginManifestField.Name, "manifest is missing required field 'name'");

        if (IsBlank(version))
            return Reject(PluginManifestField.Version, "manifest is missing required field 'version'");

        if (IsBlank(assembly))
            return Reject(PluginManifestField.Assembly, "manifest is missing required field 'assembly'");

        // SPEC F156.2: "assembly (a file name, no path separators — reject otherwise)". A bare
        // structural rule, not a hand-curated denylist: the value must round-trip unchanged through
        // Path.GetFileName (kills any embedded separator this platform's own Path type recognizes),
        // must not be "." or ".." (meaningless as a bare file name), must carry no leading/trailing OR
        // embedded whitespace, must not contain ':' (the Windows drive/NTFS-stream separator shape —
        // "C:x.dll" — a bare Linux filename character, but never a legitimate assembly file name), and
        // must not contain any character Path.GetInvalidFileNameChars() itself names. That last check
        // is deliberately widened rather than relied on alone: on this codebase's Linux deploy target
        // (CLAUDE.md) it names only NUL and '/', so the whitespace/':' checks above are what actually
        // carry the weight for those two shapes — Path.GetInvalidFileNameChars() is kept anyway for the
        // NUL case and for portability if this ever runs somewhere its denylist is richer. The '/' /
        // '\\' / ".." SUBSTRING check (ContainsPathSeparatorOrTraversal, kept rather than folded) is
        // NOT fully redundant with the rule above even so: '\\' is a plain, valid filename character on
        // Linux (Path.GetFileName leaves it untouched), and ".." as a SUBSTRING (e.g. "..dll") isn't
        // caught by the exact "." / ".." equality check — both shapes still need it. No additional
        // extension check (e.g. requiring ".dll"): SPEC F156.2 does not rule one, and the loader's own
        // WARN+skip posture (F156.4, PLAN T392) already covers a wrong-extension or non-assembly file
        // at load time — this parser only enforces what SPEC actually states.
        if (IsInvalidAssemblyFileName(assembly))
        {
            return Reject(
                PluginManifestField.Assembly,
                $"manifest field 'assembly' (\"{assembly}\") must be a bare, on-disk-safe file name — " +
                "no path separators, traversal, surrounding/embedded whitespace, or reserved characters");
        }

        if (IsBlank(entryType))
            return Reject(PluginManifestField.EntryType, "manifest is missing required field 'entryType'");

        if (IsBlank(abstractions))
            return Reject(PluginManifestField.Abstractions, "manifest is missing required field 'abstractions'");

        return PluginManifestParseResult.Success(new PluginManifest(slug, name, version, assembly, entryType, abstractions));
    }

    // [NotNullWhen(false)] so the compiler tracks "not blank ⇒ not null" through every
    // `if (IsBlank(x)) return ...;` guard above — the same annotation string.IsNullOrWhiteSpace
    // itself carries — otherwise every fallthrough read below would need a null-forgiving `!`.
    static bool IsBlank([NotNullWhen(false)] string? value) => string.IsNullOrWhiteSpace(value);

    static bool IsInvalidAssemblyFileName(string assemblyFileName) =>
        ContainsPathSeparatorOrTraversal(assemblyFileName)
        || Path.GetFileName(assemblyFileName) != assemblyFileName
        || assemblyFileName is "." or ".."
        || assemblyFileName.Any(char.IsWhiteSpace)
        || assemblyFileName.Contains(':')
        || assemblyFileName.IndexOfAny(InvalidFileNameChars) >= 0;

    static bool ContainsPathSeparatorOrTraversal(string assemblyFileName) =>
        assemblyFileName.Contains('/') || assemblyFileName.Contains('\\') || assemblyFileName.Contains("..");

    // The one choke point every Failure passes through: every Detail this parser ever produces is
    // built from either a static, ASCII-only message or an interpolated THIRD-PARTY value (a raw
    // manifest field, or a raw JsonException.Message once System.Text.Json has had its say) — routing
    // ALL of them through here, rather than sanitizing individual call sites, means a future reject
    // reason can never forget the neutralization step. Strips, never rejects on, a control character:
    // a reject reason must still name the field (SPEC F156.2's "one WARN names the field"), so a
    // crafted value earns a single-line Detail, not a swapped-out generic one.
    static PluginManifestParseResult Reject(PluginManifestField field, string detail) =>
        PluginManifestParseResult.Failure(field, NeutralizeControlCharacters(detail));

    // Widened past CR/LF alone (GenWave.Core.Logging.LogSanitize's own choice) to every control
    // character: a raw manifest value or JsonException.Message could carry any of them, and "strip,
    // don't replace" is that same house idiom's own choice, applied here rather than a reference to it
    // — GenWave.Plugins deliberately stays off GenWave.Core (see the csproj's own reference-rationale
    // comment), so this is a small, self-contained floor, not a substitute for the caller's own
    // LogSanitize.Strip pass before an actual log line (PluginManifestParseResult.Detail's own remarks).
    static string NeutralizeControlCharacters(string value) =>
        value.Any(char.IsControl) ? new string(value.Where(c => !char.IsControl(c)).ToArray()) : value;
}
