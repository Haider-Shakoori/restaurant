using BusinessOS.Restaurant.Licensing;
using BusinessOS.Restaurant.LocalServer;
using Xunit;

namespace BusinessOS.Restaurant.Tests;

public sealed class LocalServerTests
{
    [Fact]
    public void Local_server_defaults_to_expected_LAN_port()
    {
        var options = new LocalServerOptions("tenant-1");

        Assert.Equal(8787, options.Port);
        Assert.True(options.Enabled);
        options.Validate();
    }

    [Theory]
    [InlineData(80)]
    [InlineData(70000)]
    public void Local_server_rejects_unsafe_or_invalid_ports(int port)
    {
        var options = new LocalServerOptions("tenant-1", port);

        Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
    }

    [Fact]
    public void Connection_settings_enable_local_host_by_default()
    {
        var settings = new ConnectionSettings("https://restaurant.example.test");

        Assert.True(settings.SyncEnabled);
        Assert.True(settings.LocalServerEnabled);
        Assert.Equal(8787, settings.LocalServerPort);
    }
}
