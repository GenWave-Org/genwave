namespace GenWave.Core.Domain;

/// <summary>
/// A recall query against a persona's memory (SPEC F71.4; STORY-194) —
/// <see cref="Abstractions.IPersonaMemory.RecallAsync"/>'s one parameter. The same shape serves both
/// windows the spec describes:
///
/// <list type="bullet">
/// <item>
/// <b>Anti-repeat</b> ("recently done, do differently") — <paramref name="NotAiredWithin"/> and
/// <paramref name="CreatedWithin"/> both left <see langword="null"/>: the most recently AIRED
/// <paramref name="Take"/> rows of <paramref name="Kind"/> come back. A row that has never aired has
/// nothing to avoid repeating, so it never qualifies for this leg.
/// </item>
/// <item>
/// <b>Callback</b> ("earlier you mentioned X") — either window set: a row qualifies when it was NOT
/// aired within <paramref name="NotAiredWithin"/> (a never-aired row always qualifies on this leg) AND
/// it was created within <paramref name="CreatedWithin"/> (omit to mean "any age").
/// </item>
/// </list>
/// </summary>
/// <param name="Kind">The <c>persona_memory.kind</c> to recall (e.g. <c>"bit"</c>, <c>"callback"</c>).</param>
/// <param name="Take">Maximum number of rows to return.</param>
/// <param name="NotAiredWithin">
/// Callback gate: a row aired more recently than this window ago is not offered. <see langword="null"/>
/// disables this gate.
/// </param>
/// <param name="CreatedWithin">
/// Callback gate: a row created longer ago than this window is not offered. <see langword="null"/>
/// disables this gate (any age qualifies).
/// </param>
public sealed record RecallSpec(string Kind, int Take, TimeSpan? NotAiredWithin = null, TimeSpan? CreatedWithin = null);
