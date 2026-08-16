// STORY-337 — Icon packs swap the chrome (SPEC F130.1–.5 · PLAN T302 model + T303 endpoints)
//
// BDD specification — xUnit. Backend halves: definition validation (T302) and
// install/activation plumbing (T303). The renderer, per-name fallback, currentColor
// discipline, and the dangling-setting notice (AC2/AC3/AC4/AC6 UI halves) live in
// admin-ui jest (icon-pack-renderer.spec.tsx) + the T306 wire.

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;
using GenWave.Host.Catalog;
using GenWave.Host.Configuration;
using GenWave.Host.Icons;
using GenWave.Host.Tests.Fakes;

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
        [Fact]
        public async Task InstallStoresTheDefinitionKeyedBySlug()
        {
            // WIRED T303 — the real production route through WebApplicationFactory<Program> against
            // a fake catalog origin (mirrors Story332_AvatarPacksIntoTheLibrary.cs's own harness idiom)
            // and a FakeIconPackStore (this project has no Postgres fixture; the REAL station.icon_pack
            // SQL is Story333_VisualLayerStores.cs's own coverage against real Postgres, PLAN T303
            // review rider 1).
            var store = new FakeIconPackStore();
            await using var factory = new IconPackInstallWebFactory(store);
            var client = await IconPackInstallWebFactory.LoggedInClientAsync(factory);

            var response = await client.PostAsync($"/api/icon-packs/{IconPackInstallFixtures.PackSlug}/install", null);

            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var pack = await store.GetBySlugAsync(IconPackInstallFixtures.PackSlug, CancellationToken.None);
            Assert.NotNull(pack);
            Assert.Equal(IconPackInstallFixtures.PackSlug, pack.ImportedFrom);
            var valid = Assert.IsType<IconPackValidationResult.Valid>(
                IconPackDefinitionParser.Validate(Encoding.UTF8.GetBytes(pack.Definition)));
            Assert.True(valid.Definition.Icons.ContainsKey("dashboard"));
        }

        [Fact]
        public async Task InstallStoresTheReSerializedCanonicalModelNeverTheRawFetchedBytes()
        {
            // PLAN T303 review riders 2 + 5: an unknown top-level member and a DUPLICATE "dashboard"
            // key inside icons — both accepted-and-dropped at validation (System.Text.Json's own
            // Dictionary-target deserialize keeps only the LAST occurrence of a repeated key). The
            // store write must serialize the VALIDATED MODEL, never the raw fetched bytes, or both of
            // these would still be sitting in the stored jsonb.
            var store = new FakeIconPackStore();
            await using var factory = new IconPackInstallWebFactory(
                store, handler: IconPackInstallFixtures.BuildRoutedHandler(IconPackInstallFixtures.DefinitionWithNoiseJson));
            var client = await IconPackInstallWebFactory.LoggedInClientAsync(factory);

            var response = await client.PostAsync($"/api/icon-packs/{IconPackInstallFixtures.PackSlug}/install", null);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

            var pack = await store.GetBySlugAsync(IconPackInstallFixtures.PackSlug, CancellationToken.None);
            Assert.NotNull(pack);

            // The unknown top-level member never reaches storage.
            Assert.DoesNotContain("unknownTopLevelMember", pack.Definition, StringComparison.Ordinal);

            // The duplicate "dashboard" key resolved to exactly ONE entry — the LAST declared circle
            // (r=2), never both, and never two separate map entries (structurally impossible on a
            // C# IReadOnlyDictionary in the first place).
            var valid = Assert.IsType<IconPackValidationResult.Valid>(
                IconPackDefinitionParser.Validate(Encoding.UTF8.GetBytes(pack.Definition)));
            var elements = Assert.Single(valid.Definition.Icons).Value;
            var circle = Assert.IsType<IconElement.Circle>(Assert.Single(elements));
            Assert.Equal(2, circle.R);
        }

        [Fact]
        public void InstallCapEqualsTheDefinitionParsersOwnCap()
        {
            // PLAN T303 review rider 3: an icon entry carries no assets[] — the definition IS the
            // manifest file, fetched and size-capped DURING the streamed read by
            // CatalogProxyService.MaxCardBytes. Pinning it equal to IconPackDefinitionParser's own
            // MaxDefinitionBytes is what proves the two 256 KiB ceilings are the SAME cap, not a
            // coincidence two independently-chosen numbers happen to share today.
            Assert.Equal(CatalogProxyService.MaxCardBytes, IconPackDefinitionParser.MaxDefinitionBytes);
        }

        [Fact]
        public async Task OutOfContractNamesAreLoggedInOneSanitizedWarn()
        {
            var capturingLogger = new CapturingLogger<IconPackController>();
            await using var factory = new IconPackInstallWebFactory(
                handler: IconPackInstallFixtures.BuildRoutedHandler(IconPackInstallFixtures.DefinitionWithIgnoredNamesJson),
                capturingLogger: capturingLogger);
            var client = await IconPackInstallWebFactory.LoggedInClientAsync(factory);

            var response = await client.PostAsync($"/api/icon-packs/{IconPackInstallFixtures.PackSlug}/install", null);

            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            // SPEC F130.2's own "ignored with ONE install-time WARN" — a single log line naming both
            // out-of-contract names, sanitized (PLAN T303 review rider 4).
            var warning = Assert.Single(capturingLogger.Warnings, w => w.Contains("ignored", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("not-a-real-icon-slot", warning, StringComparison.Ordinal);
            Assert.Contains("another-unknown-slot", warning, StringComparison.Ordinal);
        }

        [Fact]
        public async Task AHostileDefinitionFailsQuietlyWithTheReasonWarnLoggedNeverEchoed()
        {
            var capturingLogger = new CapturingLogger<IconPackController>();
            await using var factory = new IconPackInstallWebFactory(
                handler: IconPackInstallFixtures.BuildRoutedHandler(IconPackInstallFixtures.HostileScriptTagJson),
                capturingLogger: capturingLogger);
            var client = await IconPackInstallWebFactory.LoggedInClientAsync(factory);

            var response = await client.PostAsync($"/api/icon-packs/{IconPackInstallFixtures.PackSlug}/install", null);

            // PLAN T303 review rider 4: the real reason is WARN-logged (sanitized), but the response
            // BODY is a quiet, generic 400 — F15.7's own "no internal detail in a body" posture.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("script", body, StringComparison.Ordinal);
            Assert.DoesNotContain("whitelist", body, StringComparison.Ordinal);
            Assert.Contains(capturingLogger.Warnings, w => w.Contains("tag 'script'", StringComparison.Ordinal));
        }

        [Fact]
        public async Task AnUnboundedReasonSubstringIsClampedInTheLog()
        {
            // A hostile TAG value (unbounded remote text — never bounded by any prior gate, unlike an
            // icon NAME) that Validate's own rejection reason embeds verbatim (PLAN T303 review rider
            // 4's own "a 250 KiB tag yields a 250 KiB Reason" scenario, scaled down for a fast fact).
            var capturingLogger = new CapturingLogger<IconPackController>();
            await using var factory = new IconPackInstallWebFactory(
                handler: IconPackInstallFixtures.BuildRoutedHandler(IconPackInstallFixtures.HostileOverlongTagJson),
                capturingLogger: capturingLogger);
            var client = await IconPackInstallWebFactory.LoggedInClientAsync(factory);

            var response = await client.PostAsync($"/api/icon-packs/{IconPackInstallFixtures.PackSlug}/install", null);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var warning = Assert.Single(capturingLogger.Warnings, w => w.Contains("reason=", StringComparison.Ordinal));
            // LogSafeText.Sanitize's own 200-character cap — the 5,000-character hostile tag can never
            // reach the log line anywhere near its own full length.
            Assert.True(warning.Length < 1000, $"expected a length-clamped log line, got {warning.Length} characters: {warning}");
        }

        [Fact]
        public async Task ListReturnsEveryInstalledPack()
        {
            var store = new FakeIconPackStore();
            await using var factory = new IconPackInstallWebFactory(store);
            var client = await IconPackInstallWebFactory.LoggedInClientAsync(factory);
            var install = await client.PostAsync($"/api/icon-packs/{IconPackInstallFixtures.PackSlug}/install", null);
            Assert.True(install.IsSuccessStatusCode, await install.Content.ReadAsStringAsync());

            var response = await client.GetAsync("/api/icon-packs");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var packs = await response.Content.ReadFromJsonAsync<IconPackSummaryDto[]>();
            var pack = Assert.Single(packs!);
            Assert.Equal((IconPackInstallFixtures.PackSlug, 1, IconPackInstallFixtures.PackSlug), (pack.Slug, pack.IconCount, pack.ImportedFrom));
        }

        [Fact]
        public async Task ActiveReturnsTheDefinitionForTheCurrentlyActivePack()
        {
            await using var factory = new IconPackInstallWebFactory(activeIconPack: IconPackInstallFixtures.PackSlug);
            var client = await IconPackInstallWebFactory.LoggedInClientAsync(factory);
            var install = await client.PostAsync($"/api/icon-packs/{IconPackInstallFixtures.PackSlug}/install", null);
            Assert.True(install.IsSuccessStatusCode, await install.Content.ReadAsStringAsync());

            var response = await client.GetAsync("/api/icon-packs/active");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var json = await response.Content.ReadAsStringAsync();
            var valid = Assert.IsType<IconPackValidationResult.Valid>(IconPackDefinitionParser.Validate(Encoding.UTF8.GetBytes(json)));
            Assert.True(valid.Definition.Icons.ContainsKey("dashboard"));
        }

        [Fact]
        public async Task ActiveReturnsNoContentWhenNoPackIsActivated()
        {
            // Station:IconPack unset — the F130.4 default ("" = house icons).
            await using var factory = new IconPackInstallWebFactory();
            var client = await IconPackInstallWebFactory.LoggedInClientAsync(factory);

            var response = await client.GetAsync("/api/icon-packs/active");

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        [Fact]
        public async Task ListDegradesIconCountToZeroWhenTheStoredDefinitionFailsReValidation()
        {
            // Review finding F4(b) — ToSummaryDto's own degrade arm: only this controller's Install
            // ever writes station.icon_pack.definition, always already-validated, so a stored row that
            // fails re-validation is a should-never-happen anomaly — the listing degrades IconCount to
            // 0 and still 200s, rather than 500ing the whole page over one bad row.
            var store = new FakeIconPackStore();
            await store.UpsertAsync(
                IconPackInstallFixtures.PackSlug, "not a valid icon pack definition",
                IconPackInstallFixtures.PackSlug, CancellationToken.None);
            await using var factory = new IconPackInstallWebFactory(store);
            var client = await IconPackInstallWebFactory.LoggedInClientAsync(factory);

            var response = await client.GetAsync("/api/icon-packs");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var packs = await response.Content.ReadFromJsonAsync<IconPackSummaryDto[]>();
            var pack = Assert.Single(packs!);
            Assert.Equal(0, pack.IconCount);
        }

        [Fact]
        public async Task ActiveAnswers204NeverServingAnInvalidStoredDefinition()
        {
            // Review finding F4(a) — Active's own re-validation branch: a should-never-happen invalid
            // stored definition must never ride the wire unvalidated — 204 (the renderer falls back to
            // house icons) is the honest response, and the anomaly is WARN-logged server-side rather
            // than silently served as if it were safe.
            var store = new FakeIconPackStore();
            await store.UpsertAsync(
                IconPackInstallFixtures.PackSlug, "not a valid icon pack definition",
                IconPackInstallFixtures.PackSlug, CancellationToken.None);
            var capturingLogger = new CapturingLogger<IconPackController>();
            await using var factory = new IconPackInstallWebFactory(
                store, activeIconPack: IconPackInstallFixtures.PackSlug, capturingLogger: capturingLogger);
            var client = await IconPackInstallWebFactory.LoggedInClientAsync(factory);

            var response = await client.GetAsync("/api/icon-packs/active");

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Contains(capturingLogger.Warnings, w => w.Contains("re-validation", StringComparison.OrdinalIgnoreCase));
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the settings dropdown (T303)
    // ---------------------------------------------------------------------

    public sealed class ScenarioStationIconPackIsAnAllowlistedLiveSettingFedByInstalledPacks
    {
        // Given the settings surface describing Station:IconPack — a real SettingsController, no live
        // stack or DB required (same in-process pattern Story265_ThemeSelectionAndPersistence.cs's own
        // ScenarioTheSettingPresentsAsAClosedChoice already established for Station:Theme).

        sealed class FakeSettingsStore : IStationSettingsStore
        {
            public Task WriteAsync(string key, object value, CancellationToken cancellationToken = default) =>
                throw new NotSupportedException("this scenario only reads the settings surface");

            public Task<IReadOnlyDictionary<string, string>> ReadAllAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
        }

        static async Task<SettingDto> GetStationIconPackSetting(FakeIconPackStore iconPackStore)
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
            var controller = new SettingsController(
                config, new FakeSettingsStore(), new SettingValidator(config), NullLogger<SettingsController>.Instance,
                iconPackStore: iconPackStore)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            };

            var ok = Assert.IsType<OkObjectResult>(await controller.Get(CancellationToken.None));
            var items = Assert.IsAssignableFrom<IEnumerable<SettingDto>>(ok.Value);
            return items.Single(i => i.Key.Equals("Station:IconPack", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task ItsKindIsAChoiceNotFreeText()
        {
            var iconPack = await GetStationIconPackSetting(new FakeIconPackStore());

            Assert.Equal("choice", iconPack.Kind);
        }

        [Fact]
        public async Task ItsApplyModeIsLive()
        {
            var iconPack = await GetStationIconPackSetting(new FakeIconPackStore());

            Assert.Equal("live", iconPack.ApplyMode);
        }

        [Fact]
        public async Task WithNoPacksInstalledItsChoicesOfferHouseIconsAloneNeverAnEmptyList()
        {
            // Review finding F1 — the T303-as-built shape returned an EMPTY choices list for the most
            // common station state there is (zero packs installed, every fresh deploy) which is
            // exactly the "zero choices" shape the admin-ui ChoiceSettingControl treats as a wiring
            // bug and refuses to render (its own "settings API returned none" alert) rather than a
            // real, workable default. IconPackChoices now always carries the house-icons choice first.
            var iconPack = await GetStationIconPackSetting(new FakeIconPackStore());

            var expected = new[] { new SettingChoice("", "House icons", IsDefault: true) };
            Assert.Equal(expected, iconPack.Choices);
        }

        [Fact]
        public async Task ItsChoicesLeadWithHouseIconsThenEveryInstalledPackSlugDoublingAsItsOwnLabel()
        {
            // SPEC F130.1's own gw-icon-pack document has no pack-level display name — the slug IS
            // the only honest label (StationSettingsAllowlist.IconPackChoices' own remarks).
            var store = new FakeIconPackStore();
            await store.UpsertAsync("line-icons", "{}", "line-icons", CancellationToken.None);
            await store.UpsertAsync("solid-icons", "{}", "solid-icons", CancellationToken.None);

            var iconPack = await GetStationIconPackSetting(store);

            var expectedPacks = new[]
            {
                new SettingChoice("line-icons", "line-icons"),
                new SettingChoice("solid-icons", "solid-icons"),
            };
            var houseIcons = Assert.Single(iconPack.Choices!, c => c.Value == "");
            Assert.True(houseIcons.IsDefault);
            Assert.Equal("House icons", houseIcons.Label);
            Assert.Equal(
                expectedPacks.OrderBy(c => c.Value, StringComparer.Ordinal),
                iconPack.Choices!.Where(c => c.Value != "").OrderBy(c => c.Value, StringComparer.Ordinal));
        }

        [Fact]
        public async Task ADeadIconPackStoreDegradesToHouseIconsOnlyChoicesWithTheWarnLogged()
        {
            // Review finding F4(c) — the SettingsController degrade catch: a store that cannot be
            // reached must still answer a WORKING settings page (post-F1, "house icons only" is a
            // working dropdown, not the red alert an empty list would render) rather than 500ing the
            // whole GET /api/settings response, and the failure is loud server-side.
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
            var capturingLogger = new CapturingLogger<SettingsController>();
            var controller = new SettingsController(
                config, new FakeSettingsStore(), new SettingValidator(config), capturingLogger,
                iconPackStore: new ThrowingIconPackStore())
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            };

            var ok = Assert.IsType<OkObjectResult>(await controller.Get(CancellationToken.None));
            var items = Assert.IsAssignableFrom<IEnumerable<SettingDto>>(ok.Value);
            var iconPack = items.Single(i => i.Key.Equals("Station:IconPack", StringComparison.OrdinalIgnoreCase));

            var expected = new[] { new SettingChoice("", "House icons", IsDefault: true) };
            Assert.Equal(expected, iconPack.Choices);
            Assert.Contains(capturingLogger.Warnings, w => w.Contains("unavailable", StringComparison.OrdinalIgnoreCase));
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the write-time validator's fail-open branch (T303 review finding F3)
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheValidatorPinsTheFailOpenUninstallBranch
    {
        // Mirrors what Station:Theme's own write-time guard would carry if pinned directly
        // (SettingValidator.IsValidThemeSlug's own membership check) — Station:IconPack's own guard is
        // SHAPE-ONLY (SettingValidator.IsValidIconPackSlug's own remarks): no membership check against
        // currently-installed station.icon_pack rows exists, deliberately, because a dangling slug (the
        // F130.5 fail-open uninstall) is an EXPECTED, handled state, never a defect a stricter
        // write-time gate should prevent. These three facts are what must go red the day a future
        // membership tightening changes that.
        static SettingValidator Validator() =>
            new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build());

        [Fact]
        public void AnEmptyValueIsAcceptedTheHouseIconsDefault()
        {
            Assert.Null(Validator().Validate("Station:IconPack", ""));
        }

        [Fact]
        public void AWellFormedSlugNamingNoInstalledPackStillRoundTripsAPut()
        {
            // The fail-open uninstall's load-bearing half (SPEC F130.5) — existence against
            // station.icon_pack is deliberately NOT checked at write time.
            Assert.Null(Validator().Validate("Station:IconPack", "no-such-pack-installed"));
        }

        [Fact]
        public void AMalformedSlugIsRejected()
        {
            Assert.NotNull(Validator().Validate("Station:IconPack", "Not A Valid Slug!"));
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
        [Fact]
        public async Task TheActiveResolutionAnswersTheHouseSetAfterUninstall()
        {
            // Given a pack installed AND activated (Station:IconPack env-seeded — mirrors
            // Story265_ThemeSelectionAndPersistence.cs's own ApplianceModeWebFactory idiom for proving
            // a config-layer claim with no live database),
            await using var factory = new IconPackInstallWebFactory(activeIconPack: IconPackInstallFixtures.PackSlug);
            var client = await IconPackInstallWebFactory.LoggedInClientAsync(factory);
            var install = await client.PostAsync($"/api/icon-packs/{IconPackInstallFixtures.PackSlug}/install", null);
            Assert.True(install.IsSuccessStatusCode, await install.Content.ReadAsStringAsync());
            var activeBeforeDelete = await client.GetAsync("/api/icon-packs/active");
            Assert.Equal(HttpStatusCode.OK, activeBeforeDelete.StatusCode);

            // When it is uninstalled,
            var delete = await client.DeleteAsync($"/api/icon-packs/{IconPackInstallFixtures.PackSlug}");
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

            // Then the active-pack resolution answers house icons for the now-dangling
            // Station:IconPack value (SPEC F130.5) — never an error, and never touched by the DELETE
            // itself (the SAME env-seeded slug is still what Active reads; it simply no longer
            // resolves to an installed row).
            var activeAfterDelete = await client.GetAsync("/api/icon-packs/active");
            Assert.Equal(HttpStatusCode.NoContent, activeAfterDelete.StatusCode);
        }
    }
}

// ── Test harness ───────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for this file's own T303 install/uninstall/list/
/// active Facts — boots the real Program.cs graph with <c>Community:CatalogIndexUrl</c> pointed at
/// <see cref="IconPackInstallFixtures.IndexUrl"/> (served by a fake origin) and <see cref="IIconPackStore"/>
/// replaced by a <see cref="FakeIconPackStore"/> — mirrors
/// <c>Story332_AvatarPacksIntoTheLibrary.cs</c>'s own <c>AvatarPackInstallWebFactory</c>, simpler
/// (no <c>ImageNormalizeService</c>/ffmpeg seam to wire — an icon pack is pure JSON, SPEC F130.6's own
/// "no assets[]" rule).
/// </summary>
file sealed class IconPackInstallWebFactory(
    FakeIconPackStore? store = null, FakeHttpMessageHandler? handler = null,
    string catalogIndexUrl = IconPackInstallFixtures.IndexUrl, string? activeIconPack = null,
    ILogger<IconPackController>? capturingLogger = null) : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story337-iconinstall";

    readonly FakeHttpMessageHandler handler = handler ?? IconPackInstallFixtures.BuildRoutedHandler(IconPackInstallFixtures.DefinitionJson);
    readonly FakeIconPackStore store = store ?? new FakeIconPackStore();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Community:CatalogIndexUrl", catalogIndexUrl);

        // Station:IconPack env-seeded (mirrors Story265_ThemeSelectionAndPersistence.cs's own
        // ApplianceModeWebFactory idiom) — this is a config-layer concern, no live settings-overlay
        // database needed to prove Active's own resolution.
        if (activeIconPack is not null)
            builder.UseSetting("Station:IconPack", activeIconPack);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new SingleHandlerHttpClientFactory(handler));

            services.RemoveAll<IIconPackStore>();
            services.AddSingleton<IIconPackStore>(store);

            if (capturingLogger is not null)
            {
                services.RemoveAll<ILogger<IconPackController>>();
                services.AddSingleton(capturingLogger);
            }
        });
    }

    public static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }
}

/// <summary>
/// Fixture documents + a routed fake HTTP double for this file's own T303 Facts — a single valid
/// <c>kind:"icon"</c> entry, no <c>assets[]</c> at all (SPEC F130.6 — the manifest IS the pack body).
/// <c>file</c>-scoped (mirrors <c>AvatarPackInstallFixtures</c>'s own established idiom).
/// </summary>
file static class IconPackInstallFixtures
{
    public const string IndexUrl = "https://catalog.test/repo/icon-index.json";
    const string Directory = "https://catalog.test/repo/";

    public const string PackSlug = "line-icons";

    public const string DefinitionJson = """
        { "style": { "strokeWidth": 1.5, "fill": "none" },
          "icons": { "dashboard": [ { "tag": "circle", "cx": 8, "cy": 8, "r": 2 } ] } }
        """;

    /// <summary>PLAN T303 review riders 2/5 — an unknown top-level member (accepted-and-dropped) plus
    /// a genuinely DUPLICATE "dashboard" key (last-wins).</summary>
    public const string DefinitionWithNoiseJson = """
        { "unknownTopLevelMember": "should never reach storage",
          "style": { "strokeWidth": 1.5, "fill": "none" },
          "icons": {
            "dashboard": [ { "tag": "circle", "cx": 1, "cy": 1, "r": 1 } ],
            "dashboard": [ { "tag": "circle", "cx": 8, "cy": 8, "r": 2 } ]
          } }
        """;

    /// <summary>Two names outside <see cref="IconNameContract.Names"/> (SPEC F130.2).</summary>
    public const string DefinitionWithIgnoredNamesJson = """
        { "style": { "strokeWidth": 1.5, "fill": "none" },
          "icons": {
            "dashboard": [ { "tag": "circle", "cx": 8, "cy": 8, "r": 2 } ],
            "not-a-real-icon-slot": [ { "tag": "circle", "cx": 8, "cy": 8, "r": 2 } ],
            "another-unknown-slot": [ { "tag": "circle", "cx": 8, "cy": 8, "r": 2 } ]
          } }
        """;

    public const string HostileScriptTagJson = """
        { "style": { "strokeWidth": 1.5, "fill": "none" },
          "icons": { "dashboard": [ { "tag": "script", "d": "alert(1)" } ] } }
        """;

    /// <summary>An unbounded remote TAG value (PLAN T303 review rider 4's own "a 250 KiB tag yields a
    /// 250 KiB Reason" scenario, scaled down to 5,000 characters for a fast fact) — never bounded by
    /// any prior gate (unlike an icon NAME, which IconPackDefinitionParser caps before ever echoing
    /// it), so it reaches Validate's own rejection Reason verbatim.</summary>
    public static string HostileOverlongTagJson => $$"""
        { "style": { "strokeWidth": 1.5, "fill": "none" },
          "icons": { "dashboard": [ { "tag": "{{new string('a', 5000)}}", "d": "M0 0" } ] } }
        """;

    static string Sha256Hex(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    const string MetaJson = """
        {"author":"Test Fixture","description":"An icon pack for the install endpoint specs.","audience":"everyone","added":"2026-08-16"}
        """;

    static string IndexJson(string definitionJson) => $$"""
        { "generatedAt": "2026-08-16", "entries": [
          { "slug": "{{PackSlug}}", "kind": "icon", "audience": "everyone",
            "manifest": { "path": "entries/{{PackSlug}}/{{PackSlug}}.icon.json", "sha256": "{{Sha256Hex(definitionJson)}}" },
            "meta": { "path": "entries/{{PackSlug}}/{{PackSlug}}.meta.json", "sha256": "{{Sha256Hex(MetaJson)}}" } } ] }
        """;

    /// <summary>Serves every fixture document at its own resolved URL, 404 for anything else —
    /// <paramref name="definitionJson"/> is served BOTH as the index's own hash source and as the
    /// manifest body itself, so every Fact above supplies whatever shape (valid or hostile) definition
    /// its own scenario needs.</summary>
    public static FakeHttpMessageHandler BuildRoutedHandler(string definitionJson)
    {
        var routes = new Dictionary<string, string>
        {
            [IndexUrl] = IndexJson(definitionJson),
            [Directory + "entries/" + PackSlug + "/" + PackSlug + ".icon.json"] = definitionJson,
            [Directory + "entries/" + PackSlug + "/" + PackSlug + ".meta.json"] = MetaJson,
        };

        return new((request, _) =>
        {
            var absoluteUri = request.RequestUri!.AbsoluteUri;
            return Task.FromResult(
                routes.TryGetValue(absoluteUri, out var body)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
        });
    }
}

/// <summary>Minimal <see cref="ILogger{T}"/> that collects Warning-and-above messages for assertion
/// (mirrors <c>Story234_CatalogProxyGuardedDoor.cs</c>'s own copy of this idiom — a <c>file</c>-scoped
/// third copy, tolerated the same way that file's own remarks tolerate its own copy).</summary>
file sealed class CapturingLogger<T> : ILogger<T>
{
    public List<string> Warnings { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (logLevel >= LogLevel.Warning)
            Warnings.Add(formatter(state, exception));
    }
}

/// <summary>An <see cref="IIconPackStore"/> double whose every member throws — simulates "the icon-pack
/// store is unreachable" (SPEC F130.4, PLAN T303 review finding F4) so
/// <see cref="FeatureIconPacksSwapTheChrome.ScenarioStationIconPackIsAnAllowlistedLiveSettingFedByInstalledPacks"/>'s
/// own degrade fact can prove <c>SettingsController.IconPackChoicesAsync</c> degrades to house-icons-
/// only choices rather than 500ing the whole GET /api/settings response — mirrors
/// <c>Story271_OwnerThemeStorage.cs</c>'s own <c>ThrowingThemeStore</c> idiom.</summary>
file sealed class ThrowingIconPackStore : IIconPackStore
{
    public Task UpsertAsync(string slug, string definition, string importedFrom, CancellationToken ct) =>
        throw new InvalidOperationException("simulated DB failure");

    public Task<IconPack?> GetBySlugAsync(string slug, CancellationToken ct) =>
        throw new InvalidOperationException("simulated DB failure");

    public Task<IReadOnlyList<IconPack>> GetAllAsync(CancellationToken ct) =>
        throw new InvalidOperationException("simulated DB failure");

    public Task<IReadOnlyList<string>> GetAllSlugsAsync(CancellationToken ct) =>
        throw new InvalidOperationException("simulated DB failure");

    public Task<bool> DeleteAsync(string slug, CancellationToken ct) =>
        throw new InvalidOperationException("simulated DB failure");
}
