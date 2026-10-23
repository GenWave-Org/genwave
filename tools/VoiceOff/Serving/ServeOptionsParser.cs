using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using GenWave.VoiceOff.Render;

namespace GenWave.VoiceOff.Serving;

/// <summary>Reads the arguments after <c>serve</c>. Any problem comes back as a plain message for exit code 2.</summary>
internal static class ServeOptionsParser
{
    internal const int DefaultPort = 8765;

    internal static bool TryParse(IReadOnlyList<string> args, [NotNullWhen(true)] out ServeOptions? options, out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);
        options = null;
        Dictionary<string, string> given = new(StringComparer.Ordinal);

        for (int i = 0; i < args.Count; i++)
        {
            string name = args[i];
            if (name is not ("--run" or "--root" or "--port" or "--bind"))
            {
                error = name.StartsWith("--", StringComparison.Ordinal)
                    ? $"Unknown option '{name}'. Run 'voiceoff --help' to see the verbs."
                    : $"Unexpected argument '{name}'. Options start with '--'.";
                return false;
            }

            if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                error = $"Option '{name}' needs a value.";
                return false;
            }

            if (!given.TryAdd(name, args[++i]))
            {
                error = $"Option '{name}' was given more than once.";
                return false;
            }
        }

        return TryBuild(given, out options, out error);
    }

    private static bool TryBuild(Dictionary<string, string> given, [NotNullWhen(true)] out ServeOptions? options, out string? error)
    {
        options = null;
        if (!given.TryGetValue("--run", out string? runId) || !RunId.TryParseTier(runId, out _))
        {
            error = given.ContainsKey("--run")
                ? $"'{given["--run"]}' is not a run id like 20261022T193000Z-cpu."
                : "Option '--run' is required: the paired run to serve, e.g. --run 20261022T193000Z-cpu.";
            return false;
        }

        int port = DefaultPort;
        if (given.TryGetValue("--port", out string? portText)
            && (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out port) || port is < 0 or > 65535))
        {
            error = "Option '--port' needs a whole number from 0 to 65535.";
            return false;
        }

        IPAddress bind = IPAddress.Any;
        if (given.TryGetValue("--bind", out string? bindText))
        {
            if (!IPAddress.TryParse(bindText, out IPAddress? parsed) || parsed.AddressFamily != AddressFamily.InterNetwork)
            {
                error = "Option '--bind' needs an IPv4 address like 0.0.0.0 or 127.0.0.1.";
                return false;
            }

            bind = parsed;
        }

        options = new ServeOptions(runId, Path.GetFullPath(given.GetValueOrDefault("--root") ?? RenderOptionsParser.DefaultRoot()), port, bind);
        error = null;
        return true;
    }
}
