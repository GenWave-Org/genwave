// STORY-300 — This day in history, honestly (F109, gh-#382)

namespace GenWave.Context.Tests.Specs;

public static class FeatureHistoryProvider
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioParaphraseNeverInvention
    {
        [Fact(Skip = "Pending T228 — see docs/PLAN.md")]
        public void SegmentFactsDeriveFromTheFetchedPayload()
        {
            // Fake Wikimedia handler returns a canned On-This-Day payload; every fact in
            // ContextContent traces to a payload entry (no synthesized events).
            // Assert.All(facts, f => Assert.Contains(f.Anchor, payloadEntries));
            Assert.Fail("pending T228");
        }
    }

    public sealed class ScenarioDayFileCache
    {
        [Fact(Skip = "Pending T228 — see docs/PLAN.md")]
        public void AFetchedDayPersistsAsAJsonFile()
        {
            // {CacheRoot}/context/history/{MM-dd}.json exists after the first fetch.
            // Assert.True(File.Exists(expectedPath));
            Assert.Fail("pending T228");
        }

        [Fact(Skip = "Pending T228 — see docs/PLAN.md")]
        public void ACacheHitCostsZeroNetwork()
        {
            // Second ask for the same day ⇒ zero HTTP calls.
            // Assert.Equal(1, fakeHandler.CallCount);
            Assert.Fail("pending T228");
        }

        [Fact(Skip = "Pending T228 — see docs/PLAN.md")]
        public void TheNextDayPreFetches()
        {
            // Fetching today also fetches tomorrow's file (the fallback-segment duty).
            // Assert.True(File.Exists(tomorrowPath));
            Assert.Fail("pending T228");
        }

        [Fact(Skip = "Pending T228 — see docs/PLAN.md")]
        public void OldDayFilesSweep()
        {
            // Files older than the retention horizon are removed on the next poll.
            // Assert.False(File.Exists(staleFilePath));
            Assert.Fail("pending T228");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioOutages
    {
        [Fact(Skip = "Pending T228 — see docs/PLAN.md")]
        public void UnreachableWikimediaWithACachedFileStillServes()
        {
            // Handler throws, day file exists ⇒ ContextContent served from the file.
            // Assert.NotNull(content);
            Assert.Fail("pending T228");
        }

        [Fact(Skip = "Pending T228 — see docs/PLAN.md")]
        public void UnreachableWikimediaWithNoCacheReturnsNull()
        {
            // No file, no network ⇒ null (skip semantics; one Information line upstream).
            // Assert.Null(content);
            Assert.Fail("pending T228");
        }
    }
}
