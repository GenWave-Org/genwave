namespace GenWave.Memory;

/// <summary>
/// Words that are never distinctive for the callback word rule: <see cref="RepetitionCheck.StopWords"/> plus the
/// episode template words, weekday names and common patter words. Without them ordinary patter "used" about 7% of
/// lore and episode memories (T732 replay). The additions mirror the second <c>insert into stop</c> in
/// docs/replay/t732b-callback-stop-list-replay.sql, in the same order: keep the two in step.
/// </summary>
internal static class CallbackStopWords
{
    internal static readonly HashSet<string> All = Build();

    static HashSet<string> Build()
    {
        var words = new HashSet<string>(RepetitionCheck.StopWords, StringComparer.Ordinal);
        words.UnionWith(
        [
            // episode template words (EpisodeWording.cs)
            "hosted", "shift", "track", "tracks", "opening", "closing", "handed", "over", "took", "talked", "read",
            "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday",
            // common patter words, 4+ letters
            "through", "still", "just", "like", "here", "there", "about", "back", "more", "some", "what", "when", "then", "them", "than",
            "their", "these", "those", "where", "while", "very", "much", "only", "also", "even", "ever", "every", "onto", "upon", "yours",
            "alright", "well", "folks", "right", "time", "times", "keep", "keeps", "find", "take", "make", "made", "come", "comes", "going",
            "know", "tonight", "today", "morning", "mornings", "night", "nights", "week", "weekend", "live", "lives", "listen", "listener", "listeners",
            "song", "songs", "music", "next", "tune", "tunes", "rock", "rocking", "loving", "love", "house", "close", "long", "good", "great",
            "little", "first", "last", "year", "years", "away", "down", "again", "never", "always", "people", "thing", "things", "want", "been", "being",
        ]);
        return words;
    }
}
