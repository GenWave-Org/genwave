// STORY-427 — Music picker is the installed music (SPEC F174.7 · PLAN T446)

namespace GenWave.Host.Tests.Specs;

public static class FeatureMusicPickerIsTheInstalledMusic
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheBrowseRouteFiltersByImagingKindAndJingleRole
    {
        [Fact]
        public void TheFilteredBrowseIs200()
            => Assert.Fail("pending: T446 GET /api/media?imagingKind=jingle&jingleRole=bed — AC1");

        [Fact]
        public void ExactlyTheEightBedsAreReturned()
            => Assert.Fail("pending: T446 filter exactness — AC1");

        [Fact]
        public void EachRowCarriesIdTitleAndPack()
            => Assert.Fail("pending: T446 row shape incl. pack name — AC1");
    }

    // ---------------------------------------------------------------------
    // SAD PATH (segregated)
    // ---------------------------------------------------------------------

    public sealed class ScenarioRejectingInvalidFilterValues
    {
        [Fact]
        public void AnUnknownImagingKindIs400()
            => Assert.Fail("pending: T446 400 unknown imaging kind — AC2");
    }
}
