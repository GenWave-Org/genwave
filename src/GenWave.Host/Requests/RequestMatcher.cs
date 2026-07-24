using GenWave.Core.Abstractions;

namespace GenWave.Host.Requests;

/// <summary>
/// The catalog-probe decision tree (SPEC F87.5, STORY-226, PLAN T89), folded into the SAME
/// background pipeline <see cref="RequestParserService"/> already runs rather than a second hosted
/// service — matching needs the just-parsed predicates <see cref="RequestParserService.ParseOneAsync"/>
/// already holds, and a separate service would need its own feed/discriminator into
/// <c>station.request</c> for zero benefit.
///
/// <para>
/// Called ONLY when the parse produced a non-empty predicate set (an empty parse is already
/// <c>unmatched</c> — <see cref="ParsedWish.IsEmpty"/> — before this class is ever reached):
/// <list type="bullet">
/// <item>artist and/or title present ⇒ <see cref="IRequestCatalogProbe.FindBestAsync"/>;
/// a hit stamps <c>matched_media_id</c> (<see cref="IRequestStore.MarkMatchedAsync"/>) — <c>status</c>
/// stays <c>pending</c>, a match is not a fulfillment (SPEC F87.6, PLAN T90);</item>
/// <item>a miss with a non-empty mood predicate stays <c>pending</c> as a vibe request — nothing more
/// to write, the moods are already stored by <see cref="RequestParserService"/>'s own
/// <see cref="IRequestStore.MarkParsedAsync"/> call, resolved later at pick time via the existing
/// mood-filter machinery (SPEC F86.8);</item>
/// <item>a miss with no mood predicate either ⇒ <see cref="IRequestStore.MarkUnmatchedAsync"/> —
/// nothing left to try.</item>
/// </list>
/// No artist/title given at all is impossible here by construction: the caller only reaches this
/// class for a non-empty parse, and a parse with neither artist nor title present has no predicate
/// left but moods, which is already the "stays pending as a vibe" case above with no probe needed.
/// </para>
///
/// <para>
/// Deliberately carries no <see cref="Microsoft.Extensions.Logging.ILogger"/> at all (SPEC F87.5's
/// "silently") — every branch of this decision tree is silent by construction, not merely by
/// discipline, so no future edit can accidentally log a wish, a predicate, or even a chatty outcome
/// line for a code path the spec calls out by name as silent.
/// </para>
/// </summary>
sealed class RequestMatcher(IRequestCatalogProbe catalogProbe, IRequestStore store)
{
    public async Task MatchAsync(long id, string? artist, string? title, IReadOnlyList<string> moods, CancellationToken ct)
    {
        if (artist is null && title is null)
            return; // moods-only predicate — already a vibe request, nothing more to write.

        var mediaId = await catalogProbe.FindBestAsync(artist, title, ct);
        if (mediaId is not null)
        {
            await store.MarkMatchedAsync(id, mediaId.Value, ct);
            return;
        }

        if (moods.Count == 0)
            await store.MarkUnmatchedAsync(id, ct);
        // else: a mood predicate survives the miss — stays pending as a vibe request (SPEC F87.5).
    }
}
