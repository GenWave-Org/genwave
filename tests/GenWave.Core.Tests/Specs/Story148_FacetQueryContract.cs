// STORY-148 — Eligibility curation by exact artist, album, and genre (Epic Y / SPEC F52.1, F52.3,
// closes gitea-#189) — Core contract half. The SQL half lives in
// MediaLibrary.Tests/Specs/Story148_FacetsAndExactFilterSql.cs; the API half in
// Host.Tests/Specs/Story148_FacetsEndpointAndExactParams.cs.
//
// BDD specification — xUnit. Pure reflection over Core (no I/O). Mirrors the Story050
// ReenrichmentContract reflection style.
//
// Pins the new seam shapes: IMediaCatalog.GetFacetsAsync and MediaQuery's exact-filter fields.

using System.Reflection;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Core.Tests.Specs;

public static class FeatureFacetQueryContract
{
    public sealed class ScenarioTheFacetSeamExists
    {
        [Fact]
        public void GetFacetsAsyncIsDeclaredOnIMediaCatalog()
        {
            var method = typeof(IMediaCatalog).GetMethod("GetFacetsAsync");
            Assert.NotNull(method);

            Assert.Equal(typeof(Task<IReadOnlyList<FacetValue>>), method.ReturnType);

            var parameters = method.GetParameters();
            Assert.Equal(3, parameters.Length);
            Assert.Equal(typeof(FacetField), parameters[0].ParameterType);
            Assert.Equal(typeof(LibraryScope), parameters[1].ParameterType);
            Assert.Equal(typeof(CancellationToken), parameters[2].ParameterType);
        }

        [Fact]
        public void FacetValueCarriesValueAndCount()
        {
            var facet = new FacetValue(Value: "Rock", Count: 12);

            Assert.Equal("Rock", facet.Value);
            Assert.Equal(12, facet.Count);
        }
    }

    public sealed class ScenarioMediaQueryCarriesExactFilters
    {
        [Fact]
        public void MediaQueryDeclaresArtistExactAlbumExactAndGenresExact()
        {
            var query = new MediaQuery(
                ArtistExact: "Queen",
                AlbumExact: "A Night at the Opera",
                GenresExact: ["Rock", "Progressive Rock"]);

            Assert.Equal("Queen", query.ArtistExact);
            Assert.Equal("A Night at the Opera", query.AlbumExact);
            Assert.Equal(["Rock", "Progressive Rock"], query.GenresExact);
        }

        [Fact]
        public void TheShippedSubstringFieldsAreUntouched()
        {
            // The exact fields are additive: a query can carry a field's shipped ILIKE substring
            // value (Artist/Genre/Q) alongside its new exact-match sibling with neither clobbering
            // the other — the controller (Y2) is what rejects naming both for the same field.
            var query = new MediaQuery(
                Artist: "quee",
                Genre: "rock",
                Q: "night",
                ArtistExact: "Queen",
                AlbumExact: "A Night at the Opera",
                GenresExact: ["Rock"]);

            Assert.Equal("quee", query.Artist);
            Assert.Equal("rock", query.Genre);
            Assert.Equal("night", query.Q);
            Assert.Equal("Queen", query.ArtistExact);
            Assert.Equal("A Night at the Opera", query.AlbumExact);
            Assert.Equal(["Rock"], query.GenresExact);
        }
    }
}
