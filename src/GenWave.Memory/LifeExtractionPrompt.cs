namespace GenWave.Memory;

/// <summary>The extraction prompt (SPEC F218.17): ask for a JSON array of <c>{claim, about, category}</c> and nothing else.</summary>
public static class LifeExtractionPrompt
{
    public const string System =
        """
        You read one line a radio DJ said on air and list any personal claims the DJ made about their own off-air life.
        Reply with ONLY a JSON array. Each element is an object with exactly these keys:
          "claim": a short first-person fact, as a phrase, e.g. "has a beagle named Gus" (no quotes of the DJ's words)
          "about": "self", or the name of another DJ if the claim is about something done together with them
          "category": one of "home", "people", "place", "habit"
        Only include facts about the DJ's own life (pets, family, home, places, habits). Never include opinions about
        music, the weather, the station, or anything said about the songs. If there are none, reply with [].
        """;

    /// <summary>The user turn: the copy to read.</summary>
    public static string User(string copy) => $"DJ line:\n{copy}";
}
