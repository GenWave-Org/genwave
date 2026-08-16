// STORY-337 — Icon packs swap the chrome (SPEC F130.1–.5 · PLAN T302 model + T303 endpoints)
//
// BDD specification — xUnit. Backend halves: definition validation (T302) and
// install/activation plumbing (T303). The renderer, per-name fallback, currentColor
// discipline, and the dangling-setting notice (AC2/AC3/AC4/AC6 UI halves) live in
// admin-ui jest (icon-pack-renderer.spec.tsx) + the T306 wire.

using System.Text;
using System.Text.RegularExpressions;
using GenWave.Host.Icons;

namespace GenWave.Host.Tests.Specs;

public static class FeatureIconPacksSwapTheChrome
{
    // ---------------------------------------------------------------------
    // Shared fixtures (T302) — a whitelist-valid pack body, one icon per
    // primitive tag, mirroring shapes admin-ui/app/(authed)/_components/icons.tsx
    // already draws (SPEC F130.1).
    // ---------------------------------------------------------------------

    const string ValidStyleJson = """"{ "strokeWidth": 1.5, "fill": "none" }"""";

    const string OnePrimitiveOfEachTagJson = """
        [
          { "tag": "rect", "x": 2, "y": 2, "width": 5, "height": 5, "rx": 1 },
          { "tag": "circle", "cx": 8, "cy": 8, "r": 1.3, "fill": "currentColor", "stroke": "none" },
          { "tag": "path", "d": "M5.5 5.5a4 4 0 0 0 0 5" },
          { "tag": "ellipse", "cx": 8, "cy": 8, "rx": 3, "ry": 2 },
          { "tag": "line", "x1": 2, "y1": 4, "x2": 14, "y2": 4 },
          { "tag": "polyline", "points": "2,4 6,10 10,4" },
          { "tag": "polygon", "points": "2,4 6,10 10,4" }
        ]
        """;

    static byte[] PackJsonBytes(string iconsBodyJson) =>
        Encoding.UTF8.GetBytes($$"""
            {
              "style": {{ValidStyleJson}},
              "icons": { {{iconsBodyJson}} }
            }
            """);

    // ---------------------------------------------------------------------
    // HAPPY PATH — the definition model (T302)
    // ---------------------------------------------------------------------

    public sealed class ScenarioAValidDefinitionPasses
    {
        [Fact]
        public void WhitelistPrimitivesWithNumericAttrsValidate()
        {
            // path/rect/circle/ellipse/line/polyline/polygon; d matches the grammar;
            // fills/strokes only none|currentColor; ≤256 KiB.
            var json = PackJsonBytes($$"""
                "dashboard": {{OnePrimitiveOfEachTagJson}}
                """);

            var result = IconPackDefinitionParser.Validate(json);

            var valid = Assert.IsType<IconPackValidationResult.Valid>(result);
            Assert.Empty(valid.IgnoredNames);
            Assert.Equal(1.5, valid.Definition.Style.StrokeWidth);
            Assert.Equal("none", valid.Definition.Style.Fill);

            var elements = Assert.Single(valid.Definition.Icons).Value;
            Assert.Equal(7, elements.Count);

            var rect = Assert.IsType<IconElement.Rect>(elements[0]);
            Assert.Equal(2, rect.X);
            Assert.Equal(1, rect.Rx);
            Assert.Null(rect.Ry);

            var circle = Assert.IsType<IconElement.Circle>(elements[1]);
            Assert.Equal("currentColor", circle.Fill);
            Assert.Equal("none", circle.Stroke);

            var path = Assert.IsType<IconElement.Path>(elements[2]);
            Assert.Equal("M5.5 5.5a4 4 0 0 0 0 5", path.D);

            Assert.IsType<IconElement.Ellipse>(elements[3]);
            Assert.IsType<IconElement.Line>(elements[4]);

            var polyline = Assert.IsType<IconElement.Polyline>(elements[5]);
            Assert.Equal("2,4 6,10 10,4", polyline.Points);

            Assert.IsType<IconElement.Polygon>(elements[6]);
        }

        [Fact]
        public void NamesOutsideTheContractAreIgnoredWithOneWarn()
        {
            Assert.DoesNotContain("not-a-real-icon-slot", IconNameContract.Names);

            var json = PackJsonBytes($$"""
                "dashboard": {{OnePrimitiveOfEachTagJson}},
                "not-a-real-icon-slot": [ { "tag": "circle", "cx": 8, "cy": 8, "r": 2 } ]
                """);

            var result = IconPackDefinitionParser.Validate(json);

            // The whole definition still validates — an out-of-contract name is whitelist-valid,
            // ordinary data, never a rejection reason (SPEC F130.2). The RESULT reports it so T303's
            // install route can log the one WARN; asserting the log line itself is T303's own fact.
            var valid = Assert.IsType<IconPackValidationResult.Valid>(result);
            var ignored = Assert.Single(valid.IgnoredNames);
            Assert.Equal("not-a-real-icon-slot", ignored);
            Assert.True(valid.Definition.Icons.ContainsKey("dashboard"));
            Assert.True(valid.Definition.Icons.ContainsKey("not-a-real-icon-slot"));
        }

        // Parity guard (PLAN T68's own golden-table idiom, applied to the icon-name contract):
        // string-parses admin-ui/app/(authed)/_components/icons.tsx directly for its `XxxIcon`
        // export set (no TS toolchain runs inside xUnit — the Story151/FeatureSettingsHelpKeysParity
        // repo-content-fact idiom) and asserts IconNameContract.Names against the derived set. Unlike
        // the Slugify parity guard, icons.tsx itself IS the one source SPEC F130.2 names — there is no
        // separately authored TS mirror to keep in step.
        static string RepoRoot =>
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

        static string IconsTsxPath =>
            Path.Combine(RepoRoot, "admin-ui", "app", "(authed)", "_components", "icons.tsx");

        static readonly Regex IconExportPattern = new(@"export function ([A-Za-z]+)Icon\(", RegexOptions.None);

        static string ToContractName(string exportStem) =>
            Regex.Replace(exportStem, "(?<!^)(?=[A-Z])", "-").ToLowerInvariant();

        static IReadOnlyList<string> ParseIconNamesFromTsx()
        {
            var text = File.ReadAllText(IconsTsxPath);
            var names = IconExportPattern.Matches(text)
                .Select(m => ToContractName(m.Groups[1].Value))
                .ToList();
            Assert.True(names.Count > 0, $"parsed zero icon exports out of {IconsTsxPath}");
            return names;
        }

        [Fact]
        public void TheIconNameContractMatchesTheHouseIconExports()
        {
            // The app constant and icons.tsx's export set cannot drift (parity pin,
            // the T68 golden-table idiom).
            var namesFromTsx = ParseIconNamesFromTsx().OrderBy(n => n, StringComparer.Ordinal).ToList();
            var contractNames = IconNameContract.Names.OrderBy(n => n, StringComparer.Ordinal).ToList();

            Assert.Equal(namesFromTsx, contractNames);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — install + activation (T303)
    // ---------------------------------------------------------------------

    public sealed class ScenarioInstallAndActivate
    {
        [Fact(Skip = "Pending T303 — see docs/PLAN.md")]
        public void InstallStoresTheDefinitionKeyedBySlug()
        {
            Assert.Fail("pending T303");
        }

        [Fact(Skip = "Pending T303 — see docs/PLAN.md")]
        public void StationIconPackIsAnAllowlistedLiveSetting()
        {
            // Default "" = house icons; dropdown control fed by installed packs.
            Assert.Fail("pending T303");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — hostile definitions and the fail-open uninstall
    // ---------------------------------------------------------------------

    public sealed class ScenarioAHostileDefinitionCannotLand
    {
        [Fact]
        public void AnUnknownTagRejectsNamingTheRule()
        {
            var json = PackJsonBytes("""
                "dashboard": [ { "tag": "script", "d": "alert(1)" } ]
                """);

            var result = IconPackDefinitionParser.Validate(json);

            var invalid = Assert.IsType<IconPackValidationResult.Invalid>(result);
            Assert.Contains("tag 'script'", invalid.Reason);
            Assert.Contains("whitelist", invalid.Reason);
        }

        [Fact]
        public void ANonNumericGeometryAttrRejects()
        {
            var json = PackJsonBytes("""
                "dashboard": [ { "tag": "rect", "x": "2", "y": 2, "width": 5, "height": 5 } ]
                """);

            var result = IconPackDefinitionParser.Validate(json);

            var invalid = Assert.IsType<IconPackValidationResult.Invalid>(result);
            Assert.Contains("attribute 'x'", invalid.Reason);
            Assert.Contains("numeric", invalid.Reason);
        }

        [Fact]
        public void ALiteralColorRejects()
        {
            // Only none|currentColor are expressible — hue stays token-bound.
            var json = PackJsonBytes("""
                "dashboard": [ { "tag": "circle", "cx": 8, "cy": 8, "r": 2, "fill": "#ff0000" } ]
                """);

            var result = IconPackDefinitionParser.Validate(json);

            var invalid = Assert.IsType<IconPackValidationResult.Invalid>(result);
            Assert.Contains("attribute 'fill'", invalid.Reason);
            Assert.Contains("none' or 'currentColor'", invalid.Reason);
        }

        [Fact]
        public void AnOversizeDefinitionRejects()
        {
            var padding = new string(' ', IconPackDefinitionParser.MaxDefinitionBytes);
            var json = PackJsonBytes($$"""
                "dashboard": {{OnePrimitiveOfEachTagJson}}
                """);
            var oversized = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(json) + padding);
            Assert.True(oversized.Length > IconPackDefinitionParser.MaxDefinitionBytes);

            var result = IconPackDefinitionParser.Validate(oversized);

            var invalid = Assert.IsType<IconPackValidationResult.Invalid>(result);
            Assert.Contains("KiB", invalid.Reason);
        }

        [Fact]
        public void AnIconNameOutsideTheSafeShapeRejectsNamingTheRule()
        {
            // A hostile map KEY (not a map value) — PLAN T302 review F1: icon names were
            // unconstrained free text until this gate, able to carry a script tag straight into
            // Definition.Icons/IgnoredNames.
            var json = PackJsonBytes($$"""
                "</svg><script>alert(1)</script>": {{OnePrimitiveOfEachTagJson}}
                """);

            var result = IconPackDefinitionParser.Validate(json);

            var invalid = Assert.IsType<IconPackValidationResult.Invalid>(result);
            Assert.Contains("icon name", invalid.Reason);
            Assert.Contains(IconPackDefinitionParser.IconNameText, invalid.Reason);
        }

        [Fact]
        public void AnOverlongIconNameRejectsPinningTheLengthCap()
        {
            // Also shape-hostile (trailing '<', outside IconNameText) — if the length-before-shape
            // ordering were ever swapped, this fixture would report the shape reason instead of the
            // length one and this assertion would catch it. A shape-valid-only fixture can't kill
            // that swap.
            var overlong = new string('a', IconPackDefinitionParser.MaxIconNameChars) + "<";
            var json = PackJsonBytes($$"""
                "{{overlong}}": {{OnePrimitiveOfEachTagJson}}
                """);

            var result = IconPackDefinitionParser.Validate(json);

            var invalid = Assert.IsType<IconPackValidationResult.Invalid>(result);
            Assert.Contains($"{IconPackDefinitionParser.MaxIconNameChars}-character cap", invalid.Reason);
        }
    }

    public sealed class ScenarioUninstallingTheActivePackFailsOpen
    {
        [Fact(Skip = "Pending T303 — see docs/PLAN.md")]
        public void TheActiveResolutionAnswersTheHouseSetAfterUninstall()
        {
            // No cross-store write from the DELETE; the resolver answers "house icons"
            // for a dangling Station:IconPack value.
            Assert.Fail("pending T303");
        }
    }
}
