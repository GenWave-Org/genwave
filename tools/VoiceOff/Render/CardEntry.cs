using System.Text.Json.Serialization;

namespace GenWave.VoiceOff.Render;

/// <summary>One card's line in <c>run.json</c>: the machine it rendered on, whether it did, and why not (absent when it did).</summary>
internal sealed record CardEntry(string Host, bool Rendered, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason = null);
