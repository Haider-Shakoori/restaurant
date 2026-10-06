using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace BusinessOS.Restaurant.LocalServer;

public sealed record LocalServerDescriptor(
    string TenantId,
    int Port,
    IReadOnlyList<string> LocalAddresses)
{
    public IReadOnlyList<string> BaseUrls =>
        LocalAddresses
            .Select(address => $"http://{FormatHost(address)}:{Port}")
            .ToArray();

    public static LocalServerDescriptor Create(LocalServerOptions options)
    {
        options.Validate();

        var addresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up)
            .Where(network => network.NetworkInterfaceType is not NetworkInterfaceType.Loopback)
            .SelectMany(network => network.GetIPProperties().UnicastAddresses)
            .Select(entry => entry.Address)
            .Where(address => address.AddressFamily is AddressFamily.InterNetwork)
            .Where(IsPrivateIpv4)
            .Select(address => address.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(address => address, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new LocalServerDescriptor(options.TenantId, options.Port, addresses);
    }

    internal static bool IsPrivateIpv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();

        return bytes[0] == 10 ||
               bytes[0] == 127 ||
               bytes[0] == 169 && bytes[1] == 254 ||
               bytes[0] == 192 && bytes[1] == 168 ||
               bytes[0] == 172 && bytes[1] is >= 16 and <= 31;
    }

    private static string FormatHost(string address) =>
        address.Contains(':', StringComparison.Ordinal) ? $"[{address}]" : address;
}
