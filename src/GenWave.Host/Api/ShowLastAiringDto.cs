namespace GenWave.Host.Api;

/// <summary>
/// Wire shape for <c>GET /api/shows/{id}/last-airing</c> (SPEC F152.5, STORY-373, PLAN T362) —
/// ALWAYS 200 with this DTO, never a bare JSON <c>null</c> body and never 204 (T362 review MED-3,
/// binding: the earlier draft answered <c>Ok(null)</c> for "never aired," which ASP.NET Core's
/// <c>HttpNoContentOutputFormatter</c> silently rewrites to a real 204 — a response with NO body at
/// all, not a JSON <c>null</c> literal — so the browser's own <c>response.json()</c> then throws on
/// the empty body, and only the client's blanket <c>catch</c> masked it). <see cref="AiredCount"/>/
/// <see cref="Relaxed"/> both <see langword="null"/> together means "no last airing yet" — the ONE
/// signal <see cref="Core.Abstractions.IBoothLogReader.GetLastAiringAsync"/> itself already returns
/// (a <see langword="null"/> <see cref="Core.Domain.ShowLastAiring"/>) — never a fabricated zero.
/// </summary>
/// <param name="AiredCount">
/// The last airing's own track count — <c>airedCount</c> on the wire (T362 review LOW-6: renamed
/// from an earlier <c>picks</c>-serialized draft that needed a <see cref="System.Text.Json.Serialization.JsonPropertyNameAttribute"/>
/// workaround to dodge Story221's own <c>OnlyTheBoothLogDtoFamilyExposesPickOrFiredRuleProperties</c>
/// reflection guard — renaming the WIRE field too removes the need for that workaround entirely,
/// rather than merely hiding it from reflection while still calling it "picks" to callers).
/// </param>
public sealed record ShowLastAiringDto(int? AiredCount, int? Relaxed);
