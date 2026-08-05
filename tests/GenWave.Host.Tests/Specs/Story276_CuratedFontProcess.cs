// STORY-276 — Curated-font process (SPEC F103.10)
//
// BDD specification — xUnit. FONTS.md documents the four-step curated-font process (OFL-confirm,
// record provenance, latin-subset, measure against the ceiling) and the per-theme byte ceiling;
// this file specs the runtime half of PLAN T188 — ThemeFontProvenanceValidator, wired at
// ThemeCatalog.LoadShipped() and the theme import route (Story272_ThemeImport.cs's own
// AManifestReferencingAnUnvendoredFontIsRefusedWith400 proves the SAME validator through the real
// import route; this file drives the validator itself plus LoadShipped()'s own use of it).
//
// A data-driven Theory proves every embedded theme passes (the parser already pins the font src
// URL SHAPE — ThemeManifestParser.FontSrcPattern — what this file's sad-path specs prove is the
// EXISTENCE and byte-ceiling checks the parser never attempted).

using System.Linq;
using GenWave.Host.Theming;
using Xunit;

namespace GenWave.Host.Tests.Specs;

public static class FeatureCuratedFontProcess
{
    // ── HAPPY PATH ────────────────────────────────────────────────────────

    public sealed class ScenarioEveryShippedThemePassesProvenance
    {
        /// <summary>Reads the shipped set rather than hardcoding slugs (T191 later trims six to
        /// two — mirrors Story271_OwnerThemeStorage.cs's own "reads the count rather than
        /// hardcoding it" precedent) — every shipped theme gets its OWN Theory case, so a future
        /// regression names the offending slug rather than one opaque boot failure.</summary>
        public static TheoryData<string> ShippedThemeSlugs
        {
            get
            {
                var data = new TheoryData<string>();
                foreach (var slug in ThemeCatalog.LoadShipped().All.Select(theme => theme.Slug))
                    data.Add(slug);

                return data;
            }
        }

        [Theory]
        [MemberData(nameof(ShippedThemeSlugs), MemberType = typeof(ScenarioEveryShippedThemePassesProvenance))]
        public void TheThemeReferencesOnlyVendoredFacesWithinTheCeiling(string slug)
        {
            // Arrange: the real shipped theme, loaded through the real production path
            //          (ThemeCatalog.LoadShipped() — the same canary Program.cs runs at boot, and
            //          which already calls ThemeFontProvenanceValidator itself; this spec pins the
            //          claim per-theme rather than relying on "boot didn't throw").
            var catalog = ThemeCatalog.LoadShipped();
            Assert.True(catalog.TryGetBySlug(slug, out var theme));

            // Act/Assert: validating it against the real provenance record and ceiling never throws.
            var exception = Record.Exception(() => ThemeFontProvenanceValidator.Validate(
                theme, FontProvenanceCatalog.Default.BySrc, ThemeFontProvenanceValidator.PerThemeByteCeilingBytes));
            Assert.Null(exception);
        }
    }

    public sealed class ScenarioTheNewCuratedFacesAreVendored
    {
        /// <summary>PLAN T189, Dean-approved 2026-08-05: JetBrains Mono + Grenze Gotisch. Reads the
        /// real embedded provenance record — the same one <see cref="FontEndpoints"/> and
        /// <see cref="ThemeFontProvenanceValidator"/> use — rather than the repo's <c>.woff2</c>
        /// files directly, mirroring this file's own "real production path" posture.</summary>
        [Theory]
        [InlineData("/fonts/jetbrains-mono-variable-latin.woff2", 47392)]
        [InlineData("/fonts/grenze-gotisch-variable-latin.woff2", 51992)]
        public void TheFaceIsInTheProvenanceRecordWithItsMeasuredByteCount(string src, long expectedBytes)
        {
            var found = FontProvenanceCatalog.Default.BySrc.TryGetValue(src, out var face);

            Assert.True(found, $"'{src}' is missing from the vendored provenance record.");
            Assert.NotNull(face);
            Assert.Equal(expectedBytes, face.Bytes);
        }
    }

    // ── SAD PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioAnUnvendoredFontIsRejected
    {
        [Fact]
        public void ItNamesTheMissingFaceAndTheVendoredSet()
        {
            // Arrange: a theme whose display face names a src the URL-shape check would accept but
            //          no provenance entry backs — the EXISTENCE gap ThemeManifestParser.FontSrcPattern
            //          alone never closes.
            var theme = CuratedFontFixtures.ThemeReferencingUnvendoredDisplayFont("/fonts/nonexistent.woff2");

            // Act: validated against the real vendored set,
            var ex = Assert.Throws<ThemeManifestException>(() => ThemeFontProvenanceValidator.Validate(
                theme, FontProvenanceCatalog.Default.BySrc, ThemeFontProvenanceValidator.PerThemeByteCeilingBytes));

            // Assert: the message names the missing face AND the whole vendored set — never just
            //          "invalid theme".
            Assert.Contains("/fonts/nonexistent.woff2", ex.Message, StringComparison.Ordinal);
            Assert.Contains("/fonts/fraunces-variable-latin.woff2", ex.Message, StringComparison.Ordinal);
            Assert.Contains("/fonts/fraunces-italic-variable-latin.woff2", ex.Message, StringComparison.Ordinal);
            Assert.Contains("/fonts/source-sans-3-variable-latin.woff2", ex.Message, StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioAnOverBudgetThemeIsRejected
    {
        [Fact]
        public void ItNamesTheTotalAndTheCeiling()
        {
            // Arrange: a FAKE provenance record (a TEST fixture, never the real one — PLAN T188's
            //          own "you may add a fake face entry in a TEST fixture provenance record")
            //          carrying one face heavier than the ceiling, and a theme naming only that
            //          face for both roles,
            const long fakeFaceBytes = ThemeFontProvenanceValidator.PerThemeByteCeilingBytes + 1;
            var fakeProvenance = CuratedFontFixtures.FakeProvenanceCatalog("/fonts/oversized-fake.woff2", fakeFaceBytes);
            var theme = CuratedFontFixtures.ThemeReferencingOnly("/fonts/oversized-fake.woff2");

            // Act: validated against the FAKE provenance record and the real ceiling constant,
            var ex = Assert.Throws<ThemeManifestException>(() => ThemeFontProvenanceValidator.Validate(
                theme, fakeProvenance.BySrc, ThemeFontProvenanceValidator.PerThemeByteCeilingBytes));

            // Assert: the message names the total byte count and the ceiling — never just "too big".
            Assert.Contains(fakeFaceBytes.ToString(), ex.Message, StringComparison.Ordinal);
            Assert.Contains(
                ThemeFontProvenanceValidator.PerThemeByteCeilingBytes.ToString(), ex.Message, StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioTheBasePairPlusBothNewFacesExceedsTheCeiling
    {
        [Fact]
        public void ItNamesTheRealTotalAndTheCeiling()
        {
            // Arrange: a theme referencing all FIVE real vendored faces — the base pair (Fraunces,
            //          Fraunces italic, Source Sans 3) PLUS both PLAN T189 additions (JetBrains Mono,
            //          Grenze Gotisch). Unlike ScenarioAnOverBudgetThemeIsRejected above, this is a
            //          REAL over-ceiling case now that both new faces are vendored (FONTS.md's own
            //          "pairing constraint": base pair 138,272 + both new faces = 237,656 exceeds the
            //          204,800-byte ceiling) — no fake fixture provenance record needed.
            var theme = CuratedFontFixtures.ThemeReferencingBasePairPlusBothNewFaces();

            // Act: validated against the REAL, embedded provenance record and the real ceiling
            //      constant,
            var ex = Assert.Throws<ThemeManifestException>(() => ThemeFontProvenanceValidator.Validate(
                theme, FontProvenanceCatalog.Default.BySrc, ThemeFontProvenanceValidator.PerThemeByteCeilingBytes));

            // Assert: the message names the real summed total and the real ceiling.
            Assert.Contains("237656", ex.Message, StringComparison.Ordinal);
            Assert.Contains(
                ThemeFontProvenanceValidator.PerThemeByteCeilingBytes.ToString(), ex.Message, StringComparison.Ordinal);
        }
    }
}

/// <summary>Theme/provenance builders local to this file — this file is the only one PLAN T188 is
/// scoped to touch, so fixtures live here rather than a shared Fakes/ helper (mirrors Story263's own
/// file-local <c>ThemeFixtures</c>).</summary>
file static class CuratedFontFixtures
{
    /// <summary>A theme whose SANS face names a real vendored src (so only the display face's
    /// unvendored src is under test) and whose DISPLAY face names <paramref name="displaySrc"/>.
    /// </summary>
    public static ThemeManifest ThemeReferencingUnvendoredDisplayFont(string displaySrc) => new(
        Slug: "font-fixture-theme",
        Name: "Font Fixture Theme",
        Author: "GenWave",
        Fonts: new ThemeFonts(
            new ThemeFontFace("Fraunces", [new ThemeFontAsset(displaySrc, "400 600", "normal")]),
            new ThemeFontFace(
                "Source Sans 3", [new ThemeFontAsset("/fonts/source-sans-3-variable-latin.woff2", "400", "normal")])),
        Modes: new ThemeModes(
            new Dictionary<string, string> { ["bg"] = "#f6efe3" },
            new Dictionary<string, string> { ["bg"] = "#1e1713" }));

    /// <summary>A theme whose display AND sans faces both name the SAME <paramref name="src"/> —
    /// one distinct referenced face, so a byte-ceiling assertion counts its bytes exactly once.
    /// </summary>
    public static ThemeManifest ThemeReferencingOnly(string src) => new(
        Slug: "font-fixture-theme",
        Name: "Font Fixture Theme",
        Author: "GenWave",
        Fonts: new ThemeFonts(
            new ThemeFontFace("Fake Family", [new ThemeFontAsset(src, "400 600", "normal")]),
            new ThemeFontFace("Fake Family", [new ThemeFontAsset(src, "400", "normal")])),
        Modes: new ThemeModes(
            new Dictionary<string, string> { ["bg"] = "#f6efe3" },
            new Dictionary<string, string> { ["bg"] = "#1e1713" }));

    /// <summary>A theme whose display face names all THREE base-pair-plus-display faces (Fraunces,
    /// Fraunces italic, Grenze Gotisch) and whose sans face names the remaining two (Source Sans 3,
    /// JetBrains Mono) — five distinct real vendored srcs total, i.e. the base pair plus both PLAN
    /// T189 additions (FONTS.md's own "pairing constraint" sad path).</summary>
    public static ThemeManifest ThemeReferencingBasePairPlusBothNewFaces() => new(
        Slug: "font-fixture-theme",
        Name: "Font Fixture Theme",
        Author: "GenWave",
        Fonts: new ThemeFonts(
            new ThemeFontFace(
                "Fraunces",
                [
                    new ThemeFontAsset("/fonts/fraunces-variable-latin.woff2", "400 600", "normal"),
                    new ThemeFontAsset("/fonts/fraunces-italic-variable-latin.woff2", "400 600", "italic"),
                    new ThemeFontAsset("/fonts/grenze-gotisch-variable-latin.woff2", "400", "normal"),
                ]),
            new ThemeFontFace(
                "Source Sans 3",
                [
                    new ThemeFontAsset("/fonts/source-sans-3-variable-latin.woff2", "400", "normal"),
                    new ThemeFontAsset("/fonts/jetbrains-mono-variable-latin.woff2", "400", "normal"),
                ])),
        Modes: new ThemeModes(
            new Dictionary<string, string> { ["bg"] = "#f6efe3" },
            new Dictionary<string, string> { ["bg"] = "#1e1713" }));

    /// <summary>A minimal, single-face provenance record built through
    /// <see cref="FontProvenanceCatalog.Parse"/> — a TEST fixture, never
    /// <see cref="FontProvenanceCatalog.Default"/>'s real embedded one.</summary>
    public static FontProvenanceCatalog FakeProvenanceCatalog(string src, long bytes)
    {
        var file = src["/fonts/".Length..];
        var json = $$"""
            {
              "faces": [
                {
                  "family": "Fake Family",
                  "file": "{{file}}",
                  "sourceUrl": "https://example.invalid/fake",
                  "license": "OFL-1.1",
                  "subset": "latin",
                  "bytes": {{bytes}}
                }
              ]
            }
            """;
        return FontProvenanceCatalog.Parse(json);
    }
}
