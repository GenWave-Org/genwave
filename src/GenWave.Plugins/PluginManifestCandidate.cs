namespace GenWave.Plugins;

/// <summary>
/// One directory <see cref="PluginManifestDiscovery.EnumerateCandidates"/> found a
/// <c>plugin.json</c> beneath (SPEC F156.2, STORY-385 AC2) — not yet parsed or validated.
/// </summary>
/// <param name="Slug">The candidate directory's own name, beneath the plugins root.</param>
/// <param name="ManifestPath">The manifest file's full path (<c>{root}/{Slug}/plugin.json</c>).</param>
public sealed record PluginManifestCandidate(string Slug, string ManifestPath);
