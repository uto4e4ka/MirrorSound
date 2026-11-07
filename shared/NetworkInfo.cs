using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MirrorSound;

public sealed record NetworkCandidate(string Address, string Adapter, int Score)
{
    public override string ToString() => $"{Address}  ({Adapter})";
}

public static class NetworkInfo
{
    private static readonly string[] VirtualHints =
    [
        "virtual", "vpn", "tun", "tap", "zerotier", "radmin", "hyper-v", "virtualbox",
        "vmware", "wsl", "loopback", "hamachi", "tailscale", "wireguard", "openvpn", "bluetooth",
    ];

    /// <summary>IPv4 addresses of this PC, the real home-network adapter first.</summary>
    public static List<NetworkCandidate> Candidates()
    {
        var list = new List<NetworkCandidate>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

            var props = nic.GetIPProperties();
            var text = (nic.Name + " " + nic.Description).ToLowerInvariant();
            bool isVirtual = VirtualHints.Any(text.Contains);
            bool hasGateway = props.GatewayAddresses.Any(g =>
                g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
            bool physicalType = nic.NetworkInterfaceType is NetworkInterfaceType.Ethernet
                or NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.GigabitEthernet;

            foreach (var ua in props.UnicastAddresses)
            {
                var ip = ua.Address;
                if (ip.AddressFamily != AddressFamily.InterNetwork) continue;
                var b = ip.GetAddressBytes();
                if (b[0] == 169 && b[1] == 254) continue; // link-local, never reachable from the phone

                bool privateRange = b[0] == 10 || (b[0] == 192 && b[1] == 168) || (b[0] == 172 && b[1] is >= 16 and <= 31);
                int score = (hasGateway ? 100 : 0) + (physicalType ? 30 : 0) + (privateRange ? 20 : 0)
                            + (b[0] == 192 && b[1] == 168 ? 10 : 0) - (isVirtual ? 200 : 0);
                list.Add(new NetworkCandidate(ip.ToString(), nic.Name, score));
            }
        }
        return list.OrderByDescending(c => c.Score).ToList();
    }
}
