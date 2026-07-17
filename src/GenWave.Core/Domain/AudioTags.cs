namespace GenWave.Core.Domain;

/// <summary>
/// Brand tags embedded into a mixed artifact's file metadata (RIFF INFO for wav) so the artifact is
/// self-describing and a tags-only re-enrich round-trips the brand instead of blanking it (F27.2).
/// </summary>
/// <param name="Artist">Embedded as the file's "artist" field — the station's brand name.</param>
/// <param name="Title">Embedded as the file's "title" field — the segment's display title.</param>
public sealed record AudioTags(string Artist, string Title);
