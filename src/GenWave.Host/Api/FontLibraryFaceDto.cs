namespace GenWave.Host.Api;

/// <summary>
/// One face inside a <see cref="FontLibraryPackDto"/>'s <see cref="FontLibraryPackDto.Faces"/> (SPEC
/// F104.7, STORY-284, PLAN T203) — the wire projection of
/// <c>GenWave.Core.Domain.FontPackFace</c>, metadata only. <see cref="Style"/> renders as plain text
/// only — see <see cref="FontLibraryPackDto"/>'s own "PLAIN TEXT ONLY" remarks.
/// </summary>
/// <param name="File">The <c>/fonts/&lt;file&gt;</c> basename this face serves at.</param>
/// <param name="Style">CSS font-style this face renders as — <c>"normal"</c> or <c>"italic"</c>.</param>
/// <param name="ByteSize">The stored payload's byte count, recorded at install time.</param>
public sealed record FontLibraryFaceDto(string File, string Style, int ByteSize);
