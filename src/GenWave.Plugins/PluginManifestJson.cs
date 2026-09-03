namespace GenWave.Plugins;

/// <summary>
/// Ephemeral, all-nullable projection of the untrusted <c>plugin.json</c> document — mirrors
/// <c>GenWave.Host.Icons.IconPackDefinitionParser</c>'s own <c>*Json</c> idiom: nothing here is trusted
/// until <see cref="PluginManifestParser.Parse"/> has checked it field by field, then discarded in
/// favour of the immutable <see cref="PluginManifest"/>. Property names deserialize against the SPEC
/// F156.2 field names (<c>name</c>/<c>version</c>/<c>assembly</c>/<c>entryType</c>/<c>abstractions</c>)
/// via the parser's camelCase <c>JsonSerializerOptions</c>.
/// </summary>
internal sealed record PluginManifestJson
{
    public string? Name { get; init; }
    public string? Version { get; init; }
    public string? Assembly { get; init; }
    public string? EntryType { get; init; }
    public string? Abstractions { get; init; }
}
