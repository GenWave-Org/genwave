// STORY-424 — Preview key sensitivity (SPEC F174.4 · PLAN T442) — pins the exact
// algorithm GenWave.Host.Tests/Specs/Story424_PreviewRender.cs's own PreviewKeyIsSha256Hex fact points
// here for ("AdPreviewKey.Compute's own facts already pin the exact algorithm"): a direct, no-HTTP unit
// suite against AdPreviewKey.Compute itself, one fact per input class this preview render actually
// reads, each asserting a changed input changes the digest.

namespace GenWave.Ads.Tests.Specs;

using System.Text.RegularExpressions;
using GenWave.Core.Domain;

public static class FeaturePreviewKeyChangesWhenAnyInputChanges
{
    const string BaseScript = "ANNOUNCER: Come on down to the big sale.\nVOICE1: Prices you won't believe.";

    static AdSpot MakeSpot(
        string? script = BaseScript, string? voicePlan = null, long? bedMediaId = null, int spotSeconds = 30) =>
        new(
            Id: 1, SponsorId: 1, SponsorName: "Acme", Title: "Big Sale Spot", Brief: null, Script: script,
            Source: AdSource.Llm, PackSlug: null, SpotSeconds: spotSeconds, VoicePlan: voicePlan,
            BedMediaId: bedMediaId, State: AdState.Approved, FailReason: null, MediaId: null, Generation: 1,
            CreatedAt: DateTime.UtcNow, StateChangedAt: DateTime.UtcNow, RenderedAt: null,
            RetiredAt: null, Version: "1");

    static Sponsor MakeSponsor(
        string name = "Acme Anvils", string? tagline = "Built to last", string? about = "Since 1949",
        string? phone = "555-0100", string? address = "1 Anvil Way", string? website = "https://acme.example",
        string? tone = "warm") =>
        new(
            Id: 1, Name: name, PackSlug: null, Tagline: tagline, About: about, Phone: phone, Address: address,
            Website: website, Tone: tone, Paused: false, PausedAt: null,
            CreatedAt: DateTime.UtcNow, UpdatedAt: DateTime.UtcNow, Version: "1");

    static AdLiveSettings MakeLive(
        string announcerVoice = "af_heart", IReadOnlyList<string>? castVoices = null, int bedFadeMs = 300,
        double bedDuckDb = -12.0, double targetLufs = -16.0) =>
        new(announcerVoice, castVoices ?? ["voice_a", "voice_b"], BedFadeMs: bedFadeMs, BedDuckDb: bedDuckDb,
            TargetLufs: targetLufs);

    static string BaseKey() => AdPreviewKey.Compute(MakeSpot(), MakeSponsor(), MakeLive());

    // ---------------------------------------------------------------------
    // Spot-level inputs
    // ---------------------------------------------------------------------

    public sealed class ScenarioSpotFieldsChangeTheKey
    {
        [Fact]
        public void ScriptChangeChangesTheKey()
        {
            var before = AdPreviewKey.Compute(MakeSpot(script: BaseScript), MakeSponsor(), MakeLive());
            var after = AdPreviewKey.Compute(MakeSpot(script: BaseScript + " Extra line."), MakeSponsor(), MakeLive());
            Assert.NotEqual(before, after);
        }

        [Fact]
        public void VoicePlanChangeChangesTheKey()
        {
            var before = AdPreviewKey.Compute(MakeSpot(voicePlan: null), MakeSponsor(), MakeLive());
            var after = AdPreviewKey.Compute(
                MakeSpot(voicePlan: """[{"tag":"ANNOUNCER","voiceId":"af_heart"}]"""), MakeSponsor(), MakeLive());
            Assert.NotEqual(before, after);
        }

        [Fact]
        public void BedMediaIdChangeChangesTheKey()
        {
            var before = AdPreviewKey.Compute(MakeSpot(bedMediaId: null), MakeSponsor(), MakeLive());
            var after = AdPreviewKey.Compute(MakeSpot(bedMediaId: 42), MakeSponsor(), MakeLive());
            Assert.NotEqual(before, after);
        }

        [Fact]
        public void SpotSecondsChangeChangesTheKey()
        {
            var before = AdPreviewKey.Compute(MakeSpot(spotSeconds: 30), MakeSponsor(), MakeLive());
            var after = AdPreviewKey.Compute(MakeSpot(spotSeconds: 60), MakeSponsor(), MakeLive());
            Assert.NotEqual(before, after);
        }
    }

    // ---------------------------------------------------------------------
    // Sponsor-level inputs — every field AdPreviewKey.Compute reads off Sponsor (mutation 6: dropping
    // any of these from the canonical join must die here).
    // ---------------------------------------------------------------------

    public sealed class ScenarioEverySponsorFieldChangesTheKey
    {
        [Fact]
        public void NameChangeChangesTheKey()
        {
            var before = BaseKey();
            var after = AdPreviewKey.Compute(MakeSpot(), MakeSponsor(name: "Zenith Anvils"), MakeLive());
            Assert.NotEqual(before, after);
        }

        [Fact]
        public void TaglineChangeChangesTheKey()
        {
            var before = BaseKey();
            var after = AdPreviewKey.Compute(MakeSpot(), MakeSponsor(tagline: "Forged for the future"), MakeLive());
            Assert.NotEqual(before, after);
        }

        [Fact]
        public void AboutChangeChangesTheKey()
        {
            var before = BaseKey();
            var after = AdPreviewKey.Compute(MakeSpot(), MakeSponsor(about: "Family-owned since 1988"), MakeLive());
            Assert.NotEqual(before, after);
        }

        [Fact]
        public void PhoneChangeChangesTheKey()
        {
            var before = BaseKey();
            var after = AdPreviewKey.Compute(MakeSpot(), MakeSponsor(phone: "555-0199"), MakeLive());
            Assert.NotEqual(before, after);
        }

        [Fact]
        public void AddressChangeChangesTheKey()
        {
            var before = BaseKey();
            var after = AdPreviewKey.Compute(MakeSpot(), MakeSponsor(address: "2 Foundry Row"), MakeLive());
            Assert.NotEqual(before, after);
        }

        [Fact]
        public void WebsiteChangeChangesTheKey()
        {
            var before = BaseKey();
            var after = AdPreviewKey.Compute(MakeSpot(), MakeSponsor(website: "https://acme.example/sale"), MakeLive());
            Assert.NotEqual(before, after);
        }

        [Fact]
        public void ToneChangeChangesTheKey()
        {
            var before = BaseKey();
            var after = AdPreviewKey.Compute(MakeSpot(), MakeSponsor(tone: "brash"), MakeLive());
            Assert.NotEqual(before, after);
        }
    }

    // ---------------------------------------------------------------------
    // Live render knobs
    // ---------------------------------------------------------------------

    public sealed class ScenarioLiveKnobsChangeTheKey
    {
        [Fact]
        public void AnnouncerVoiceChangeChangesTheKey()
        {
            var before = BaseKey();
            var after = AdPreviewKey.Compute(MakeSpot(), MakeSponsor(), MakeLive(announcerVoice: "am_liam"));
            Assert.NotEqual(before, after);
        }

        [Fact]
        public void CastVoicesChangeChangesTheKey()
        {
            var before = BaseKey();
            var after = AdPreviewKey.Compute(MakeSpot(), MakeSponsor(), MakeLive(castVoices: ["voice_c"]));
            Assert.NotEqual(before, after);
        }

        [Fact]
        public void BedFadeMsChangeChangesTheKey()
        {
            var before = BaseKey();
            var after = AdPreviewKey.Compute(MakeSpot(), MakeSponsor(), MakeLive(bedFadeMs: 800));
            Assert.NotEqual(before, after);
        }

        [Fact]
        public void BedDuckDbChangeChangesTheKey()
        {
            var before = BaseKey();
            // gh-#746 — the duck rides on the live settings now (Station:Ads:BedDuckDb), still a key input.
            var after = AdPreviewKey.Compute(MakeSpot(), MakeSponsor(), MakeLive(bedDuckDb: -6.0));
            Assert.NotEqual(before, after);
        }

        [Fact]
        public void TargetLufsChangeChangesTheKey()
        {
            var before = BaseKey();
            // SPEC F196.1; PLAN T542 — the station's Loudness:TargetLufs is the bed's reference level
            // whenever the voice itself is unmeasurable, so a changed target changes the render too.
            var after = AdPreviewKey.Compute(MakeSpot(), MakeSponsor(), MakeLive(targetLufs: -20.0));
            Assert.NotEqual(before, after);
        }
    }

    // ---------------------------------------------------------------------
    // Shape, determinism, and null/empty collapsing
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheKeysOwnShape
    {
        [Fact]
        public void TheKeyIsLowercaseSha256Hex()
            => Assert.Matches(new Regex("^[0-9a-f]{64}$"), BaseKey());

        [Fact]
        public void TheKeyIsDeterministic()
        {
            var first = BaseKey();
            var second = BaseKey();
            Assert.Equal(first, second);
        }

        /// <summary>
        /// AdDeterministicSeed.Canonical (the shared newline-join <see cref="AdPreviewKey.Compute"/>
        /// hashes) does NOT itself coalesce null to empty — it is a bare
        /// <c>string.Join('\n', terms)</c> over whatever strings it is handed. The coalescing lives at
        /// EACH of <see cref="AdPreviewKey.Compute"/>'s own call sites instead (e.g.
        /// <c>sponsor.Tagline ?? ""</c>) — this fact pins THAT: a null nullable field and an
        /// explicit empty-string one produce the SAME key, because both reach <c>Canonical</c> as
        /// exactly <c>""</c>.
        /// </summary>
        [Fact]
        public void NullAndEmptyFieldsCollapseToTheSameKey()
        {
            var withNullTagline = AdPreviewKey.Compute(MakeSpot(), MakeSponsor(tagline: null), MakeLive());
            var withEmptyTagline = AdPreviewKey.Compute(MakeSpot(), MakeSponsor(tagline: ""), MakeLive());
            Assert.Equal(withNullTagline, withEmptyTagline);
        }
    }
}
