namespace GenWave.Core.Domain;

/// <summary>
/// The projection <c>IRequestStore.GetForParseAsync</c> returns for a row still awaiting a wish
/// parse (SPEC F87.4, STORY-225, PLAN T88) — exactly what a parser needs and nothing more.
/// <see cref="Wish"/> is the listener's raw text; the same "stored only briefly, never voiced,
/// quoted, or logged" discipline <c>IRequestStore.InsertAsync</c>'s own remarks describe applies to
/// every consumer of this type just as strictly.
/// </summary>
/// <param name="Id">The row's identity — the same value the intake controller's insert produced.</param>
/// <param name="Wish">
/// The listener's raw wish text, not yet swept by the insert-time retention job.
/// <see langword="null"/> for a picker-only request (gh-#131) — the listener chose only from the
/// published genre/mood dropdowns and typed nothing, so there is nothing for a parser (LLM or
/// deterministic) to interpret; <see cref="PickedGenre"/>/<see cref="PickedMood"/> carry the whole
/// predicate set for such a row.
/// </param>
/// <param name="PickedGenre">
/// The dropdown genre the intake endpoint validated against the live requestable-genre list
/// (gh-#131) — already canonical catalog casing, merged into the parse outcome as-is, never
/// re-validated here. <see langword="null"/> when the listener picked none.
/// </param>
/// <param name="PickedMood">
/// The dropdown mood the intake endpoint validated against <c>MoodVocabulary.Terms</c> (gh-#131) —
/// already a vocabulary member, merged into the parse outcome as-is. <see langword="null"/> when the
/// listener picked none.
/// </param>
/// <param name="ExpiresAt">
/// The row's fulfillment window end (UTC), unrelated to parsing itself but carried through since a
/// parser has no other reason to re-query the row. Plain <see cref="DateTime"/>, not
/// <see cref="DateTimeOffset"/> — mirrors <c>BoothLogEntry.OccurredAt</c>'s own established
/// "Postgres timestamptz reads back as DateTime" shape for a read-side row projection.
/// </param>
public sealed record UnparsedRequest(
    long Id, string? Wish, string? PickedGenre, string? PickedMood, DateTime ExpiresAt);
