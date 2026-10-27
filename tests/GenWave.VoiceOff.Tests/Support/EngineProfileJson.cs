using System.Text.Json.Nodes;

namespace GenWave.VoiceOff.Tests.Support;

/// <summary>A valid engine profile as JSON, so a spec can add exactly one defect to it.</summary>
internal static class EngineProfileJson
{
    internal static JsonObject Valid(string slug = "alpha", bool? baseline = true)
    {
        JsonObject node = JsonNode.Parse($$"""
            {
              "slug": "{{slug}}",
              "container": "alpha-box",
              "port": 8880,
              "license": "MIT"
            }
            """)?.AsObject() ?? throw new InvalidOperationException("Valid profile did not parse.");
        if (baseline is not null)
        {
            node["baseline"] = baseline;
        }

        return node;
    }

    internal static JsonObject Child(JsonObject node, string name) =>
        node[name]?.AsObject() ?? throw new InvalidOperationException($"Valid profile has no {name}.");

    internal static string With(Action<JsonObject> defect, string slug = "alpha")
    {
        JsonObject node = Valid(slug);
        defect(node);
        return node.ToJsonString();
    }
}
