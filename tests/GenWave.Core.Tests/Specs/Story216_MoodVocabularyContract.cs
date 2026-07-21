// STORY-216 — Tracks get moods from a bounded vocabulary
//
// BDD specification — xUnit (SPEC F85.1, F85.2). Pure reflection/value checks over the
// GenWave.Abstractions-hosted vocabulary constant — no I/O. PLAN T58. The DB-backed write-path
// facts (rejecting an out-of-vocabulary write, the ≤3 cap) live in GenWave.MediaLibrary.Tests
// (Story216_MoodTagEnrichment.cs, ScenarioBoundedTagging).

using GenWave.Core.Domain;

namespace GenWave.Core.Tests.Specs;

public static class FeatureMoodVocabularyContract
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — the term list's shape (F85.1)
    // ---------------------------------------------------------------------

    public sealed class ScenarioTermListShape
    {
        [Fact]
        public void HasBetween12And16Terms()
        {
            Assert.InRange(MoodVocabulary.Terms.Count, 12, 16);
        }

        [Fact]
        public void EveryTermIsLowercase()
        {
            Assert.All(MoodVocabulary.Terms, t => Assert.Equal(t.ToLowerInvariant(), t));
        }

        [Fact]
        public void EveryTermIsASingleWordWithNoSeparators()
        {
            // "kebab-free single words" — no spaces, no hyphens, no underscores.
            Assert.All(MoodVocabulary.Terms, t => Assert.DoesNotContain(t, c => c is ' ' or '-' or '_'));
        }

        [Fact]
        public void EveryTermIsDistinct()
        {
            Assert.Equal(MoodVocabulary.Terms.Count, MoodVocabulary.Terms.Distinct().Count());
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the ≤3-per-track cap (F85.2)
    // ---------------------------------------------------------------------

    public sealed class ScenarioMaxMoodsPerTrackCap
    {
        [Fact]
        public void CapIsThree()
        {
            Assert.Equal(3, MoodVocabulary.MaxMoodsPerTrack);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — membership (the single predicate the write path shares)
    // ---------------------------------------------------------------------

    public sealed class ScenarioContainsMembership
    {
        [Fact]
        public void AVocabularyTermIsAMember()
        {
            Assert.True(MoodVocabulary.Contains("dreamy"));
        }

        [Fact]
        public void AnUnknownTermIsNotAMember()
        {
            Assert.False(MoodVocabulary.Contains("not-a-real-mood"));
        }

        [Fact]
        public void MembershipIsCaseSensitive()
        {
            // The vocabulary is defined as already-lowercase; a mixed-case variant of a real term is
            // NOT itself a member — callers normalize before asking, this is not a fuzzy match.
            Assert.False(MoodVocabulary.Contains("Dreamy"));
        }
    }
}
