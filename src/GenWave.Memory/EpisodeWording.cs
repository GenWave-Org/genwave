using System.Globalization;
using GenWave.Core.Domain;

namespace GenWave.Memory;

/// <summary>The words an accrued episode is remembered in (SPEC F218.15); pure, so the rules are pinned by one fake event stream.</summary>
static class EpisodeWording
{
    /// <summary>The show a persona hosted: which show, which day and time, how many tracks, the first and the last.</summary>
    public static string HostedShow(ShowRun run)
    {
        var when = run.StartedAt.ToString("dddd HH:mm", CultureInfo.InvariantCulture);
        var tracks = run.TrackCount == 1 ? "1 track" : $"{run.TrackCount} tracks";
        var ends = run.FirstTrack is { } first && run.LastTrack is { } last
            ? $", opening with {first} and closing with {last}"
            : string.Empty;
        return $"hosted {run.ShowName} on {when}: {tracks}{ends}";
    }

    /// <summary>A fact the persona read on air, labelled by the provider that produced it.</summary>
    public static string ReadFact(FactReadOnAir fact) => $"read the {fact.Key}: {fact.Text}";

    /// <summary>One side of a crosstalk, naming the other persona.</summary>
    public static string TalkedWith(CrosstalkParticipant other) => $"talked on air with {other.Name}";

    /// <summary>The shared happening both episodes point at.</summary>
    public static string CrosstalkSummary(CrosstalkParticipant host, CrosstalkParticipant neighbor) =>
        $"{host.Name} and {neighbor.Name} talked on air";

    /// <summary>A track as it is remembered: title, and artist when known.</summary>
    public static string? Track(string? title, string? artist) => (title, artist) switch
    {
        (null or "", _) => null,
        (_, null or "") => $"\"{title}\"",
        _ => $"\"{title}\" by {artist}",
    };
}
