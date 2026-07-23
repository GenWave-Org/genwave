namespace GenWave.Core.Domain;

/// <summary>
/// The projection <c>IRequestStore.GetForParseAsync</c> returns for a row still awaiting a wish
/// parse (SPEC F87.4, STORY-225, PLAN T88) — exactly what a parser needs and nothing more.
/// <see cref="Wish"/> is the listener's raw text; the same "stored only briefly, never voiced,
/// quoted, or logged" discipline <c>IRequestStore.InsertAsync</c>'s own remarks describe applies to
/// every consumer of this type just as strictly.
/// </summary>
/// <param name="Id">The row's identity — the same value the intake controller's insert produced.</param>
/// <param name="Wish">The listener's raw wish text, not yet swept by the insert-time retention job.</param>
/// <param name="ExpiresAt">
/// The row's fulfillment window end (UTC), unrelated to parsing itself but carried through since a
/// parser has no other reason to re-query the row. Plain <see cref="DateTime"/>, not
/// <see cref="DateTimeOffset"/> — mirrors <c>BoothLogEntry.OccurredAt</c>'s own established
/// "Postgres timestamptz reads back as DateTime" shape for a read-side row projection.
/// </param>
public sealed record UnparsedRequest(long Id, string Wish, DateTime ExpiresAt);
