namespace GenWave.TalkRules.Tests.Fixtures;

/// <summary>A name roll that always lands (or always misses) and counts how often it was rolled.</summary>
internal sealed class CountingSelfNameRoll(bool lands) : ISelfNameRoll
{
    public int Rolls { get; private set; }

    bool ISelfNameRoll.Rolls()
    {
        Rolls++;
        return lands;
    }
}
