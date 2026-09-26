// gh-#865 — an automatic re-render is visible on the wire (API half).
// The page half is specced in admin-ui/__specs__/ad-auto-rerender-note.spec.tsx; the booth-log half
// in GenWave.Ads.Tests/Specs/Gh865_AutoRerenderBoothLine.cs. Arranged by AdsApiArc
// (Story392_AdsApi.cs) — GET /api/ads/{id} against real Postgres.

using System.Text.Json;

namespace GenWave.Host.Tests.Specs;

public static class Gh865FeatureTheReRenderIsOnTheWire
{
    [Collection(AdsApiCollection.Name)]
    public sealed class ScenarioASwappedSpotIsRead(AdsApiArc arc)
    {
        [Fact]
        public void ItCarriesTheVersionAndTime()
        {
            var note = Assert.NotNull(arc.MarkedSpotAutoRerender);
            Assert.Equal(JsonValueKind.Object, note.ValueKind);
            Assert.Equal(AdsApiArc.AutoRerenderVersion, note.GetProperty("onVersion").GetString());
            Assert.Equal(AdsApiArc.AutoRerenderAt, note.GetProperty("at").GetDateTime().ToUniversalTime());
        }
    }

    [Collection(AdsApiCollection.Name)]
    public sealed class ScenarioANeverSwappedSpotIsRead(AdsApiArc arc)
    {
        [Fact]
        public void ItCarriesNoNote()
        {
            var note = arc.UnmarkedSpotAutoRerender;
            Assert.True(note is null || note.Value.ValueKind == JsonValueKind.Null);
        }
    }
}
