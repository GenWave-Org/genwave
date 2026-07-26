// STORY-234 — The proxy: one guarded door to the shelf (SPEC F90.1–F90.4, PLAN T99–T101)
//
// BDD specification — xUnit, pending until /build-loop turns each fact live. Entry-point
// discipline: every scenario drives the production surface (WebApplicationFactory<Program>
// against /api/catalog/*) with the upstream catalog faked at the HTTP boundary — never by
// calling proxy internals.
//
// T99 (SPEC F90.1) is the one exception: it ships no endpoint, so its two facts below —
// ScenarioValidatorEnforcesTheUrlRule and ScenarioAccessorIsFailClosed — are real, always-run
// unit coverage of SettingValidator's Community:CatalogIndexUrl rule and CommunityCatalogAccessor,
// the two seams T101 builds its endpoints on top of. Same "direct SettingValidator construction"
// idiom as Story124_EndpointLiveness.cs/Story149_SettingCeilings.cs.

using Microsoft.Extensions.Configuration;
using GenWave.Host.Configuration;
using GenWave.Host.Options;

namespace GenWave.Host.Tests.Specs;

public static class FeatureCatalogProxyGuardedDoor
{
    public sealed class ScenarioFetchVerifyCache
    {
        // Given Community:CatalogIndexUrl configured and a valid faked upstream (index +
        // entries with correct sha256), When /api/catalog/index is called twice within TTL.

        [Fact(Skip = "Pending (T100/T101)")]
        public void FirstCallFetchesAndHashVerifiesTheIndex() { }

        [Fact(Skip = "Pending (T100/T101)")]
        public void SecondCallWithinTtlServesFromCacheWithoutUpstreamHit() { }

        [Fact(Skip = "Pending (T100/T101)")]
        public void ResponseCarriesTheFetchedAtTimestamp() { }

        [Fact(Skip = "Pending (T100/T101)")]
        public void EntryFetchResolvesPathsRelativeToTheIndexUrl() { }
    }

    public sealed class ScenarioStaleBeatsAbsent
    {
        // Given a warm cache, When the upstream starts failing and TTL expires.

        [Fact(Skip = "Pending (T100/T101)")]
        public void CachedIndexIsServedAfterUpstreamFailure() { }

        [Fact(Skip = "Pending (T100/T101)")]
        public void StaleResponseKeepsItsOriginalFetchedAtTimestamp() { }
    }

    public sealed class ScenarioRejectingEmptyUrl
    {
        // Sad path — Given Community:CatalogIndexUrl = "" (fail-closed, F90.1). T99 shipped the
        // option, its validator (empty is legal — see ScenarioValidatorEnforcesTheUrlRule below),
        // and the CommunityCatalogAccessor fail-closed read side T101 wires into these two
        // endpoints; the endpoints themselves don't exist until T101.

        [Fact(Skip = "Pending (T101)")]
        public void IndexEndpointReturns404WhenUrlIsEmpty() { }

        [Fact(Skip = "Pending (T101)")]
        public void EntryEndpointReturns404WhenUrlIsEmpty() { }
    }

    public sealed class ScenarioRejectingHostileIndex
    {
        // Sad path — Given an upstream index containing an absolute entry URL or a
        // path-traversing relative path (F90.2).

        [Fact(Skip = "Pending (T100/T101)")]
        public void IndexWithAbsoluteEntryUrlIsRejectedWholesale() { }

        [Fact(Skip = "Pending (T100/T101)")]
        public void IndexWithPathTraversingEntryIsRejectedWholesale() { }

        [Fact(Skip = "Pending (T100/T101)")]
        public void RejectionWarnNamesTheOffendingPath() { }
    }

    public sealed class ScenarioRejectingTamperedContent
    {
        // Sad path — Given one entry whose fetched bytes mismatch the index sha256 (F90.3).

        [Fact(Skip = "Pending (T100/T101)")]
        public void MismatchedEntryIsWithheldWith502() { }

        [Fact(Skip = "Pending (T100/T101)")]
        public void RemainingEntriesStillServeWhileOneIsWithheld() { }

        [Fact(Skip = "Pending (T100/T101)")]
        public void OversizeCardIsWithheldBeforeCaching() { }
    }

    // ---------------------------------------------------------------------
    // T99 — real, always-run coverage (SPEC F90.1): the option + validator + fail-closed accessor
    // this whole story's endpoints (T100/T101) are built on top of.
    // ---------------------------------------------------------------------

    public sealed class ScenarioValidatorEnforcesTheUrlRule
    {
        // Mirrors Llm:Endpoint/Tts:Fallback:Endpoint's own "empty legal, else absolute http/https"
        // shape (Story124_EndpointLiveness.cs's sibling coverage for those two keys) — empty is the
        // F90.1 kill switch, not an error.

        static SettingValidator BuildValidator() => new(new ConfigurationBuilder().Build());

        [Fact]
        public void AnAbsoluteHttpsUrlIsAccepted()
        {
            var error = BuildValidator().Validate(
                "Community:CatalogIndexUrl",
                "https://raw.githubusercontent.com/GenWave-Org/genwave-catalog/main/index.json");

            Assert.Null(error);
        }

        [Fact]
        public void EmptyIsAcceptedAsTheFailClosedKillSwitch()
        {
            var error = BuildValidator().Validate("Community:CatalogIndexUrl", "");

            Assert.Null(error);
        }

        [Fact]
        public void ARelativePathIsRejected()
        {
            var error = BuildValidator().Validate("Community:CatalogIndexUrl", "index.json");

            Assert.NotNull(error);
        }

        [Fact]
        public void AnFtpSchemeIsRejected()
        {
            var error = BuildValidator().Validate(
                "Community:CatalogIndexUrl", "ftp://example.test/index.json");

            Assert.NotNull(error);
        }

        [Fact]
        public void GarbageIsRejected()
        {
            var error = BuildValidator().Validate("Community:CatalogIndexUrl", "not a url");

            Assert.NotNull(error);
        }

        [Fact]
        public void TheRejectionMessageNamesTheUrlRule()
        {
            var error = BuildValidator().Validate("Community:CatalogIndexUrl", "not a url");

            Assert.NotNull(error);
            Assert.Contains("absolute http/https URL", error, StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioAccessorIsFailClosed
    {
        // CommunityCatalogAccessor is the fail-closed read side T101's endpoints consume — an
        // empty CatalogIndexUrl (the F90.1 kill switch) must resolve to IsEnabled=false and a null
        // IndexUrl, never an empty string a caller might mistake for "no constraint".

        static CommunityCatalogAccessor BuildAccessor(string catalogIndexUrl)
        {
            var monitor = new FakeOptionsMonitor<CommunityOptions>(
                new CommunityOptions { CatalogIndexUrl = catalogIndexUrl });
            return new CommunityCatalogAccessor(monitor);
        }

        const string ConfiguredUrl = "https://raw.githubusercontent.com/GenWave-Org/genwave-catalog/main/index.json";

        [Fact]
        public void AConfiguredUrlIsEnabled()
        {
            var accessor = BuildAccessor(ConfiguredUrl);

            Assert.True(accessor.IsEnabled);
        }

        [Fact]
        public void AConfiguredUrlIsExposed()
        {
            var accessor = BuildAccessor(ConfiguredUrl);

            Assert.Equal(ConfiguredUrl, accessor.IndexUrl);
        }

        [Fact]
        public void AnEmptyUrlIsDisabled()
        {
            var accessor = BuildAccessor("");

            Assert.False(accessor.IsEnabled);
        }

        [Fact]
        public void AnEmptyUrlExposesNoIndexUrl()
        {
            var accessor = BuildAccessor("");

            Assert.Null(accessor.IndexUrl);
        }

        [Fact]
        public void AWhitespaceOnlyUrlIsAlsoDisabled()
        {
            // Mirrors IsNonBlank's own discipline elsewhere in the allowlist (Station:Name/Voice) —
            // whitespace is not a real value, so it degrades to the same fail-closed state as "".
            var accessor = BuildAccessor("   ");

            Assert.False(accessor.IsEnabled);
        }
    }
}
