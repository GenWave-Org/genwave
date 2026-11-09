namespace GenWave.PatterBench;

using GenWave.Core.Domain;

/// <summary>The fixed inputs every bench break is built from, so a run is repeatable (SPEC F218.27).</summary>
internal static class BenchFixtures
{
    internal const string StationName = "GenWave Radio";

    /// <summary>The station-local time every prompt carries: a Friday afternoon.</summary>
    internal static readonly DateTimeOffset LocalNow = new(2026, 11, 6, 16, 20, 0, TimeSpan.FromHours(-5));

    /// <summary>Seeds the self-name roll and the quirk pick, so the same run builds the same prompts.</summary>
    internal const int Seed = 20261106;

    internal static readonly IReadOnlyList<MediaItem> Tracks =
    [
        Track("Blinding Lights", "The Weeknd"),
        Track("Take On Me", "a-ha"),
        Track("Dreams", "Fleetwood Mac"),
        Track("Mr. Brightside", "The Killers"),
        Track("September", "Earth, Wind & Fire"),
        Track("Heroes", "David Bowie"),
        Track("Come Together", "The Beatles"),
        Track("Juice", "Lizzo"),
    ];

    /// <summary>Break <paramref name="index"/> introduces track index mod count, so the prompts differ the way real breaks do.</summary>
    internal static SegmentRequest RequestFor(int index, string personaName) =>
        new(
            SegmentKind.LeadIn,
            Voice: "bench",
            StationName,
            Tracks[index % Tracks.Count],
            LocalNow,
            StationId: "bench",
            PersonaName: personaName);

    private static MediaItem Track(string title, string artist) =>
        new(title, $"/bench/{title}.mp3", title, new Loudness(-16.0, -1.0, true), artist);
}
