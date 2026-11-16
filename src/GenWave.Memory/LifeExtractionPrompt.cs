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
        You may be given "Already known about this DJ" lines. List ONLY details that are new: never repeat or reword
        a known detail, and never state one that contradicts it (for example a different name or kind of animal for a
        pet that is already known). Each claim must be specific: no bare "has a pet" or "has a vehicle". Never list
        songs, tracks, playlists or what the DJ listened to or played on air. When nothing is new, reply with [].
        """;

    /// <summary>The user turn: what is already known about the DJ (newest first, may be empty), then the copy to read.</summary>
    public static string User(string copy, IReadOnlyList<string> known) =>
        known.Count == 0
            ? $"DJ line:\n{copy}"
            : $"Already known about this DJ:\n{string.Join('\n', known.Select(k => $"- {k}"))}\n\nDJ line:\n{copy}";
}
