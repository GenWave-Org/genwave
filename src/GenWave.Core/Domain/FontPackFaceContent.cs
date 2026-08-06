namespace GenWave.Core.Domain;

/// <summary>
/// A <c>station.font_pack_face</c> row's serving payload — bytes plus just enough content metadata
/// to answer an HTTP request, nothing this hot path has no use for (SPEC F104, STORY-282, PLAN
/// T198). <see cref="Abstractions.IFontPackStore.GetFaceByFileAsync"/> returns this for the widened
/// <c>/fonts/{file}</c> route (T200) once a request falls through the vendored literal switch —
/// every installed face serves the SAME <c>font/woff2</c> content type regardless of
/// <see cref="FontPackFace.Style"/>, so this type carries neither <c>style</c> nor <c>pack_id</c>,
/// unlike the library-listing shape <see cref="FontPackFace"/> returns.
/// </summary>
/// <param name="Bytes">The face's raw payload, streamed straight to the response body.</param>
/// <param name="Sha256">The payload's pinned hash (see <see cref="FontPackFaceInput.Sha256"/>'s own
/// remarks) — available to a caller wanting an ETag without rehashing <paramref name="Bytes"/> on
/// every request.</param>
public sealed record FontPackFaceContent(byte[] Bytes, string Sha256);
