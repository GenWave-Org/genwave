using System.Net;

namespace GenWave.VoiceOff.Serving;

/// <summary>The address to show a judge. <paramref name="IsFallback"/> means no network address was found and this is the loopback.</summary>
internal sealed record LanChoice(IPAddress Address, bool IsFallback);
