namespace GenWave.Core.Domain;

/// <summary>
/// What an <see cref="Abstractions.IContextProvider"/> hands back on a successful fetch (SPEC
/// F107.1, amended by F125.2): the ordered, already-airable facts this fetch produced, plus the
/// caching horizon the pipeline must respect. Never prose-for-air on its own — each entry in
/// <see cref="Facts"/> is raw material a segment's or a patter line's copy is written FROM, not the
/// copy itself.
///
/// <para>
/// <b>The provider stops pre-choosing (F125.2, gh-#468).</b> An earlier shape of this record split
/// <c>SegmentFacts</c>/<c>PatterFact</c>: a provider decided, once per fetch, which fact(s) the
/// segment lane got and which single fact the patter lane got, so both stayed the SAME for the
/// entire <see cref="FreshUntil"/> window — the gh-#468 sighting was exactly this: a tornado fact
/// aired as chill-morning color, and the same patter fact vended all day. Selection now happens at
/// VEND time, in <c>GenWave.Context.ContextPipeline</c>, against a per-provider, day-scoped,
/// in-memory aired-set (SPEC F125.3/F125.4): the patter lane vends the first not-yet-aired fact and
/// skips once every fact has aired; the segment lane rotates a window through <see cref="Facts"/>,
/// wrapping once it runs past the end. A provider with only one fact to offer (e.g. current weather
/// conditions) legally returns a one-element list, and the two lanes then behave DIFFERENTLY —
/// deliberately: the segment lane's window degenerates cleanly to "always this one fact," every
/// single vend, the exact pre-F125 shape. The patter lane does NOT — its aired-set marks that one
/// index aired on its FIRST vend and holds it until the next <see cref="FreshUntil"/> roll, so a
/// one-element provider's patter fact airs exactly ONCE per content generation and every later
/// patter-cadence slot within that same generation is skipped. This is a genuine behavior change
/// from pre-F125 (a single-fact provider's patter line used to repeat every slot, forever) —
/// accepted, since patter is optional color (F125.3) and a provider on a sane refetch cadence
/// (weather's own hourly default) still earns a fresh patter vend on every new generation.
/// </para>
/// </summary>
/// <param name="Facts">
/// The ordered, airable facts this fetch produced — plain text, one fact per element, never a
/// newline within an element. <b>An empty list means "nothing to say this fetch"</b> (T221 review
/// carry-forward, preserved under F125.2): never an error and never logged as one — the pipeline
/// produces no segment and no patter fact for it and moves on. A non-empty list feeds BOTH lanes via
/// the pipeline's own vend-time selection; this record itself carries no "segment material" vs
/// "patter material" distinction any more (see this record's own remarks).
/// </param>
/// <param name="FreshUntil">
/// The pipeline caching horizon: this content may be reused for any segment/patter airing up to (but
/// not including) this instant, after which the provider must be fetched again before it airs. Also
/// the rotation horizon (F125.4): the pipeline's per-provider aired-set resets when THIS VALUE next
/// rolls forward to a new instant, not on every re-fetch — a provider whose cadence is narrower than
/// its own content's shelf life (e.g. a 4-hour fetch cadence against an all-day FreshUntil) re-fetches
/// the SAME generation, with the SAME FreshUntil, many times before it ever rolls over, and rotation
/// carries on across every one of those re-fetches so it can actually vary within the day.
/// </param>
public sealed record ContextContent(IReadOnlyList<string> Facts, DateTimeOffset FreshUntil);
