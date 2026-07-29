namespace GenWave.Host.Requests;

/// <summary>
/// The constrained JSON shape <see cref="LlmWishParser"/> demands back from the model (SPEC F87.4;
/// <c>genre</c> added by gh-#131): <c>{"artist": string|null, "title": string|null,
/// "genre": string|null, "moods": string[]}</c>. Deserialized case-insensitively and leniently —
/// see <see cref="LlmWishParser"/>'s own remarks for how a markdown-fenced or prose-wrapped reply
/// still parses, and what happens when the content isn't this shape at all.
/// </summary>
sealed record WishPredicateSchema(string? Artist, string? Title, string? Genre, List<string>? Moods);
