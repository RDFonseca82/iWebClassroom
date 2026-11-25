using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace IWebClassroom.Server.Services;

public sealed class LocalServerEndpointProvider
{
    public IReadOnlyCollection<string> HttpsUrls()
    {
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Dns.GetHostName()
        };

        foreach (var network in NetworkInterface.GetAllNetworkInterfaces()
                     .Where(item => item.OperationalStatus == OperationalStatus.Up &&
                                    item.NetworkInterfaceType != NetworkInterfaceType.Loopback))
        {
            foreach (var address in network.GetIPProperties().UnicastAddresses
                         .Select(item => item.Address)
                         .Where(item => !IPAddress.IsLoopback(item) &&
                                        item.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
            {
                hosts.Add(address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{address}]" : address.ToString());
            }
        }

        return hosts
            .Select(host => $"https://{host}:7443")
            .OrderBy(url => url.Contains('['))
            .ThenBy(url => url, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
