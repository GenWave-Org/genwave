using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace GenWave.VoiceOff.Serving;

/// <summary>Reads this machine's interfaces into <see cref="NetworkCandidate"/> rows.</summary>
internal static class NetworkCandidates
{
    internal static IReadOnlyList<NetworkCandidate> Detect()
    {
        List<NetworkCandidate> rows = [];
        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            foreach (UnicastIPAddressInformation unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    rows.Add(new NetworkCandidate(nic.Name, nic.NetworkInterfaceType, nic.OperationalStatus, unicast.Address));
                }
            }
        }

        return rows;
    }
}
