// STORY-254 — A place to edit how the DJ says it (gh-#284), PLAN T144 review finding F5
//
// PronunciationRuleSet.MergeWithProvenance projects the SAME persona/station merge Merge() encodes,
// but tags every rule with its source and whether it is the one currently in effect — a shadowed
// station row is INCLUDED (InEffect: false) rather than dropped the way Merge()'s own output drops
// it. This file pins that projection directly, plus the parity invariant the review asked for: the
// InEffect-true subset can never disagree with Merge()'s own winning set, because MergeWithProvenance
// computes InEffect by calling Merge() itself — not a parallel identity check.

namespace GenWave.Tts.Tests.Specs;

public static class FeatureMergedPronunciationView
{
    public static class ScenarioProvenanceIsTagged
    {
        [Fact]
        public static void A_card_rule_is_always_in_effect()
        {
            var station = PronunciationRuleSet.Create([]);
            var card = PronunciationRuleSet.Create([new PronunciationRule("MacLeod", "MacLeod", "/cardIpa/")]);

            var rows = PronunciationRuleSet.MergeWithProvenance(station, card);

            Assert.True(Assert.Single(rows, r => r.Source == PronunciationRuleSource.Persona).InEffect);
        }

        [Fact]
        public static void A_station_only_rule_is_in_effect()
        {
            var station = PronunciationRuleSet.Create([new PronunciationRule("Reykjavík", "Reykjavík", "/x/")]);
            var card = PronunciationRuleSet.Create([]);

            var rows = PronunciationRuleSet.MergeWithProvenance(station, card);

            Assert.True(Assert.Single(rows, r => r.Source == PronunciationRuleSource.Station).InEffect);
        }

        [Fact]
        public static void A_shadowed_station_rule_is_marked_not_in_effect()
        {
            // Same (pattern, word) identity on both sides — the card wins (F97.4) — but unlike
            // Merge()'s own output, the shadowed station rule still gets a row here.
            var station = PronunciationRuleSet.Create([new PronunciationRule("MacLeod", "MacLeod", "/stationIpa/")]);
            var card = PronunciationRuleSet.Create([new PronunciationRule("MacLeod", "MacLeod", "/cardIpa/")]);

            var rows = PronunciationRuleSet.MergeWithProvenance(station, card);

            Assert.False(Assert.Single(rows, r => r.Source == PronunciationRuleSource.Station).InEffect);
        }
    }

    public static class ScenarioParityWithMerge
    {
        [Fact]
        public static void The_in_effect_true_subset_matches_Merges_own_winning_set()
        {
            // A genuinely contended merge: one clean station-only rule, one clean card-only rule, and
            // one shadowed identity — Merge()'s own compiled Rules is the independent oracle
            // MergeWithProvenance's InEffect projection must never disagree with.
            var station = PronunciationRuleSet.Create([
                new PronunciationRule("Reykjavík", "Reykjavík", "/stationOnly/"),
                new PronunciationRule("MacLeod", "MacLeod", "/shadowedStation/"),
            ]);
            var card = PronunciationRuleSet.Create([
                new PronunciationRule("Duncan", "Duncan", "/cardOnly/"),
                new PronunciationRule("MacLeod", "MacLeod", "/shadowingCard/"),
            ]);

            var winningIpas = PronunciationRuleSet.Merge(station, card).Rules.Select(r => r.Ipa).ToHashSet();
            var inEffectIpas = PronunciationRuleSet.MergeWithProvenance(station, card)
                .Where(row => row.InEffect)
                .Select(row => row.Rule.Ipa)
                .ToHashSet();

            Assert.Equal(winningIpas, inEffectIpas);
        }
    }
}
