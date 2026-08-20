// gh-#161 — Context-aware pronunciation corrections for heteronyms
//
// BDD specification — xUnit (SPEC F68.5 extension). A blanket wind→wynd rule fixes "time to wind
// down" and breaks "a strong wind"; these specs pin the context-conditioned rule shape that makes
// a correction heteronym-safe: optional whenFollowedBy / whenPrecededBy word conditions compiled
// into the SAME escaped, boundary-anchored, case-insensitive, timeout-guarded chokepoint pattern
// every context-free rule already uses. Back-compat is the headline contract: a rule that carries
// no context parses, matches, merges, and fingerprints exactly as it did before this feature.
// (Expected strings are post-flatten — gh-#541 lowercases booth copy and removes clause marks
// after corrections run; each scenario's claim is about the RULE firing or not, which the
// flatten never disturbs.)

using System.Text.Json;
using Xunit;

namespace GenWave.Tts.Tests.Specs;

public static class FeatureContextAwareCorrections
{
    public sealed class ScenarioFollowedByCondition
    {
        // The issue's own motivating pair, in one sentence: verb sense fires, noun sense survives.
        readonly SpeechCorrectionSet rules = SpeechCorrectionSet.Create(
            [new SpeechCorrection("wind", "wynd") { WhenFollowedBy = "down|up" }]);

        [Fact]
        public void FiresOnlyWhereTheContextHolds()
        {
            var result = SpeechText.Normalize("Time to wind down after a strong wind tonight.", rules);
            Assert.Equal("time to wynd down after a strong wind tonight.", result);
        }

        [Fact]
        public void EveryAlternativeCounts()
        {
            var result = SpeechText.Normalize("We wind up the show soon.", rules);
            Assert.Equal("we wynd up the show soon.", result);
        }

        [Fact]
        public void DoesNotFireWithoutTheContext()
        {
            var result = SpeechText.Normalize("The wind was howling.", rules);
            Assert.Equal("the wind was howling.", result);
        }

        [Fact]
        public void ContextWordIsWordBoundaryAware()
        {
            // "downtown" must not satisfy "followed by down" — same \b discipline as From itself.
            var result = SpeechText.Normalize("They wind downtown streets.", rules);
            Assert.Equal("they wind downtown streets.", result);
        }

        [Fact]
        public void MatchAndContextAreCaseInsensitive()
        {
            var result = SpeechText.Normalize("WIND DOWN with us.", rules);
            Assert.Equal("wynd down with us.", result);
        }

        [Fact]
        public void PunctuationBetweenMatchAndContextIsAllowed()
        {
            var result = SpeechText.Normalize("Your wind-down mix starts now.", rules);
            Assert.Equal("your wynd-down mix starts now.", result);
        }

        [Fact]
        public void ASentenceEndBreaksTheContext()
        {
            // "followed by down" must not reach across a full stop into the next sentence.
            var result = SpeechText.Normalize("Feel that wind. Down the coast it is worse.", rules);
            Assert.Equal("feel that wind. down the coast it is worse.", result);
        }
    }

    public sealed class ScenarioPrecededByCondition
    {
        readonly SpeechCorrectionSet rules = SpeechCorrectionSet.Create(
            [new SpeechCorrection("record", "wreckerd") { WhenPrecededBy = "a|the|that|new" }]);

        [Fact]
        public void FiresOnlyWhereTheContextHolds()
        {
            var result = SpeechText.Normalize("Spin that record while we record the show.", rules);
            Assert.Equal("spin that wreckerd while we record the show.", result);
        }

        [Fact]
        public void DoesNotFireAtTextStartWithoutTheContext()
        {
            var result = SpeechText.Normalize("Record this moment.", rules);
            Assert.Equal("record this moment.", result);
        }

        [Fact]
        public void ContextWordIsWordBoundaryAware()
        {
            // "data" must not satisfy "preceded by a".
            var result = SpeechText.Normalize("Their data record shows it.", rules);
            Assert.Equal("their data record shows it.", result);
        }
    }

    public sealed class ScenarioBothConditionsMustHold
    {
        readonly SpeechCorrectionSet rules = SpeechCorrectionSet.Create(
            [new SpeechCorrection("tear", "tair") { WhenPrecededBy = "to", WhenFollowedBy = "through" }]);

        [Fact]
        public void FiresWhenBothHold()
        {
            var result = SpeechText.Normalize("Ready to tear through the setlist.", rules);
            Assert.Equal("ready to tair through the setlist.", result);
        }

        [Theory]
        [InlineData("Ready to tear it up.", "ready to tear it up.")]        // followed-by fails
        [InlineData("A single tear through it all.", "a single tear through it all.")] // preceded-by fails
        public void DoesNotFireWhenEitherFails(string input, string flattened)
        {
            // The rule must not fire; only the gh-#541 speakability flatten touches the copy.
            Assert.Equal(flattened, SpeechText.Normalize(input, rules));
        }
    }

    public sealed class ScenarioMultiWordContextAlternative
    {
        [Fact]
        public void AnAlternativeMayBeAPhrase()
        {
            var rules = SpeechCorrectionSet.Create(
                [new SpeechCorrection("tear", "tair") { WhenFollowedBy = "it up|through" }]);

            var result = SpeechText.Normalize("We tear it up tonight; no tear was shed.", rules);
            Assert.Equal("we tair it up tonight no tear was shed.", result);
        }
    }

    public sealed class ScenarioOverlappingSpecificAndGeneralRules
    {
        [Fact]
        public void SpecificFirstThenGeneralComposesInOperatorOrder()
        {
            // Rules apply in order: the context rule rewrites its occurrences, the later blanket
            // rule catches whatever is left — the operator-authorable "specific first" idiom.
            var rules = SpeechCorrectionSet.Create(
            [
                new SpeechCorrection("wind", "wynd") { WhenFollowedBy = "down|up" },
                new SpeechCorrection("wind", "winnd"),
            ]);

            var result = SpeechText.Normalize("Let's wind down before the wind picks up.", rules);
            Assert.Equal("let's wynd down before the winnd picks up.", result);
        }
    }

    public sealed class ScenarioBackwardCompatibility
    {
        [Fact]
        public void AContextFreeRuleBehavesExactlyAsBefore()
        {
            var rules = SpeechCorrectionSet.Create([new SpeechCorrection("MacLeod", "Muh-cloud")]);
            var result = SpeechText.Normalize("A deep cut from MacLeod.", rules);
            Assert.Equal("a deep cut from muh-cloud.", result);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("|")]
        [InlineData(" | | ")]
        public void ABlankContextMeansUnconditional(string? blankContext)
        {
            // Blank (or all-separator) conditions parse to no words at all — treated as absent,
            // never as "matches nothing" (which would silently disable the rule).
            var rules = SpeechCorrectionSet.Create(
                [new SpeechCorrection("wind", "wynd") { WhenFollowedBy = blankContext, WhenPrecededBy = blankContext }]);

            var result = SpeechText.Normalize("A strong wind tonight.", rules);
            Assert.Equal("a strong wynd tonight.", result);
        }

        [Fact]
        public void StoredJsonWithoutContextFieldsStillParses()
        {
            // The exact pre-gh-#161 wire shape, via the same STJ options SpeechCorrectionProvider uses.
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var rules = JsonSerializer.Deserialize<List<SpeechCorrection>>(
                """[{"from":"MacLeod","to":"Muh-cloud"}]""", options);

            Assert.NotNull(rules);
            var rule = Assert.Single(rules);
            Assert.Null(rule.WhenPrecededBy);
            Assert.Null(rule.WhenFollowedBy);
        }

        [Fact]
        public void StoredJsonWithContextFieldsParses()
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var rules = JsonSerializer.Deserialize<List<SpeechCorrection>>(
                """[{"from":"wind","to":"wynd","whenFollowedBy":"down|up","whenPrecededBy":"to"}]""", options);

            Assert.NotNull(rules);
            var rule = Assert.Single(rules);
            Assert.Equal("down|up", rule.WhenFollowedBy);
            Assert.Equal("to", rule.WhenPrecededBy);
        }
    }

    public sealed class ScenarioCardOverStationMerge
    {
        [Fact]
        public void CardWinsOnAnIdenticalContextFreeFrom()
        {
            // F97.4 amends F71.7: same From, both context-free → the card wins, station dropped
            // (reverses the shipped station-wins contract this scenario used to pin).
            var station = SpeechCorrectionSet.Create([new SpeechCorrection("wind", "station")]);
            var card = SpeechCorrectionSet.Create([new SpeechCorrection("WIND", "card")]);

            var result = SpeechText.Normalize("The wind.", SpeechCorrectionSet.Merge(station, card));
            Assert.Equal("the card.", result);
        }

        [Fact]
        public void CardWinsOnAnIdenticalContextRule()
        {
            // Same From AND same (whitespace/case-normalized) context → same rule identity; F97.4
            // flips the winner to the card side.
            var station = SpeechCorrectionSet.Create(
                [new SpeechCorrection("wind", "station") { WhenFollowedBy = "down|up" }]);
            var card = SpeechCorrectionSet.Create(
                [new SpeechCorrection("wind", "card") { WhenFollowedBy = " Down | UP " }]);

            var result = SpeechText.Normalize("We wind down.", SpeechCorrectionSet.Merge(station, card));
            Assert.Equal("we card down.", result);
        }

        [Fact]
        public void ACardRuleWithADifferentContextIsADifferentRuleAndSurvives()
        {
            var station = SpeechCorrectionSet.Create(
                [new SpeechCorrection("wind", "wynd") { WhenFollowedBy = "down|up" }]);
            var card = SpeechCorrectionSet.Create([new SpeechCorrection("wind", "winnd")]);
            var merged = SpeechCorrectionSet.Merge(station, card);

            // Different context => different identity => neither rule is dropped by the merge —
            // that part of the claim still holds, even though (below) the station rule can no
            // longer be observed firing.
            Assert.Equal(2, merged.Rules.Count());

            // Card rules are now ordered ahead of station rules (F97.4/orchestrator ruling): the
            // card's blanket rule rewrites every occurrence of "wind" — including the one
            // station's context rule would have claimed — before the station rule ever runs, so
            // the more specific station rule never fires here. No station rule ever pre-empts a
            // card rule: every card rule gets its turn on the text before any station rule runs,
            // even on a non-identical overlap like this one, not only on an identical identity.
            var result = SpeechText.Normalize("We wind down as the wind howls.", merged);
            Assert.Equal("we winnd down as the winnd howls.", result);
        }

        [Fact]
        public void ACardSubPhraseRuleWinsOverAStationWordRule()
        {
            // Executed case from review: a card rule for a SUB-PHRASE ("MacLeod Duncan") has a
            // different identity than the station's rule for just "MacLeod" — under the old
            // identity-only merge the station rule ran first and won "MacLeod Duncan.", leaving
            // "station-way Duncan." The orchestrator ruling is that no station rule ever pre-empts
            // a card rule — every card rule gets its turn on the text before any station rule
            // runs, not only when its identity is identical — ordering the card rule first
            // delivers that even for a non-identical, non-overlapping-identity pair.
            var station = SpeechCorrectionSet.Create([new SpeechCorrection("MacLeod", "station-way")]);
            var card = SpeechCorrectionSet.Create([new SpeechCorrection("MacLeod Duncan", "card-way")]);

            var result = SpeechText.Normalize(
                "Here comes MacLeod Duncan.", SpeechCorrectionSet.Merge(station, card));
            Assert.Equal("here comes card-way.", result);
        }

        [Fact]
        public void AShadowedCardRuleDoesNotRelocateToTheEndOfTheList()
        {
            // Latent bug the reviewer found in the identity-only merge: this card rule
            // ("MacLeod"->CARDFIRED) does NOT collide by identity with the station's
            // ("MacLeod Duncan"->stn-full) — they are different identities, so the old merge kept
            // both. But the old merge always put every station rule ahead of every non-colliding
            // card rule (station rules first, surviving card rules appended at the END), so the
            // station's more specific "MacLeod Duncan" rule ran FIRST by position and consumed
            // the whole span before the card rule ever got a look: "MacLeod Duncan speaks." came
            // out "stn-full speaks.", the card firing nowhere at all (re-verified against the
            // pre-flip algorithm). Ordering card rules ahead of every station rule fixes this: the
            // card rule now runs first regardless of which station rule it does or doesn't share
            // identity with.
            //
            // The card's replacement is deliberately a token that does not itself contain
            // "macleod" — Apply rewrites sequentially (SPEC F68.7), so a replacement that DID
            // contain it would hand the still-surviving "MacLeod Duncan" station rule a brand
            // new occurrence to latch onto, muddying what this spec is pinning.
            var station = SpeechCorrectionSet.Create([new SpeechCorrection("MacLeod Duncan", "stn-full")]);
            var card = SpeechCorrectionSet.Create([new SpeechCorrection("MacLeod", "CARDFIRED")]);

            var result = SpeechText.Normalize(
                "MacLeod Duncan speaks.", SpeechCorrectionSet.Merge(station, card));
            Assert.Equal("cardfired duncan speaks.", result);
        }
    }

    public sealed class ScenarioContentFingerprint
    {
        const string Sentinel = "no-rules-sentinel";

        static string FingerprintOf(params SpeechCorrection[] corrections) =>
            CorrectionsFingerprint.Compute(SpeechCorrectionSet.Create(corrections).Rules, Sentinel);

        [Fact]
        public void AContextFreeRuleSetKeepsItsPreFeatureFingerprint()
        {
            // The canonical encoding for context-free rules is byte-identical to the pre-gh-#161
            // one (From␟To joined by ␞) — pinned here against the algorithm itself so an operator
            // who never touches the new fields sees zero TtsSegmentSource cache churn on upgrade.
            var expected = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes("MacLeod\x1FMuh-cloud\x1EGenWave\x1FJen Wave")))[..16];

            Assert.Equal(expected, FingerprintOf(
                new SpeechCorrection("MacLeod", "Muh-cloud"), new SpeechCorrection("GenWave", "Jen Wave")));
        }

        [Fact]
        public void AddingAContextChangesTheFingerprint()
        {
            var without = FingerprintOf(new SpeechCorrection("wind", "wynd"));
            var with = FingerprintOf(new SpeechCorrection("wind", "wynd") { WhenFollowedBy = "down|up" });

            Assert.NotEqual(without, with);
        }

        [Fact]
        public void EditingOnlyTheContextChangesTheFingerprint()
        {
            var followedByDown = FingerprintOf(new SpeechCorrection("wind", "wynd") { WhenFollowedBy = "down" });
            var followedByUp = FingerprintOf(new SpeechCorrection("wind", "wynd") { WhenFollowedBy = "up" });

            Assert.NotEqual(followedByDown, followedByUp);
        }

        [Fact]
        public void SameRulesAlwaysFoldToTheSameFingerprint()
        {
            var first = FingerprintOf(new SpeechCorrection("wind", "wynd") { WhenFollowedBy = "down|up" });
            var second = FingerprintOf(new SpeechCorrection("wind", "wynd") { WhenFollowedBy = "down|up" });

            Assert.Equal(first, second);
        }
    }
}
