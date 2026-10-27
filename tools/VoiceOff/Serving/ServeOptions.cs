using System.Net;

namespace GenWave.VoiceOff.Serving;

/// <summary>What <c>serve</c> was asked for. Port 0 lets the system pick one. The engines and cards folders are where the run's card definitions are read from.</summary>
internal sealed record ServeOptions(string RunId, string Root, int Port, IPAddress Bind, string EnginesDirectory, string CardsDirectory);
