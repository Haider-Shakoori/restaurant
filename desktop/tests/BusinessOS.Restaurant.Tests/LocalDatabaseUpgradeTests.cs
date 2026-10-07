using BusinessOS.Restaurant.Persistence;
using Xunit;

namespace BusinessOS.Restaurant.Tests;

public sealed class LocalDatabaseUpgradeTests
{
    [Fact]
    public async Task EnsureCreated_recreates_missing_kot_rounds_table_for_existing_database()
    {
        var root = Path.Combine(Path.GetTempPath(), "businessos-restaurant-upgrade-" + Guid.NewGuid().ToString("N"));

        try
        {
            var factory = new LocalDatabaseFactory(root);
            await factory.EnsureCreatedAsync();

            await using (var connection = factory.CreateConnection())
            {
                await connection.OpenAsync();

                await using var drop = connection.CreateCommand();
                drop.CommandText = "DROP TABLE kot_rounds;";
                await drop.ExecuteNonQueryAsync();
            }

            await factory.EnsureCreatedAsync();

            await using var verify = factory.CreateConnection();
            await verify.OpenAsync();

            await using var command = verify.CreateCommand();
            command.CommandText = """
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type = 'table' AND name = 'kot_rounds';
                """;

            var count = Convert.ToInt32(await command.ExecuteScalarAsync());
            Assert.Equal(1, count);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
