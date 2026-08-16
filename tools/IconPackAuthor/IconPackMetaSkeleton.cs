using System.Text.Json;
using System.Text.Json.Nodes;

namespace GenWave.IconPackAuthor;

/// <summary>
/// Emits a draft <c>&lt;slug&gt;.meta.json</c> skeleton alongside the authored pack (T305 build note:
/// "the icon CATALOG entry's meta.json is authored separately at T312 — the script may emit a meta
/// skeleton too, cheap"). Mirrors the shelf-metadata idiom every other catalog kind's own meta.json
/// already carries (<c>author</c>/<c>description</c>/<c>added</c> — see e.g.
/// <c>entries/personas/*/*.meta.json</c>, <c>entries/fonts/*/*.meta.json</c>) plus the licence/
/// provenance fields <c>CatalogFontManifest</c> carries INSIDE its own <c>.font.json</c> (<c>license</c>/
/// <c>sourceUrl</c>/<c>version</c>) — SPEC F130.1's <c>gw-icon-pack</c> schema has no member of its own
/// for any of that (see <see cref="IconPackAuthoringOptions"/>'s own remarks), so it lands here
/// instead. T309 (not yet built) owns the icon kind's own authoritative catalog schema; this is a
/// curator's starting draft for T312 to hand-finish, not a validated artifact — the companion
/// <c>&lt;slug&gt;.icon.json</c> is the only file this script's own real-parser proof covers.
/// </summary>
public static class IconPackMetaSkeleton
{
    public static string Build(string author, string description, string license, string sourceUrl, string? version, DateOnly added)
    {
        var document = new JsonObject
        {
            ["author"] = author,
            ["description"] = description,
            ["license"] = license,
            ["sourceUrl"] = sourceUrl,
            ["version"] = version,
            ["added"] = added.ToString("yyyy-MM-dd"),
        };

        return document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
