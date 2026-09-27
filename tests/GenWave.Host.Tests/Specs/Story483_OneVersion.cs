// STORY-483 — One version, read once, shown one way (gh-#9 + gh-#868 · SPEC F211.1–F211.2 · PLAN T589, T590)
//
// BDD specification — xUnit. RED at plan time: every fact is [Fact(Skip = Pending)] with a loud body —
// remove the Skip only in the task that makes it green. Each Given comment names the arrange the scenario needs.
// Entry point: GET /api/about + /spectator/api/about through WebApplicationFactory with IAppVersion
// replaced by one built from "5.13.2+abc1234"; the UA facts read each fetcher's outgoing request via a
// capturing HttpMessageHandler.

namespace GenWave.Host.Tests.Specs;

public static class FeatureOneVersion
{
    const string PendingAbout = "pending: T589 — About + spectator About inject IAppVersion (STORY-483)";
    const string PendingUa = "pending: T590 — User-Agent builders inject IAppVersion (STORY-483)";

    public sealed class ScenarioAboutShowsTheDisplayForm
    {
        // Given: host IAppVersion from "5.13.2+abc1234"; GET /api/about

        /// <summary>AC5 — version is "v5.13.2"</summary>
        [Fact(Skip = PendingAbout)]
        public void ReadsV5132() => Assert.Fail(PendingAbout);
    }

    public sealed class ScenarioSpectatorAboutShowsTheDisplayForm
    {
        // Given: host IAppVersion from "5.13.2+abc1234"; GET /spectator/api/about

        /// <summary>AC6 — version is "v5.13.2"</summary>
        [Fact(Skip = PendingAbout)]
        public void ReadsV5132() => Assert.Fail(PendingAbout);
    }

    public sealed class ScenarioTheUserAgentCarriesTheDisplayForm
    {
        // Given: host IAppVersion from "5.13.2+abc1234"; one fetch each through the MusicBrainz year
        //        lookup, the history context provider and CatalogHttpFetcher, each on a capturing handler

        const string Expected = "GenWave/v5.13.2 (+https://github.com/GenWave-Org/genwave)";

        /// <summary>AC8 — MusicBrainz UA is <see cref="Expected"/></summary>
        [Fact(Skip = PendingUa)]
        public void MusicBrainz() => Assert.Fail(PendingUa);

        /// <summary>AC8 — history UA is <see cref="Expected"/></summary>
        [Fact(Skip = PendingUa)]
        public void History() => Assert.Fail(PendingUa);

        /// <summary>AC8 — catalog UA is <see cref="Expected"/></summary>
        [Fact(Skip = PendingUa)]
        public void Catalog() => Assert.Fail(PendingUa);
    }
}
