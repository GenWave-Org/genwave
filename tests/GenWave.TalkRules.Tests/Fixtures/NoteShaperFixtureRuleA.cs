namespace GenWave.TalkRules.Tests.Fixtures;

public sealed class NoteShaperFixtureRuleA : NoteShaperFixtureRule
{
    public override string Id => "fixture-note-a";

    protected override string Note => "Keep it short.";
}
