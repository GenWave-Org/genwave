namespace GenWave.TalkRules.Tests.Fixtures;

/// <summary>A random source whose draw always passes the callback planner's chance gate.</summary>
internal sealed class AlwaysDraws : Random
{
    public override double NextDouble() => 0.0;
}
