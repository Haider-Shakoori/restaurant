using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Restaurant.Persistence;

public sealed class LocalDatabaseFactory
{
    private readonly string _databasePath;

    public LocalDatabaseFactory(string? rootDirectory = null)
    {
        var root = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BusinessOS",
            "Restaurant");

        _databasePath = Path.Combine(root, "data", "restaurant.db");
    }

    public RestaurantDbContext Create()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            ForeignKeys = true,
        }.ToString();

        var options = new DbContextOptionsBuilder<RestaurantDbContext>()
            .UseSqlite(connectionString)
            .Options;

        return new RestaurantDbContext(options);
    }

    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        await using var db = Create();
        await db.Database.EnsureCreatedAsync(cancellationToken);
    }
}
