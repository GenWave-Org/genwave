// STORY-564 — A rule is one file that describes itself (SPEC F220 · PLAN F220)
//
// BDD specification — xUnit. Law R6 and R5 over the shipped rules plus the good fixture rules, each proven red against a bad fixture.

using GenWave.Architecture.Tests.TalkRules;
using GenWave.Architecture.Tests.TalkRules.Construction;
using GenWave.Architecture.Tests.TalkRules.Fixtures;
using GenWave.Architecture.Tests.TalkRules.Fixtures.Bad;
using GenWave.Architecture.Tests.TalkRules.Fixtures.Good;
using GenWave.Architecture.Tests.TalkRules.Laws;
using GenWave.Core.TalkRules;
using GenWave.TalkRules;
using Microsoft.Extensions.DependencyInjection;

namespace GenWave.Architecture.Tests.Specs;

public static class FeatureDescribesItselfLaw
{
    // AC1, AC3 — law R6 over every shipped rule
    public sealed class ScenarioEveryShippedRule
    {
        private readonly IReadOnlyList<ITalkRule> rules = TalkRuleCatalog.Build(TalkRuleCatalog.CheckedTypes);

        public ScenarioEveryShippedRule() => Assert.NotEmpty(rules);

        [Fact]
        public void HasAUniqueKebabId() => Assert.Empty(RuleDescribesItselfLaw.IdViolations(rules));

        [Fact]
        public void NameStartsWithACapitalAndHasNoFullStop() => Assert.Empty(RuleDescribesItselfLaw.NameViolations(rules));

        [Fact]
        public void WhatItDoesStartsWithACapitalAndEndsWithAFullStop() => Assert.Empty(RuleDescribesItselfLaw.WhatItDoesViolations(rules));

        [Fact]
        public void WhatItDoesStatesItsOwnNumbers() => Assert.Empty(RuleDescribesItselfLaw.NumberViolations(rules));

        [Fact]
        public void DefaultCoverIsWithinWhatItCanCover() => Assert.Empty(RuleDescribesItselfLaw.DefaultCoverViolations(rules));

        [Fact]
        public void ImplementsAShapeOrJudgePart() => Assert.Empty(RuleDescribesItselfLaw.PartViolations(rules));

        [Fact]
        public void ARuleWithACollaboratorIsBuiltWithNoCatalogEdit() => Assert.Contains(rules, rule => rule is HistoryAwareShaper);
    }

    // AC2 — law R5, a rule declared in the test assembly
    public sealed class ScenarioARuleDeclaredInOneFile
    {
        private readonly IReadOnlyList<ITalkRule> listed;

        public ScenarioARuleDeclaredInOneFile()
        {
            using var provider = new ServiceCollection()
                .AddLogging()
                .AddSingleton(typeof(ITalkLineStore), NullObjectProxy.For(typeof(ITalkLineStore))) // the host registers a rule's collaborators
                .AddTalkRules(typeof(ShoutingJudge).Assembly)
                .BuildServiceProvider();
            listed = provider.GetRequiredService<TalkRuleBook>().Rules;
        }

        [Fact]
        public void IsListedByTheRuleBookWithNoOtherEdit() =>
            Assert.Contains(listed, rule => rule is ShoutingJudge);
    }

    // Sad path
    // AC4 — a rule that neither shapes nor judges
    public sealed class ScenarioARuleWithNeitherPart
    {
        private readonly IReadOnlyList<string> violations =
            RuleDescribesItselfLaw.Violations([new NeitherPartRule()]);

        [Fact]
        public void TheLawFailsNamingTheRule() =>
            Assert.Contains(violations, violation => violation.Contains(nameof(NeitherPartRule), StringComparison.Ordinal));
    }

    // Sad path
    // AC5 — two rules with one id
    public sealed class ScenarioTwoRulesShareAnId
    {
        private readonly IReadOnlyList<string> violations =
            RuleDescribesItselfLaw.Violations([new DuplicateIdFirst(), new DuplicateIdSecond()]);

        [Fact]
        public void TheLawFailsNamingBoth() =>
            Assert.Contains(violations, violation =>
                violation.Contains(nameof(DuplicateIdFirst), StringComparison.Ordinal)
                && violation.Contains(nameof(DuplicateIdSecond), StringComparison.Ordinal));
    }

    // Sad path
    // AC1 — a rule whose text leaves out its own number
    public sealed class ScenarioARuleHidesItsNumber
    {
        private readonly IReadOnlyList<string> violations =
            RuleDescribesItselfLaw.NumberViolations([new HiddenNumberJudge()]);

        [Fact]
        public void TheLawFailsNamingTheRuleAndTheNumber() =>
            Assert.Contains(violations, violation =>
                violation.Contains(nameof(HiddenNumberJudge), StringComparison.Ordinal)
                && violation.Contains("55", StringComparison.Ordinal));
    }

    // Sad path
    // AC1 — a rule whose only mention of its number is inside a decimal
    public sealed class ScenarioARuleHidesItsNumberInADecimal
    {
        private readonly IReadOnlyList<string> violations =
            RuleDescribesItselfLaw.NumberViolations([new DecimalHidingJudge()]);

        [Fact]
        public void TheLawFailsNamingTheRule() =>
            Assert.Contains(violations, violation => violation.Contains(nameof(DecimalHidingJudge), StringComparison.Ordinal));
    }

    // AC1 — a rule whose number ends its sentence
    public sealed class ScenarioARuleEndsItsSentenceOnItsNumber
    {
        [Fact]
        public void TheLawCountsTheNumberAsStated() =>
            Assert.Empty(RuleDescribesItselfLaw.NumberViolations([new SentenceEndingNumberJudge()]));
    }

    // Sad path
    // AC1, AC3 — a rule that describes itself sloppily
    public sealed class ScenarioARuleDescribesItselfSloppily
    {
        private readonly IReadOnlyList<ITalkRule> rules = [new SloppyDescriptionJudge()];

        [Fact]
        public void ANonKebabIdFailsTheLaw() => Assert.NotEmpty(RuleDescribesItselfLaw.IdViolations(rules));

        [Fact]
        public void ALowerCaseNameFailsTheLaw() => Assert.NotEmpty(RuleDescribesItselfLaw.NameViolations(rules));

        [Fact]
        public void ANameWithAFullStopFailsTheLaw() => Assert.NotEmpty(RuleDescribesItselfLaw.NameViolations([new FullStopNameJudge()]));

        [Fact]
        public void WhatItDoesWithoutCapitalOrFullStopFailsTheLaw() => Assert.NotEmpty(RuleDescribesItselfLaw.WhatItDoesViolations(rules));

        [Fact]
        public void ADefaultOutsideWhatItCanCoverFailsTheLaw() => Assert.NotEmpty(RuleDescribesItselfLaw.DefaultCoverViolations(rules));
    }
}
