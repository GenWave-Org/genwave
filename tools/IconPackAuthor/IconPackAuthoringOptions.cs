using System.Globalization;
using System.Text.RegularExpressions;

namespace GenWave.IconPackAuthor;

/// <summary>
/// Parsed <c>author</c> subcommand argv (PLAN T305 — house "argv-only" rule: no interactive prompts,
/// no config file beyond the explicit <see cref="MappingPath"/>). <see cref="License"/>/
/// <see cref="SourceUrl"/>/<see cref="Version"/> mirror <c>CatalogFontManifest</c>'s own provenance
/// field idiom (T305 build note: "check what the catalog font manifests carry") — they land in the
/// companion <c>&lt;slug&gt;.meta.json</c> skeleton (<see cref="IconPackMetaSkeleton"/>), never inside
/// the emitted <c>&lt;slug&gt;.icon.json</c> itself: SPEC F130.1's <c>gw-icon-pack</c> schema has no
/// licence/provenance member of its own (style + icons, nothing else — see
/// <c>IconPackDefinitionSerializer</c>'s own remarks on why the canonical form can only ever express
/// what the schema defines), so smuggling licence text into that document would be silently dropped
/// the instant it round-trips through the real serializer.
/// </summary>
public sealed partial record IconPackAuthoringOptions(
    string SourceDir,
    string MappingPath,
    string OutputDir,
    string Slug,
    string License,
    string SourceUrl,
    string? Version,
    string? FillOverride,
    double? StrokeWidthOverride,
    string MetaAuthor,
    string MetaDescription)
{
    public const string UsageText =
        "author --source <dir> --mapping <file> --out <dir> --slug <slug> --license <text> --source-url <url> " +
        "[--version <text>] [--fill none|currentColor] [--stroke-width <n>] [--author <text>] [--description <text>]";

    // --slug becomes a filesystem path segment (Program.cs: "{Slug}.icon.json"/"{Slug}.meta.json"
    // under --out) — gated to the house slug shape at parse time, not left to whatever the OS
    // filesystem happens to tolerate, the same "reject at the door" posture --stroke-width gets
    // below. Mirrors GenWave.Host.Api.PersonaController.SlugFormat / Configuration.SettingValidator's
    // own \A[a-z0-9]+(-[a-z0-9]+)*\z convention and BoundedImportBodyReader.MaxCatalogSlugLength (64)
    // — duplicated here as literals, not referenced, because all three are `private`/`internal` to
    // GenWave.Host and unreachable from this project (same reasoning IconPackAuthoringGateway's own
    // remarks give for MinStrokeWidth/MaxStrokeWidth below). \A/\z, not ^/$: .NET's `$` matches
    // immediately before a trailing '\n', which ^/$ would let slip through as e.g. "ok\n".
    const int MaxSlugChars = 64;

    [GeneratedRegex("\\A[a-z0-9]+(-[a-z0-9]+)*\\z")]
    private static partial Regex SlugShape();

    public static IconPackAuthoringOptions Parse(IReadOnlyList<string> args)
    {
        string? source = null, mapping = null, outputDir = null, slug = null, license = null, sourceUrl = null;
        string? version = null, fill = null, author = null, description = null;
        double? strokeWidth = null;

        for (var i = 0; i < args.Count; i++)
        {
            var flag = args[i];

            string NextValue()
            {
                if (i + 1 >= args.Count)
                    throw new IconPackAuthoringUsageException($"'{flag}' expects a value");
                return args[++i];
            }

            switch (flag)
            {
                case "--source": source = NextValue(); break;
                case "--mapping": mapping = NextValue(); break;
                case "--out": outputDir = NextValue(); break;
                case "--slug": slug = NextValue(); break;
                case "--license": license = NextValue(); break;
                case "--source-url": sourceUrl = NextValue(); break;
                case "--version": version = NextValue(); break;
                case "--fill": fill = NextValue(); break;
                case "--author": author = NextValue(); break;
                case "--description": description = NextValue(); break;
                case "--stroke-width":
                    var raw = NextValue();
                    if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                        throw new IconPackAuthoringUsageException($"--stroke-width '{raw}' is not a number");

                    // SPEC F130.1's strokeWidth bound ([0.5, 3]) — the real parser's own copy
                    // (IconPackDefinitionParser.MinStrokeWidth/MaxStrokeWidth) is `private`, unreachable
                    // even through this project's one IVT-backed doorway (IconPackAuthoringGateway), so
                    // this is a deliberate, narrow duplication of two literals rather than a full second
                    // copy of the grammar. Checked here, at argv parse time, so a bad --stroke-width
                    // fails immediately with the flag named, instead of surfacing after every glyph in
                    // the run has already converted, as one opaque "emitted pack failed the real
                    // IconPackDefinitionParser" message.
                    if (parsed is < 0.5 or > 3.0)
                        throw new IconPackAuthoringUsageException($"--stroke-width {parsed} is outside the [0.5, 3] range");

                    strokeWidth = parsed;
                    break;
                default:
                    throw new IconPackAuthoringUsageException($"unrecognized argument '{flag}'");
            }
        }

        if (fill is not (null or "none" or "currentColor"))
            throw new IconPackAuthoringUsageException($"--fill must be 'none' or 'currentColor', not '{fill}'");

        var slugValue = Require(slug, "--slug");
        // Length BEFORE shape (cheap reject, keeps a pathological input away from the regex engine at
        // all — mirrors PersonaController.Import's own ordering for the same reason).
        if (slugValue.Length > MaxSlugChars)
            throw new IconPackAuthoringUsageException($"--slug is {slugValue.Length} characters, over the {MaxSlugChars}-character cap");
        if (!SlugShape().IsMatch(slugValue))
        {
            throw new IconPackAuthoringUsageException(
                $"--slug '{slugValue}' must be lowercase-kebab (letters, digits, single hyphens) — it becomes a filesystem path segment under --out");
        }

        return new IconPackAuthoringOptions(
            Require(source, "--source"),
            Require(mapping, "--mapping"),
            Require(outputDir, "--out"),
            slugValue,
            Require(license, "--license"),
            Require(sourceUrl, "--source-url"),
            version,
            fill,
            strokeWidth,
            author ?? "GenWave",
            description ?? "");
    }

    static string Require(string? value, string flag) =>
        value ?? throw new IconPackAuthoringUsageException($"missing required argument '{flag}'");
}
