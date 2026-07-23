namespace GenWave.Core.Domain;

/// <summary>
/// The result of resolving an opaque artwork token back to the row it names (SPEC F88.2,
/// gh-#105, STORY-222). Deliberately narrow: <see cref="Path"/> is the file locator the artwork
/// extraction seam (F88.3) needs, and <see cref="MediaId"/> stays entirely internal to that
/// resolution — no public surface ever re-exposes it (F62.9's "no numeric media id on any public
/// surface" applies to every consumer of this record, not just this seam).
/// </summary>
/// <param name="MediaId">The row's numeric <c>library.media.id</c> — internal use only.</param>
/// <param name="Path">The engine-visible file path (the Locator) to extract embedded art from.</param>
public sealed record ArtworkTokenResolution(long MediaId, string Path);
