namespace GenWave.Tts;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;

/// <summary>
/// No boot-frozen <see cref="HttpClient.BaseAddress"/> (SPEC F36.1–F36.2) — <c>Tts:Endpoint</c> is
/// read from <see cref="IOptionsMonitor{TOptions}.CurrentValue"/> and an absolute URI is built per
/// call (<see cref="EndpointUri"/>), so a live PUT to <c>Tts:Endpoint</c> applies to the very next
/// render with no api restart.
/// </summary>
public sealed class KokoroTtsSynthesizer(HttpClient http, IOptionsMonitor<TtsOptions> optionsMonitor) : ITtsSynthesizer
{
    public async Task<string> SynthesizeAsync(string text, string voice, CancellationToken ct)
    {
        var cfg = optionsMonitor.CurrentValue;
        var body = new { input = text, voice, response_format = cfg.Format };
        using var content = new StringContent(
            JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var requestUri = EndpointUri.Combine(cfg.Endpoint, "/v1/audio/speech");
        var response = await http.PostAsync(requestUri, content, ct);
        response.EnsureSuccessStatusCode();   // throws HttpRequestException on non-2xx

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var path = GetCachePath(text, voice, cfg);

        // Path.GetDirectoryName always returns a non-null string when the path is produced
        // by Path.Combine with a non-empty CacheRoot; the guard below satisfies the compiler
        // without using the null-forgiving operator.
        var dir = Path.GetDirectoryName(path);
        if (dir is not null)
        {
            Directory.CreateDirectory(dir);
        }

        await File.WriteAllBytesAsync(path, bytes, ct);
        return path;
    }

    static string GetCachePath(string text, string voice, TtsOptions cfg)
    {
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(text + "|" + voice)));
        return Path.Combine(cfg.CacheRoot, $"{hash}.{cfg.Format}");
    }
}
