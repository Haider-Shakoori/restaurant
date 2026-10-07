using Xunit;

namespace BusinessOS.Restaurant.Tests;

public sealed class SqliteDateTimeOffsetRegressionTests
{
    [Fact]
    public void Desktop_pages_do_not_push_DateTimeOffset_sorting_into_SQLite()
    {
        var root = RepositoryRoot();
        var pages = File.ReadAllText(Path.Combine(
            root, "desktop", "src", "BusinessOS.Restaurant.Desktop", "RestaurantOperationalPages.cs"));
        var actions = File.ReadAllText(Path.Combine(
            root, "desktop", "src", "BusinessOS.Restaurant.Desktop", "OperationalActionViews.cs"));
        var expenses = File.ReadAllText(Path.Combine(
            root, "desktop", "src", "BusinessOS.Restaurant.LocalServer", "LocalExpenseService.cs"));
        var kitchen = File.ReadAllText(Path.Combine(
            root, "desktop", "src", "BusinessOS.Restaurant.LocalServer", "LocalKitchenService.cs"));
        var sync = File.ReadAllText(Path.Combine(
            root, "desktop", "src", "BusinessOS.Restaurant.LocalServer", "LocalSyncService.cs"));

        Assert.DoesNotContain("orderby ticket.QueuedAt", pages, StringComparison.Ordinal);
        Assert.DoesNotContain("orderby ticket.QueuedAt", actions, StringComparison.Ordinal);
        Assert.DoesNotContain("orderby po.OrderedAt descending", pages, StringComparison.Ordinal);
        Assert.DoesNotContain("expense.RecordedAtUtc descending", pages, StringComparison.Ordinal);

        Assert.DoesNotContain(
            ".AsNoTracking().Where(x => x.Status != \"closed\").OrderByDescending(x => x.UpdatedAtUtc)",
            pages,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ".AsNoTracking().Where(x => x.Status != \"closed\").OrderByDescending(x => x.UpdatedAtUtc)",
            actions,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ".AsNoTracking().OrderByDescending(x => x.ReceivedAt)",
            pages,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ".WaiterShifts.AsNoTracking().OrderByDescending(x => x.StartedAt)",
            pages,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ".CashierSessions.AsNoTracking().OrderByDescending(x => x.OpenedAt)",
            actions,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ".Where(x => x.Status == \"open\").OrderByDescending(x => x.OpenedAt)",
            actions,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ".Where(x => x.Status == \"open\").OrderByDescending(x => x.IssuedAt)",
            actions,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "db.Bills.Where(x => x.IssuedAt >= from && x.IssuedAt < to)",
            pages,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "q.OrderByDescending(x => x.ExpenseDate).ThenByDescending(x => x.RecordedAtUtc)",
            expenses,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            ".Where(value => value.KotRoundId == existingRound.Id)\n                .OrderBy(value => value.QueuedAt)",
            kitchen,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ".OrderBy(value => value.RoundNumber)\n            .ThenBy(value => value.QueuedAt)\n            .AsNoTracking()",
            kitchen,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ".Where(value =>\n                value.OrderId == order.Id &&\n                value.Status == \"held\" &&\n                value.CourseNumber == courseNumber)\n            .OrderBy(value => value.CreatedAtUtc)",
            sync,
            StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "desktop")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
