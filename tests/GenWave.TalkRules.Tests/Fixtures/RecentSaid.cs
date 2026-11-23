namespace GenWave.TalkRules.Tests.Fixtures;

/// <summary>One line a speaker said earlier, as the store holds it. <paramref name="Fallback"/> marks a stand-in template line.</summary>
internal sealed record RecentSaid(string Text, bool Fallback = false);
