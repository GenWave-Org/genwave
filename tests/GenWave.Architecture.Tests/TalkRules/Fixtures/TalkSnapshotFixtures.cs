using GenWave.Core.TalkRules;

namespace GenWave.Architecture.Tests.TalkRules.Fixtures;

/// <summary>Fixture snapshots for laws R3/R4: DJ, chat and ad talk, some that the good fixture rules flag and some that they pass.</summary>
public static class TalkSnapshotFixtures
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 9, 0, 0, TimeSpan.FromHours(-6));

    private static readonly string LongLine = string.Join(' ', Enumerable.Repeat("word", 45));

    public static IReadOnlyList<TalkSnapshot> All { get; } =
    [
        Build(TalkKinds.DjLine, "Nice and easy on a Sunday morning."),
        Build(TalkKinds.DjLine, "WHAT A SONG THAT WAS FOLKS, back after this."),
        Build(TalkKinds.Chat, "Morning, did you catch that one?"),
        Build(TalkKinds.Chat, LongLine),
        Build(TalkKinds.Chat, "THIS IS THE BEST SONG EVER MADE, " + LongLine),
        Build(TalkKinds.Ad, "Fresh bread at the corner bakery."),
        Build(TalkKinds.Ad, LongLine),
    ];

    private static TalkSnapshot Build(TalkKinds kind, string text) => new(
        kind,
        kind == TalkKinds.Ad ? "ad" : "talk",
        1,
        new TalkList<TalkLine>([new TalkLine("HOST", text)]),
        new TalkList<TalkSpeaker>([new TalkSpeaker(1, "Sal", new TalkList<string>(["Stay easy."]))]),
        null,
        null,
        Now,
        "America/Edmonton",
        null,
        new TalkStation("GenWave", "Friends at home."),
        TalkList<TalkRecentLine>.Empty,
        kind == TalkKinds.Ad ? new TalkAdFields("Corner Bakery", null, 20, false, 0.4, 220, 15.0, TalkList<double>.Empty) : null,
        TalkMemos.Empty);
}
