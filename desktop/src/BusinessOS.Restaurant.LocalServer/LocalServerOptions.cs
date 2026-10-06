namespace BusinessOS.Restaurant.LocalServer;

public sealed record LocalServerOptions(
    string TenantId,
    int Port = 8787,
    bool Enabled = true)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(TenantId))
        {
            throw new ArgumentException("A tenant ID is required for the local restaurant host.", nameof(TenantId));
        }

        if (Port is < 1024 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(Port), "The local restaurant host port must be between 1024 and 65535.");
        }
    }
}
