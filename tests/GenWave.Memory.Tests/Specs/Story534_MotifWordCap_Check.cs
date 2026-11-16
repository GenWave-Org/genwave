// STORY-534 — motif-word cap (PLAN T724, Dean 11-16): a content word already in three of the persona's lines this
// shift is a hit for a fourth. Big Tony's "ballroom" (16 times in one shift on 11-15) is the fixture.

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Memory.Tests.Specs;

public static class FeatureMotifWordCapCheck
{
    static readonly IRepetitionCheck Check = new RepetitionCheck();

    static readonly string[] Ballroom =
    [
        "Welcome to the ballroom, everybody.",
        "The ballroom is warm tonight.",
        "Grab a partner, this ballroom never sleeps.",
    ];

    static RepetitionResult Run(string copy, string[] shift, string? persona = "Big Tony", string? title = null, string? artist = null) =>
        Check.Check(copy, new RepetitionContext([], [], persona, "GenWave Radio", title, artist, ShiftTexts: shift));

    [Fact]
    public static void TheFourthLineUsingTheWordIsAMotifHit()
    {
        var hit = Assert.Single(Run("Back in the ballroom for one more.", Ballroom).Hits);

        Assert.Equal(new RepetitionHit("ballroom", RepetitionHitKind.Motif), hit);
    }

    [Fact]
    public static void TwoPriorLinesIsNotAHit() =>
        Assert.False(Run("Back in the ballroom for one more.", Ballroom[..2]).IsHit);

    [Fact]
    public static void AWordTwiceInOnePriorLineCountsOnce() =>
        Assert.False(Run("Back in the ballroom for one more.",
            ["Ballroom, ballroom, ballroom!", "The ballroom is warm.", "Nothing here."]).IsHit);

    [Fact]
    public static void NoShiftLinesMeansNoMotifCheck() =>
        Assert.False(Check.Check("Back in the ballroom for one more.", new RepetitionContext([], [])).IsHit);

    [Theory]
    [InlineData("Ballroom Bob", null, null, "Ballroom")]
    [InlineData("Dj", "Ballroom Blitz", null, "Ballroom")]
    [InlineData("Dj", null, "The Ballroom Kings", "Ballroom")]
    public static void APersonaTitleOrArtistWordIsNotAMotif(string persona, string? title, string? artist, string word)
    {
        var lines = Ballroom.Select(l => l.Replace("ballroom", word.ToLowerInvariant(), StringComparison.Ordinal)).ToArray();

        Assert.False(Run($"Back in the {word} for one more.", lines, persona, title, artist).IsHit);
    }

    [Fact]
    public static void TheStationNameWordIsNotAMotif()
    {
        var lines = new[] { "GenWave radio rocks.", "Radio all night.", "More radio now." };

        Assert.False(Run("This is radio at its best.", lines, persona: "Tony").IsHit);
    }

    [Fact]
    public static void AShortWordIsNotAMotif() =>
        Assert.False(Run("Another jazz, friends.", ["A jazz now.", "Jazz, jazz.", "The jazz floor."]).IsHit);

    [Fact]
    public static void AWordMustMatchWhole() =>
        Assert.False(Run("Out of the ballrooms we go.", Ballroom).IsHit);
}
