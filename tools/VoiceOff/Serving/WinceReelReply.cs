using System.Text.Json.Serialization;

namespace GenWave.VoiceOff.Serving;

/// <summary>One reel in GET /wince. <paramref name="Engine"/> is present only once every reel has a count; until then no engine name is in the body.</summary>
internal sealed record WinceReelReply(
    string ReelId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Count,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Engine);
