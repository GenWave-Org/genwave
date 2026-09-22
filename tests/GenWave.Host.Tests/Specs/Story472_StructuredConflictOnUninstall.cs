// STORY-472 — Structured conflict on uninstall (SPEC F204.3 · PLAN T563)
//
// BDD specification — xUnit. RED at plan time: every fact is [Fact(Skip = Pending)] with a loud body —
// remove the Skip only in the task that makes it green. Each Given comment names the arrange the scenario needs.

namespace GenWave.Host.Tests.Specs;

public static class FeatureStructuredconflictonuninstall
{
    const string Pending = "pending: T563 — Structured conflict on uninstall (STORY-472)";

    public sealed class ScenarioDeleteAFontATheeReferences
    {
        // Given: WebApplicationFactory, a theme referencing the font, DELETE /api/fonts/{slug}

        /// <summary>AC8 — extensions.referencedBy = [theme names]</summary>
        [Fact(Skip = Pending)]
        public void NamesTheReferrer() => Assert.Fail(Pending);
    }

    public sealed class ScenarioTheSixDeleteRoutes
    {
        // Given: fonts, icons, avatars, ad-packs, jingle-packs, voice-packs each with a referrer

        /// <summary>AC9 — every 409 carries referencedBy</summary>
        [Fact(Skip = Pending)]
        public void IsStructuredOnEveryKind() => Assert.Fail(Pending);
    }

}
