// STORY-417 — Owner sponsors are real; pack sponsors stay parody (SPEC F172.5 · PLAN T438)
//
// Rule-id mapping (PLAN T438 ruling): STORY-417's own AC2/AC4 name the rule ids
// "phone_not_fictional"/"brand_blocklisted" — those are the SAME two shipped checks Story390 already
// pinned on the wire as AdScriptRuleIds.PhoneShape ("phone_shape") and AdScriptRuleIds.BrandCollision
// ("brand_collision"); nothing here renames either one, the STORY's own vocabulary just maps onto the
// ids that already ship. Every assertion below compares against the shipped constants.

using GenWave.Ads.Tests.Fakes;
using GenWave.Ads.Tests.Support;
using GenWave.Core.Domain;

namespace GenWave.Ads.Tests.Specs;

public static class FeatureOwnerSponsorsAreRealPackSponsorsStayParody
{
    // ---------------------------------------------------------------------
    // Fixture literals (PLAN T438 ruling — see class remarks below)
    // ---------------------------------------------------------------------

    const string SponsorPhone = "(406) 222-0100";
    const string OtherPhone = "(406) 222-0199";

    // STORY-417's own AC4/AC5 use the fictional "Northside Bakery"/"Southside Bakery" pair as its
    // illustrative example; BrandBlocklist.txt is a real-world-brands list — no fictional entries
    // belong in it, and no test seam exists to add one — so these three literals are the shipped-list
    // stand-ins for that same pair (PLAN T438 ruling). N shares a word with E1 ON PURPOSE — "pictures"
    // — and that shared word is what a fuzzy per-word skip (rather than the ruled exact-phrase skip)
    // would strip out of N too: N's own blocklist match depends on BOTH its words together (no bare
    // "paramount" entry exists to fall back on), so a fuzzy implementation wrongly accepts N while the
    // exact-phrase one still refuses it.
    const string SponsorName = "Universal Pictures";               // E1 — a multi-word blocklist entry.
    const string DifferentBlocklistedBrand = "Mountain Dew";       // E2 — a different multi-word entry,
                                                                     // sharing no word with E1.
    const string NearName = "Paramount Pictures";                  // N — shares "Pictures" with E1,
                                                                     // matches its OWN two-word entry.

    static readonly AdScriptValidationRequest OwnerRequest = new(
        Posture: AudiencePosture.Everyone, MaxLineChars: 200, SpotSeconds: 30, ToleranceRatio: 0.4,
        SponsorName: SponsorName, SponsorPhone: SponsorPhone, IsPackOwned: false);

    static AdScriptValidationResult Validate(string script, AdScriptValidationRequest request) =>
        AdScriptValidator.Validate(script, request, new FakePatterDurationEstimator());

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioOwnerSponsorsOwnPhonePasses
    {
        [Fact]
        public void AScriptSpeakingTheSponsorsExactPhonePasses()
        {
            const string script =
                "ANNOUNCER: Cravin's Diner has a deal so good it's almost illegal.\n" +
                "ANNOUNCER: Call (406) 222-0100 today.";

            var result = Validate(script, OwnerRequest);

            Assert.IsType<AdScriptValidationResult.Accepted>(result);
        }

        // PLAN T438 ruling: the phone skip compares DIGITS, never the raw punctuation — the script
        // speaks the sponsor's own number in a different format than SponsorPhone carries it and still
        // passes.
        [Fact]
        public void AFormattingDifferenceFromTheSponsorsPhoneStillPasses()
        {
            const string script =
                "ANNOUNCER: Cravin's Diner has a deal so good it's almost illegal.\n" +
                "ANNOUNCER: Call 406-222-0100 today.";

            var result = Validate(script, OwnerRequest);

            Assert.IsType<AdScriptValidationResult.Accepted>(result);
        }
    }

    public sealed class ScenarioOwnerSponsorsOwnNamePassesTheBlocklist
    {
        [Fact]
        public void AScriptNamingTheSponsorPasses()
        {
            const string script =
                $"ANNOUNCER: {SponsorName} has a deal so good it's almost illegal.\n" +
                "ANNOUNCER: Call 555-0100 today.";

            var result = Validate(script, OwnerRequest);

            Assert.IsType<AdScriptValidationResult.Accepted>(result);
        }

        // PLAN T438 ruling: two mentions separated only by punctuation fold adjacent (no word falls
        // between them once "." folds away to nothing) — a single non-overlapping Replace pass would
        // consume the pad space the second mention needs and leave it sitting in the blocklist match.
        [Fact]
        public void AScriptNamingTheSponsorTwiceSeparatedOnlyByPunctuationPasses()
        {
            const string script =
                $"ANNOUNCER: Fresh every day at {SponsorName}. {SponsorName}, on Main Street.\n" +
                "ANNOUNCER: Call 555-0100 today.";

            var result = Validate(script, OwnerRequest);

            Assert.IsType<AdScriptValidationResult.Accepted>(result);
        }

        // PLAN T438 ruling: the SAME adjacency the punctuation-only fact exercises, but the two
        // mentions sit on separate ANNOUNCER lines — CheckBrandCollision folds the whole script's
        // joined text before this strip ever runs, so a line break folds away exactly like punctuation
        // does and the two mentions are adjacent there too.
        [Fact]
        public void AScriptNamingTheSponsorTwiceAcrossALineBreakPasses()
        {
            const string script =
                $"ANNOUNCER: {SponsorName}\n" +
                $"ANNOUNCER: {SponsorName} — call 555-0100 today.";

            var result = Validate(script, OwnerRequest);

            Assert.IsType<AdScriptValidationResult.Accepted>(result);
        }

        // PLAN T438 ruling: the sponsor's name sits glued between two unrelated words — collapsing the
        // strip's double-space padding back to a single space would fuse "mountain" and "dew" into a
        // fabricated match against the UNRELATED "Mountain Dew" blocklist entry; left as a two-space
        // boundary, it never can.
        [Fact]
        public void TheSponsorsNameGluedBetweenTwoOtherWordsNeverFabricatesACollision()
        {
            const string script =
                $"ANNOUNCER: Mountain {SponsorName} Dew has a deal so good it's almost illegal.\n" +
                "ANNOUNCER: Call 555-0100 today.";

            var result = Validate(script, OwnerRequest);

            Assert.IsType<AdScriptValidationResult.Accepted>(result);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH (segregated)
    // ---------------------------------------------------------------------

    public sealed class ScenarioStillRefusing
    {
        [Fact]
        public void ADifferentNon555NumberRefusesOnPhoneNotFictional()
        {
            const string script =
                "ANNOUNCER: Cravin's Diner has a deal so good it's almost illegal.\n" +
                $"ANNOUNCER: Call {OtherPhone} today.";

            var result = Validate(script, OwnerRequest);

            var refused = Assert.IsType<AdScriptValidationResult.Refused>(result);
            Assert.Equal(AdScriptRuleIds.PhoneShape, refused.Violation.RuleId);
        }

        [Fact]
        public void APackSponsorsNon555NumberRefuses()
        {
            const string script =
                "ANNOUNCER: Cravin's Diner has a deal so good it's almost illegal.\n" +
                $"ANNOUNCER: Call {SponsorPhone} today.";
            // The SAME sponsor facts as OwnerRequest, but pack-owned (SPEC F172.5's own parody
            // posture) — the phone skip never runs regardless of what SponsorPhone carries.
            var request = OwnerRequest with { IsPackOwned = true };

            var result = Validate(script, request);

            var refused = Assert.IsType<AdScriptValidationResult.Refused>(result);
            Assert.Equal(AdScriptRuleIds.PhoneShape, refused.Violation.RuleId);
        }

        [Fact]
        public void APackSponsorsOwnNameStillRefuses()
        {
            const string script =
                $"ANNOUNCER: {SponsorName} has a deal so good it's almost illegal.\n" +
                "ANNOUNCER: Call 555-0100 today.";
            // The SAME sponsor facts as OwnerRequest, but pack-owned (SPEC F172.5's own parody
            // posture) — the name skip never runs regardless of what SponsorName carries.
            var request = OwnerRequest with { IsPackOwned = true };

            var result = Validate(script, request);

            var refused = Assert.IsType<AdScriptValidationResult.Refused>(result);
            Assert.Equal(AdScriptRuleIds.BrandCollision, refused.Violation.RuleId);
        }

        [Fact]
        public void ADifferentBlocklistedBrandRefusesOnBrandBlocklisted()
        {
            const string script =
                $"ANNOUNCER: {DifferentBlocklistedBrand} has a deal so good it's almost illegal.\n" +
                "ANNOUNCER: Call 555-0100 today.";

            var result = Validate(script, OwnerRequest);

            var refused = Assert.IsType<AdScriptValidationResult.Refused>(result);
            Assert.Equal(AdScriptRuleIds.BrandCollision, refused.Violation.RuleId);
        }

        [Fact]
        public void ANearNameLikeSouthsideBakeryStillRefuses()
        {
            const string script =
                $"ANNOUNCER: {NearName} has a deal so good it's almost illegal.\n" +
                "ANNOUNCER: Call 555-0100 today.";

            var result = Validate(script, OwnerRequest);

            var refused = Assert.IsType<AdScriptValidationResult.Refused>(result);
            Assert.Equal(AdScriptRuleIds.BrandCollision, refused.Violation.RuleId);
        }
    }

    // ---------------------------------------------------------------------
    // WORKER WIRING (PLAN T438 ruling: pins AdSpotWorker.GenerateOneAsync's own IsPackOwned argument —
    // sponsor.PackSlug, never brief.PackSlug (see AdSpotWorker.cs's own remarks above its
    // AdScriptValidationRequest construction) — against the deployed call site itself, not just the
    // validator this class's other Scenarios exercise directly.)
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheWorkerWiresTheSponsorFieldsItResolves
    {
        static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

        // The Story389_AdStockKeeping WellFormedReply precedent, brand swapped to this file's own
        // SponsorName — well under the 30s target with a 555 number, so nothing but the brand check
        // itself can refuse it.
        const string ScriptNamingTheSponsor =
            $"ANNOUNCER: {SponsorName} has a deal so good it's almost illegal.\n" +
            "VOICE1: Almost. Stop by and taste the difference tonight.\n" +
            "ANNOUNCER: Call 555-0142 - that's 555-0142 - the deal of the year.";

        // The SAME shape as ScriptNamingTheSponsor, with the sponsor's own (non-555) phone number in
        // place of the 555 line — well under the 30s target, so nothing but the phone check itself can
        // refuse it.
        const string ScriptSpeakingTheSponsorsPhone =
            $"ANNOUNCER: {SponsorName} has a deal so good it's almost illegal.\n" +
            "VOICE1: Almost. Stop by and taste the difference tonight.\n" +
            $"ANNOUNCER: Call {SponsorPhone} - that's {SponsorPhone} - the deal of the year.";

        [Fact]
        public async Task AnOwnerBriefNamingItsOwnSponsorLands()
        {
            // Given an OWNER brief (pack_slug null) naming an owner-owned sponsor (the FakeSponsorStore
            // default — never marked pack-owned) in the generated script...
            var harness = AdSpotWorkerHarness.Build(
                Now, llmHandler: AdSpotWorkerHarness.ServeSameReplyEveryTime(ScriptNamingTheSponsor));
            harness.Briefs.AddEnabled(SponsorName, premise: "A deal too good to pass up", tone: "playful");

            // When the worker ticks...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then the spot lands — the sponsor's own name never trips the blocklist.
            var created = Assert.Single(harness.Store.CreateRequests);
            Assert.NotEqual(AdState.Failed, created.InitialState);
            Assert.Null(created.FailReason);
        }

        [Fact]
        public async Task AnOwnerBriefSpeakingItsOwnSponsorsPhoneLands()
        {
            // Given an OWNER brief naming an owner-owned sponsor whose own phone is on file (PLAN T438
            // ruling: AdSpotWorker.GenerateOneAsync resolves SponsorPhone from the SAME sponsorStore.
            // GetAsync read as the name, never a second lookup), and a generated script that speaks
            // that exact (non-555) number...
            var harness = AdSpotWorkerHarness.Build(
                Now, llmHandler: AdSpotWorkerHarness.ServeSameReplyEveryTime(ScriptSpeakingTheSponsorsPhone));
            harness.Briefs.AddEnabled(SponsorName, premise: "A deal too good to pass up", tone: "playful");
            harness.Sponsors.WithPhone(harness.Briefs.SponsorIdsByBrand[SponsorName], SponsorPhone);

            // When the worker ticks...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then the spot lands — the sponsor's own phone never trips the 555 rule.
            var created = Assert.Single(harness.Store.CreateRequests);
            Assert.NotEqual(AdState.Failed, created.InitialState);
            Assert.Null(created.FailReason);
        }

        [Fact]
        public async Task TheSameOwnerBriefUnderAPackOwnedSponsorRefusesOnBrandCollision()
        {
            // Given the SAME owner brief (pack_slug still null — only the SPONSOR moves to pack-owned)
            // naming the same sponsor in the same script...
            var harness = AdSpotWorkerHarness.Build(
                Now, llmHandler: AdSpotWorkerHarness.ServeSameReplyEveryTime(ScriptNamingTheSponsor));
            harness.Briefs.AddEnabled(SponsorName, premise: "A deal too good to pass up", tone: "playful");
            harness.Sponsors.MarkPackOwned(harness.Briefs.SponsorIdsByBrand[SponsorName]);

            // When the worker ticks...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then it refuses on brand_collision — the name skip never runs for a pack-owned sponsor,
            // regardless of the brief's own (unrelated) pack_slug.
            var created = Assert.Single(harness.Store.CreateRequests);
            Assert.Equal(AdState.Failed, created.InitialState);
            Assert.Contains("blocklisted brand", created.FailReason);
        }
    }
}
