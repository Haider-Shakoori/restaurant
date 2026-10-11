using BusinessOS.Restaurant.Desktop;
using Xunit;

namespace BusinessOS.Restaurant.Tests;

public sealed class DesktopCriticalActionTests
{
    [Fact]
    public void Unverified_payment_remains_locked_to_avoid_double_charge()
    {
        var gate = new DesktopSubmissionGate();
        Assert.True(gate.TryBegin());
        Assert.False(gate.TryBegin());
        gate.Finish(lockOnSuccess: true);
        Assert.True(gate.IsLocked);
        Assert.False(gate.TryBegin());
        Assert.True(new DesktopSubmissionGate().TryBegin()); // independent gate
    }

    [Fact]
    public void Verified_partial_payment_allows_second_payment_on_same_page()
    {
        var gate = new DesktopSubmissionGate();
        Assert.True(gate.TryBegin());
        Assert.False(gate.TryBegin()); // rapid double click is blocked
        gate.Finish(lockOnSuccess: false); // committed state reloaded from SQLite
        Assert.False(gate.IsLocked);
        Assert.True(gate.TryBegin()); // remaining balance may be collected now
        gate.Finish(lockOnSuccess: true); // cannot verify: remain locked
        Assert.False(gate.TryBegin());
    }

    [Fact]
    public void Failed_submission_or_completed_kot_round_can_be_retried()
    {
        var gate = new DesktopSubmissionGate();
        Assert.True(gate.TryBegin());
        gate.Finish(lockOnSuccess: false);
        Assert.False(gate.IsLocked);
        Assert.True(gate.TryBegin());
        gate.Finish(lockOnSuccess: false);
        Assert.True(gate.TryBegin());
    }

    [Fact]
    public async Task Double_tap_cannot_start_concurrent_payment_submissions()
    {
        var gate = new DesktopSubmissionGate();
        var commit = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        async Task SubmitAsync()
        {
            if (!gate.TryBegin()) return;
            try
            {
                Interlocked.Increment(ref calls);
                await commit.Task;
            }
            finally { gate.Finish(lockOnSuccess: true); }
        }

        var first = SubmitAsync();
        var second = SubmitAsync();
        Assert.Equal(1, calls);
        commit.SetResult(true);
        await Task.WhenAll(first, second);
        Assert.False(gate.TryBegin());
    }

    [Fact]
    public void Desktop_operations_require_confirmation_and_guard_payment_kot()
    {
        var root = RepositoryRoot();
        var views = File.ReadAllText(Path.Combine(root, "desktop", "src",
            "BusinessOS.Restaurant.Desktop", "OperationalActionViews.cs"));

        Assert.Contains("ConfirmOperationalChange(string message, string title)", views);
        Assert.Contains("DesktopGlassDialog.Confirm(message, title)", views);
        foreach (var title in new[] {
            "Confirm order line void", "Confirm order cancellation",
            "Confirm kitchen recall", "Confirm kitchen waste", "Confirm kitchen re-fire",
            "Confirm end-of-day closing"
        })
            Assert.Contains(title, views);
        Assert.Contains("var paymentSubmission = new DesktopSubmissionGate();", views);
        Assert.Contains("if (!paymentSubmission.TryBegin()) return;", views);
        Assert.Contains("paymentSubmission.Finish(lockOnSuccess: paymentNeedsManualVerification || (sentToDatabase && !verified));", views);
        Assert.Contains("await RefreshCashierStateAsync();", views);
        Assert.Contains("selected.Balance > 0", views);
        Assert.Contains("Name = \"PosGlassModalOverlay\"", views);
        Assert.Contains("Name = \"PosOpenPaymentModal\"", views);
        Assert.DoesNotContain("Refresh the cashier workspace before entering another payment.", views);
        Assert.Contains("var kotSubmission = new DesktopSubmissionGate();", views);
        Assert.Contains("kotSubmission.Finish(lockOnSuccess: false);", views);
        Assert.Contains("submit.IsEnabled = true;", views);
        Assert.Contains("Order served; recipe inventory consumption recorded.", views);
        Assert.DoesNotContain("Order served; DesktopNoticeEvents.Publish", views);
    }

    [Fact]
    public void Service_still_generates_distinct_transaction_ids_and_printer_recovery_requires_review()
    {
        var root = RepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, "desktop", "src",
            "BusinessOS.Restaurant.Desktop", "DesktopRestaurantWorkflowService.cs"));
        Assert.Contains("DESK-PAY-{Guid.CreateVersion7():N}", workflow);
        var pages = File.ReadAllText(Path.Combine(root, "desktop", "src",
            "BusinessOS.Restaurant.Desktop", "RestaurantOperationalPages.cs"));
        Assert.Contains("Confirm printer recovery", pages);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "desktop")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
