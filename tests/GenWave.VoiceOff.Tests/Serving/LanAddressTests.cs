using System.Net;
using System.Net.NetworkInformation;
using GenWave.VoiceOff.Serving;

namespace GenWave.VoiceOff.Tests.Serving;

public static class LanAddressTests
{
    private static NetworkCandidate Nic(string name, string ip, OperationalStatus status = OperationalStatus.Up, NetworkInterfaceType type = NetworkInterfaceType.Ethernet) =>
        new(name, type, status, IPAddress.Parse(ip));

    public sealed class ScenarioTwoUpInterfaces
    {
        private readonly LanChoice choice = LanAddress.Choose([Nic("eth0", "10.0.0.5"), Nic("wlan0", "10.0.0.6")]);

        [Fact]
        public void TheFirstUpInterfaceWins() => Assert.Equal("10.0.0.5", choice.Address.ToString());

        [Fact]
        public void AFoundAddressIsNotAFallback() => Assert.False(choice.IsFallback);
    }

    public sealed class ScenarioContainerBridges
    {
        private readonly LanChoice choice = LanAddress.Choose([Nic("docker0", "172.17.0.1"), Nic("br-1a2b", "172.18.0.1"), Nic("wlan0", "192.168.1.9")]);

        [Fact]
        public void ContainerBridgesComeAfterRealInterfaces() => Assert.Equal("192.168.1.9", choice.Address.ToString());
    }

    public sealed class ScenarioNothingUsable
    {
        private readonly LanChoice choice = LanAddress.Choose([]);

        [Fact]
        public void NothingUsableFallsBackToTheLoopback() => Assert.Equal("127.0.0.1", choice.Address.ToString());

        [Fact]
        public void TheFallbackIsFlagged() => Assert.True(choice.IsFallback);
    }

    public sealed class ScenarioSkippedInterfaces
    {
        private static string Chosen(NetworkCandidate skipped) => LanAddress.Choose([skipped, Nic("wlan0", "10.0.0.6")]).Address.ToString();

        [Fact]
        public void ADownInterfaceIsSkipped() => Assert.Equal("10.0.0.6", Chosen(Nic("eth0", "10.0.0.5", OperationalStatus.Down)));

        [Fact]
        public void TheLoopbackIsSkipped() => Assert.Equal("10.0.0.6", Chosen(Nic("lo", "127.0.0.1", type: NetworkInterfaceType.Loopback)));

        [Fact]
        public void ALinkLocalAddressIsSkipped() => Assert.Equal("10.0.0.6", Chosen(Nic("eth0", "169.254.3.4")));

        [Fact]
        public void ATunnelIsSkipped() => Assert.Equal("10.0.0.6", Chosen(Nic("tun0", "10.8.0.2", type: NetworkInterfaceType.Tunnel)));
    }

    public sealed class ScenarioAVirtualOnlyMachine
    {
        [Theory]
        [InlineData("docker0")]
        [InlineData("br-1a2b3c")]
        [InlineData("veth12ab")]
        [InlineData("virbr0")]
        public void StillGetsItsAddress(string name) =>
            Assert.Equal("172.17.0.1", LanAddress.Choose([Nic(name, "172.17.0.1")]).Address.ToString());
    }
}
