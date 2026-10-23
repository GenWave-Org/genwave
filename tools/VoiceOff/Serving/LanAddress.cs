using System.Net;
using System.Net.NetworkInformation;

namespace GenWave.VoiceOff.Serving;

/// <summary>
/// Picks the address a phone on the same network can reach: the first Up, non-loopback, non-tunnel, non-link-local IPv4 address,
/// preferring interfaces not named like a container bridge. With none, the loopback. Pure, so a spec can drive it.
/// </summary>
internal static class LanAddress
{
    private static readonly string[] VirtualPrefixes = ["docker", "br-", "veth", "virbr"];

    internal static LanChoice Choose(IReadOnlyList<NetworkCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        List<NetworkCandidate> usable = [.. candidates.Where(IsUsable)];
        NetworkCandidate? pick = usable.FirstOrDefault(c => !IsVirtualName(c.Name)) ?? usable.FirstOrDefault();
        return pick is null ? new LanChoice(IPAddress.Loopback, true) : new LanChoice(pick.Address, false);
    }

    private static bool IsUsable(NetworkCandidate c) =>
        c.Status == OperationalStatus.Up
        && c.Type is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
        && c.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
        && !IPAddress.IsLoopback(c.Address)
        && !IsLinkLocal(c.Address);

    private static bool IsLinkLocal(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        return bytes[0] == 169 && bytes[1] == 254;
    }

    private static bool IsVirtualName(string name) =>
        VirtualPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
