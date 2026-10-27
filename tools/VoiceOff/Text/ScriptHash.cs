using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace GenWave.VoiceOff.Text;

/// <summary>The script's content hash: SHA-256, lowercase hex, over the canonical JSON of the whole script document.</summary>
internal static class ScriptHash
{
    internal static string Of(JsonNode document) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalJson.Of(document))));
}
