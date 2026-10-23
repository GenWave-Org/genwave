using System.Net;
using System.Net.NetworkInformation;

namespace GenWave.VoiceOff.Serving;

/// <summary>One address of one network interface, as <see cref="LanAddress"/> sees it.</summary>
internal sealed record NetworkCandidate(string Name, NetworkInterfaceType Type, OperationalStatus Status, IPAddress Address);
