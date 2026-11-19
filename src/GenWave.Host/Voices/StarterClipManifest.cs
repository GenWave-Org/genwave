using System.Text.Json;
using System.Text.RegularExpressions;

namespace GenWave.Host.Voices;

/// <summary>
/// Reads the starter manifest (<c>starter.json</c>) and keeps only entries that are safe to seed: a valid slug, a label, a credit
/// line, a file name that is exactly <c>&lt;slug&gt;.wav</c> and a length the table's CHECK accepts (SPEC F219.14).
/// </summary>
public static partial class StarterClipManifest
{
    public const string FileName = "starter.json";

    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Entries from <paramref name="json"/>; malformed JSON throws <see cref="JsonException"/>, invalid entries are dropped.</summary>
    public static IReadOnlyList<StarterClipEntry> Parse(string json) =>
        [.. (JsonSerializer.Deserialize<List<StarterClipEntry>>(json, JsonOptions) ?? []).Where(IsSeedable)];

    static bool IsSeedable(StarterClipEntry? entry) =>
        entry is { Slug: { } slug, Label: { } label, Credit: { } credit, File: { } file }
        && SlugShape().IsMatch(slug)
        && !string.IsNullOrWhiteSpace(label)
        && !string.IsNullOrWhiteSpace(credit)
        && file == VoiceClipFolder.FileNameFor(slug)
        && entry.DurationMs is >= VoiceClipLimits.MinDurationMs and <= VoiceClipLimits.MaxDurationMs;

    [GeneratedRegex("\\A[a-z0-9]+(-[a-z0-9]+)*\\z")]
    private static partial Regex SlugShape();
}
