using System.Net;

namespace GenWave.VoiceOff.Serving;

/// <summary>What <c>serve</c> was asked for. Port 0 lets the system pick one.</summary>
internal sealed record ServeOptions(string RunId, string Root, int Port, IPAddress Bind);
