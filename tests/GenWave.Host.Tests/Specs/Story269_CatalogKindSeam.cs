// STORY-269 — The catalog admits a second kind (SPEC F103.1, F103.2, F103.3)
//
// BDD specification — xUnit. The seam the whole theme-catalog epic hangs off: the catalog entry
// model gains an explicit `kind` discriminator and a generalized {manifest, meta} shape, so a
// theme kind (and later font/icon/avatar) plugs in additively while personas keep working
// unchanged. This is an INVISIBLE refactor — personas default to kind:"persona", the shelf is
// unchanged, and no theme exists yet.
//
// Also fixes the format contract: golden.theme.json is a real ThemeManifest committed to both the
// app tests and (T178+) the catalog repo, pinned byte-stable — the concrete .theme.json shape both
// repos lock onto.
//
// PENDING T176 / T177 — flip live as the kind seam lands. One assertion per Fact; happy path
// first and exhaustive; the sad path (unknown kind vs unknown audience) is its own block.

using Xunit;

namespace GenWave.Host.Tests.Specs;

public static class FeatureCatalogKindDiscriminator
{
    const string PendingSeam = "pending T176 — catalog kind discriminator";
    const string PendingGolden = "pending T177 — golden.theme.json format contract";

    // ── HAPPY PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioAnIndexEntryCarriesAKind
    {
        [Fact(Skip = PendingSeam)]
        public void EachEntryExposesItsKind()
        {
            // Given a catalog index with a persona entry and a theme entry,
            // When the index is parsed,
            // Then each entry exposes its kind ("persona" | "theme") (AC1).
            Assert.Fail(PendingSeam);
        }
    }

    public sealed class ScenarioAPersonaEntryWithoutAnExplicitKindDefaultsToPersona
    {
        [Fact(Skip = PendingSeam)]
        public void ItsKindResolvesToPersona()
        {
            // Given a legacy index entry authored before the kind field,
            // When it is parsed,
            // Then its kind resolves to "persona" (AC2) — back-compat for the shipped shelf.
            Assert.Fail(PendingSeam);
        }
    }

    public sealed class ScenarioTheTwoFileModelIsKindNeutral
    {
        [Fact(Skip = PendingSeam)]
        public void FileReferencesAreExposedAsManifestAndMeta()
        {
            // Given any entry,
            // When its file references are read,
            // Then they are exposed as {manifest, meta} (the persona `card` renamed) (AC3).
            Assert.Fail(PendingSeam);
        }
    }

    public sealed class ScenarioAThemeManifestReferenceUsesTheThemePattern
    {
        [Fact(Skip = PendingSeam)]
        public void TheManifestPathMatchesTheThemeFilePattern()
        {
            // Given a theme entry,
            // When its manifest path is validated,
            // Then it matches entries/<slug>/<slug>.theme.json (AC4), while a persona's stays
            //      <slug>.persona.json.
            Assert.Fail(PendingSeam);
        }
    }

    public sealed class ScenarioTheGoldenThemeFixtureRoundTrips
    {
        [Fact(Skip = PendingGolden)]
        public void ItIsByteIdenticalThroughTheManifestParser()
        {
            // Given the committed golden.theme.json exported from a real theme,
            // When it is parsed as a ThemeManifest and re-serialized,
            // Then it is byte-identical (AC5) — the concrete format contract, pinned in both repos.
            Assert.Fail(PendingGolden);
        }
    }

    // ── SAD PATH ────────────────────────────────────────────────────────────

    public sealed class ScenarioAnUnknownKindIsSkippedNotFatal
    {
        [Fact(Skip = PendingSeam)]
        public void TheRestOfTheIndexStillLoads()
        {
            // Given an index entry whose kind the app does not recognise (a future font/icon/avatar),
            // When the index is parsed,
            // Then that entry is skipped and the rest of the index still loads (AC6) — forward-compat.
            Assert.Fail(PendingSeam);
        }
    }

    public sealed class ScenarioAnUnknownAudienceStillRejectsTheIndex
    {
        [Fact(Skip = PendingSeam)]
        public void TheWholeIndexIsRejected()
        {
            // Given an entry with an unrecognised audience,
            // When the index is parsed,
            // Then the whole index is rejected (AC7) — audience is content-safety, unlike kind
            //      which is forward-compat. The two must not be conflated.
            Assert.Fail(PendingSeam);
        }
    }
}
