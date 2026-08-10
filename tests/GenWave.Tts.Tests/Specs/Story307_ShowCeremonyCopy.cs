// STORY-307 — Ceremony names the show (F116.2) — prompt-content half
//
// BDD specification — xUnit, PENDING scaffold (planned 2026-08-10). Comment-bodied on
// purpose: the show fields in ceremony prompts land at T248. Golden-string idiom follows
// Story243/Story303. The boundary/dedupe half lives in Orchestration.Tests.

namespace GenWave.Tts.Tests.Specs;

using Xunit;

public static class FeatureShowCeremonyCopy
{
    public sealed class ScenarioSignOnCarriesTheShow
    {
        [Fact(Skip = "Pending (T248)")]
        public void SignOnPromptCarriesIncomingShowNameAndFlavor()
        {
            // Given a boundary into a block with show "The Breakfast Show" (tagline + flavor set)
            // When  the sign-on prompt is built
            // Then  it carries the incoming show's name and flavor (flavor reaches the
            //       prompt ONLY — never any public payload; F115.3)
        }

        [Fact(Skip = "Pending (T248)")]
        public void SignOffMayNameTheEndingAndNextShows()
        {
            // Given a boundary between two named shows
            // When  the sign-off prompt is built
            // Then  both show names are available to the copywriter (F114.3's "may name")
        }
    }

    public sealed class ScenarioShowlessCeremonyUntouched
    {
        [Fact(Skip = "Pending (T248)")]
        public void ShowlessCeremonyPromptIsByteIdentical()
        {
            // Given a boundary between blocks with no shows
            // When  sign-on/sign-off prompts are built
            // Then  output matches the pre-F116 golden byte-for-byte
        }
    }
}
