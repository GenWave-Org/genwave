namespace GenWave.Host.Icons;

using System.Text.Json.Nodes;

/// <summary>
/// Re-serializes an already-validated <see cref="IconPackDefinition"/> back into the canonical
/// <c>gw-icon-pack</c> document (SPEC F130.1, STORY-337, PLAN T303) — the ONLY form
/// <c>IconPackController.Install</c> ever persists via <c>IIconPackStore.UpsertAsync</c>.
///
/// <para>
/// <b>WHY A SERIALIZER EXISTS AT ALL (unlike <c>Catalog.CatalogAvatarPackManifestSerializer</c>'s own
/// "no Serialize, this app never writes one" posture).</b> Storing the raw fetched/request bytes
/// verbatim — the way a font pack's own face bytes are stored, see
/// <see cref="Api.AvatarPackController"/>'s remarks for why an avatar item differs too — would open a
/// validator/renderer parser differential here specifically: <see cref="IconPackDefinitionParser.Validate"/>
/// accepts a document carrying unknown top-level members or a JSON object with a DUPLICATE key inside
/// <c>icons</c> (<c>System.Text.Json</c>'s own <c>TryGetProperty</c>/last-object-wins deserialize
/// semantics silently keep only the LAST occurrence — see <see cref="IconPackDefinitionParser"/>'s own
/// remarks) — both accepted-and-dropped at validation time. A byte-for-byte copy of the ORIGINAL bytes
/// would still carry that dropped noise; a future renderer parsing the SAME bytes with a differently-
/// ordered or differently-strict parser could disagree with what this app actually validated and
/// intended to store. Re-serializing the VALIDATED MODEL closes that gap structurally: the stored jsonb
/// can only ever express what <see cref="IconPackDefinition"/> itself can hold — one entry per icon
/// name (a C# <see cref="IReadOnlyDictionary{TKey,TValue}"/> cannot carry a duplicate key at all), no
/// member this schema does not define — making this class's own output the canonical, and only,
/// persistence form.
/// </para>
///
/// <para>
/// <b>DUPLICATE ICON NAMES ARE LAST-WINS AT PARSE, THEN CEASE TO EXIST (PLAN T303 review rider).</b> A
/// remote document naming the same <c>icons</c> key twice (e.g. <c>{"play": [...], "play": [...]}</c>)
/// is not rejected by <see cref="IconPackDefinitionParser"/> — <c>System.Text.Json</c>'s own
/// <see cref="System.Text.Json.JsonSerializer.Deserialize{TValue}(string, System.Text.Json.JsonSerializerOptions?)"/>
/// against a <see cref="System.Collections.Generic.Dictionary{TKey,TValue}"/>-shaped target keeps only
/// the LAST occurrence of a repeated key (the same last-object-wins behaviour documented on
/// <see cref="System.Text.Json.JsonDocument.RootElement"/>'s own <c>TryGetProperty</c>), so
/// <see cref="IconPackDefinition.Icons"/> can structurally never carry two entries for one name by the
/// time <see cref="Validate"/> hands it back. Rejecting the duplicate outright at parse time was
/// considered and deliberately NOT done: SPEC F130.5 pins whole-document reject to a whitelist/grammar
/// violation, not a shape a real JSON parser already resolves unambiguously and predictably — and this
/// class's own re-serialization is what makes that resolution SAFE to store: the canonical form this
/// route persists carries the resolved (last-wins) icon exactly once, so no differential between "what
/// was validated" and "what a later reader parses" can ever reappear downstream.
/// </para>
/// </summary>
internal static class IconPackDefinitionSerializer
{
    /// <summary>
    /// Builds the canonical JSON document for <paramref name="definition"/> — <c>schemaVersion</c>
    /// always written explicitly (never omitted the way an install REQUEST may legally leave it
    /// absent, SPEC F130.1's own "absent ⇒ current" rule) so the stored form is self-describing on its
    /// own, and <c>icons</c> keys are written in ordinal-sorted order so two definitions holding the
    /// identical icon set always serialize byte-identically regardless of the source
    /// <see cref="System.Collections.Generic.Dictionary{TKey,TValue}"/>'s own enumeration order (SPEC
    /// F130.1's own document has no ordering semantics of its own — a stable output is purely this
    /// class's own choice, for a predictable stored value and an easy round-trip fact).
    /// </summary>
    public static string Serialize(IconPackDefinition definition)
    {
        var icons = new JsonObject();
        foreach (var (name, elements) in definition.Icons.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var array = new JsonArray();
            foreach (var element in elements)
                array.Add(SerializeElement(element));

            icons[name] = array;
        }

        var document = new JsonObject
        {
            ["schemaVersion"] = IconPackDefinitionParser.CurrentSchemaVersion,
            ["style"] = new JsonObject
            {
                ["strokeWidth"] = definition.Style.StrokeWidth,
                ["fill"] = definition.Style.Fill,
            },
            ["icons"] = icons,
        };

        return document.ToJsonString();
    }

    static JsonObject SerializeElement(IconElement element)
    {
        var (obj, fill, stroke) = element switch
        {
            IconElement.Path e => (new JsonObject { ["tag"] = "path", ["d"] = e.D }, e.Fill, e.Stroke),
            IconElement.Rect e => (WithRadii(new JsonObject
            {
                ["tag"] = "rect", ["x"] = e.X, ["y"] = e.Y, ["width"] = e.Width, ["height"] = e.Height,
            }, e.Rx, e.Ry), e.Fill, e.Stroke),
            IconElement.Circle e => (new JsonObject { ["tag"] = "circle", ["cx"] = e.Cx, ["cy"] = e.Cy, ["r"] = e.R }, e.Fill, e.Stroke),
            IconElement.Ellipse e => (new JsonObject
            {
                ["tag"] = "ellipse", ["cx"] = e.Cx, ["cy"] = e.Cy, ["rx"] = e.Rx, ["ry"] = e.Ry,
            }, e.Fill, e.Stroke),
            IconElement.Line e => (new JsonObject
            {
                ["tag"] = "line", ["x1"] = e.X1, ["y1"] = e.Y1, ["x2"] = e.X2, ["y2"] = e.Y2,
            }, e.Fill, e.Stroke),
            IconElement.Polyline e => (new JsonObject { ["tag"] = "polyline", ["points"] = e.Points }, e.Fill, e.Stroke),
            IconElement.Polygon e => (new JsonObject { ["tag"] = "polygon", ["points"] = e.Points }, e.Fill, e.Stroke),
            _ => throw new System.Diagnostics.UnreachableException($"Unhandled {nameof(IconElement)} case."),
        };

        if (fill is not null) obj["fill"] = fill;
        if (stroke is not null) obj["stroke"] = stroke;
        return obj;
    }

    static JsonObject WithRadii(JsonObject obj, double? rx, double? ry)
    {
        if (rx is { } rxValue) obj["rx"] = rxValue;
        if (ry is { } ryValue) obj["ry"] = ryValue;
        return obj;
    }
}
