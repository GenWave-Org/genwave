// STORY-527 — The card page (gh-#614 · SPEC F217 · PLAN T665)
//
// BDD specification — xUnit. Active since T665. Drag, play and the debounce are proven by the task's Playwright pass; these pin what the served HTML holds.

using System.Net;
using System.Text.RegularExpressions;
using GenWave.VoiceOff.Tests.Support;

namespace GenWave.VoiceOff.Tests.Specs;

public static class FeatureTheCardPage
{
    private static bool Has(string html, string pattern) => Regex.IsMatch(html, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — the page is served
    public sealed class ScenarioThePageIsServed : IDisposable
    {
        // Given a run
        // When  GET /
        private readonly ServeScenario serve = new();
        private readonly HttpResponseMessage answer;
        private readonly string html;

        public ScenarioThePageIsServed()
        {
            answer = serve.Get("/");
            html = answer.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }

        [Fact]
        public void TheReplyIs200TextHtml() => Assert.Equal((HttpStatusCode.OK, "text/html"), (answer.StatusCode, answer.Content.Headers.ContentType?.MediaType));

        [Fact]
        public void ThePageContainsACardTemplate() => Assert.True(Has(html, "<template id=\"card-template\">"));

        [Fact]
        public void ThePageContainsAPlayerWithAProgressBar() => Assert.True(Has(html, "<audio[^>]*data-role=\"audio\"") && Has(html, "type=\"range\"[^>]*data-role=\"seek\""));

        [Fact]
        public void ThePageContainsADragHandle() => Assert.True(Has(html, "data-role=\"handle\"[^>]*draggable=\"true\"[^>]*aria-label="));

        [Fact]
        public void ThePageContainsAWinceCounter() => Assert.True(Has(html, "data-role=\"wince-down\"") && Has(html, "data-role=\"wince-count\"") && Has(html, "data-role=\"wince-up\""));

        [Fact]
        public void ThePageContainsFiveStars() => Assert.True(Has(html, "data-role=\"stars\"") && Has(html, "MAX_STARS = 5"));

        [Fact]
        public void ThePageContainsANotesField() => Assert.True(Has(html, "<textarea[^>]*data-role=\"notes\"[^>]*maxlength=\"2000\""));

        [Fact]
        public void ThePageContainsADidNotRenderTray() => Assert.True(Has(html, "id=\"tray\"") && Has(html, "Did not render"));

        public void Dispose()
        {
            answer.Dispose();
            serve.Dispose();
        }
    }

    // AC2 — the page is told nothing it should not know
    public sealed class ScenarioThePageIsToldNothingItShouldNotKnow : IDisposable
    {
        // Given the served HTML
        // When  it is searched
        private readonly ServeScenario serve = new();
        private readonly string html;

        public ScenarioThePageIsToldNothingItShouldNotKnow() => html = serve.GetText("/");

        [Fact]
        public void NoEngineSlugFromEnginesAppears() =>
            Assert.DoesNotContain(
                Directory.GetFiles(RealEngines.Directory, "*.json").Select(file => Path.GetFileNameWithoutExtension(file)),
                slug => Has(html, @"\b" + Regex.Escape(slug) + @"\b"));

        [Fact]
        public void NoJudgeNameFieldAppears() => Assert.False(Has(html, "<input[^>]*type=\"text\"") || Has(html, "name=\"(judge|name)\"") || Has(html, "autocomplete=\"name\""));

        public void Dispose() => serve.Dispose();
    }

    // AC3 — the page saves whole
    public sealed class ScenarioThePageSavesWhole : IDisposable
    {
        // Given the page script
        // When  it is read
        private readonly ServeScenario serve = new();
        private readonly string html;

        public ScenarioThePageSavesWhole() => html = serve.GetText("/");

        [Fact]
        public void ItPUTsJudgementWithOrderCardsAndSavedAt() =>
            Assert.True(Has(html, "fetch\\(\"/judgement\", \\{\\s*method: \"PUT\"") && Has(html, "savedAt:[^\\n]*order:[^\\n]*cards:"));

        public void Dispose() => serve.Dispose();
    }

    // AC4 — the page restores on open
    public sealed class ScenarioThePageRestoresOnOpen : IDisposable
    {
        // Given the page script
        // When  it is read
        private readonly ServeScenario serve = new();
        private readonly string html;

        public ScenarioThePageRestoresOnOpen() => html = serve.GetText("/");

        // The only call of show(state) sits inside the /state fetch chain (before its .catch), so no card is drawn ahead of the saved judgement.
        [Fact]
        public void ItGETsStateBeforeRenderingCards() =>
            Assert.True(
                Has(html, @"fetch\(""/state""\)(?:(?!\.catch\()[\s\S])*?\.then\(\(state\)(?:(?!\.catch\()[\s\S])*?(?<!function )\bshow\(state\)")
                    && Regex.Matches(html, @"(?<!function )\bshow\(state\)").Count == 1,
                "show(state) must be called exactly once, inside the fetch(\"/state\").then chain.");

        public void Dispose() => serve.Dispose();
    }
}
