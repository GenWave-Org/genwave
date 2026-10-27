namespace GenWave.VoiceOff.Deck;

/// <summary>The file spelling of each <see cref="CardHost"/>, as written in a card and in <c>run.json</c>.</summary>
internal static class CardHostName
{
    internal const string ThorGpu = "thor-gpu";
    internal const string ThorCpu = "thor-cpu";
    internal const string RunesGpu = "runes-gpu";

    internal static string Of(CardHost host) => host switch
    {
        CardHost.ThorGpu => ThorGpu,
        CardHost.ThorCpu => ThorCpu,
        CardHost.RunesGpu => RunesGpu,
        _ => throw new ArgumentOutOfRangeException(nameof(host), host, "Unknown card host."),
    };
}
