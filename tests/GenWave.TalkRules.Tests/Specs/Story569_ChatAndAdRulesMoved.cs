// STORY-569 — Chat and ad rules moved (SPEC F220 · PLAN F220)
//
// BDD specification — xUnit. Pending: each fact is activated by the task its Skip names.

namespace GenWave.TalkRules.Tests.Specs;

public static class FeatureChatAndAdRulesMoved
{
    // AC1 — a chat line with a frequency, a call sign or a date claim
    public sealed class ScenarioStationDetails
    {
        [Fact(Skip = "pending: T783")]
        public void AFrequencyIsFlagged() { }

        [Fact(Skip = "pending: T783")]
        public void ACallSignIsFlagged() { }

        [Fact(Skip = "pending: T783")]
        public void ADateClaimIsFlagged() { }
    }

    // AC2 — a chat line sharing 5 words with that speaker's last 10 chat lines
    public sealed class ScenarioChatRepeats
    {
        [Fact(Skip = "pending: T784")]
        public void ItIsFlagged() { }
    }

    // AC3 — talk longer than its time
    public sealed class ScenarioFitsTheTime
    {
        [Fact(Skip = "pending: T786")]
        public void ALongChatIsFlagged() { }

        [Fact(Skip = "pending: T786")]
        public void ALongAdIsFlagged() { }
    }

    // AC4 — an ad naming a blocklisted brand
    public sealed class ScenarioOtherBrands
    {
        [Fact(Skip = "pending: T786")]
        public void ItIsFlagged() { }
    }

    // AC5 — audience Everyone and an ad with a listed word
    public sealed class ScenarioCleanLanguage
    {
        [Fact(Skip = "pending: T786")]
        public void ItIsFlagged() { }
    }

    // AC6 — a 7-digit number that is not the sponsor's and has no 555
    public sealed class ScenarioPhoneNumber
    {
        [Fact(Skip = "pending: T786")]
        public void ItIsFlagged() { }

        [Fact(Skip = "pending: T786")]
        public void TheAskNamesTheSponsorsNumber() { }
    }

    // Sad path
    // AC7 — the sponsor's own name is on the brand list, not pack-owned
    public sealed class ScenarioTheSponsorsOwnName
    {
        [Fact(Skip = "pending: T786")]
        public void ItIsNotFlagged() { }
    }

    // Sad path
    // AC8 — the ad says the sponsor's phone
    public sealed class ScenarioTheSponsorsNumber
    {
        [Fact(Skip = "pending: T786")]
        public void ItIsNotFlagged() { }
    }

    // Sad path
    // AC9 — a wrong number in the reply
    public sealed class ScenarioAWrongNumberIsNotRewritten
    {
        [Fact(Skip = "pending: T786")]
        public void TheCleanedTextStillHoldsThatNumber() { }
    }
}
