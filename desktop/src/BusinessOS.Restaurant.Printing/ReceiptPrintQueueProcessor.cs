using BusinessOS.Restaurant.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessOS.Restaurant.Printing;

public sealed class ReceiptPrintQueueProcessor : IAsyncDisposable
{
    private readonly LocalDatabaseFactory _databaseFactory;
    private readonly WindowsRawPrinter _printer;
    private CancellationTokenSource? _cancellation;
    private Task? _loop;
    // Failed spool submissions back off without consuming ten attempts in seconds.
    private readonly Dictionary<string, DateTimeOffset> _retryNotBefore = new(StringComparer.Ordinal);

    public ReceiptPrintQueueProcessor(
        LocalDatabaseFactory databaseFactory,
        WindowsRawPrinter? printer = null)
    {
        _databaseFactory = databaseFactory;
        _printer = printer ?? new WindowsRawPrinter();
    }

    public Task StartAsync()
    {
        if (!OperatingSystem.IsWindows() || _loop is not null)
        {
            return Task.CompletedTask;
        }

        _cancellation = new CancellationTokenSource();
        _loop = RunAsync(_cancellation.Token);
        return Task.CompletedTask;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        await _databaseFactory.EnsureCreatedAsync(cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var processed = await ProcessNextAsync(cancellationToken);

                if (!processed)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }
        }
    }

    private async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        await using var db = _databaseFactory.Create();

        var candidates = await db.ReceiptPrintJobs
            .Where(value =>
                value.Status == "pending" ||
                value.Status == "failed") // An interrupted "printing" job needs operator review; auto-replay may duplicate food.

            .Where(value => value.Attempts < 10)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var candidate = candidates
            .Where(value => value.Status == "pending" ||
                !_retryNotBefore.TryGetValue(value.Id, out var next) || next <= now)
            .OrderBy(value => value.CreatedAtUtc)
            .FirstOrDefault();

        if (candidate is null)
        {
            return false;
        }

        var job = await db.ReceiptPrintJobs
            .SingleAsync(value => value.Id == candidate.Id, cancellationToken);

        job.Status = "printing";
        job.Attempts += 1;
        job.LastError = null;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            for (var copy = 0; copy < Math.Clamp(job.Copies, 1, 5); copy++)
            {
                _printer.Print(job.PrinterName, job.DocumentName, job.PayloadText);
            }

            job.Status = "printed";
            job.PrintedAtUtc = DateTimeOffset.UtcNow;
            job.LastError = null;
            _retryNotBefore.Remove(job.Id);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            job.Status = "failed";
            // Exponential in-process backoff (2..256 seconds). Persisted attempts
            // still enforce the existing cap across application restarts.
            _retryNotBefore[job.Id] = DateTimeOffset.UtcNow.AddSeconds(
                1 << Math.Min(Math.Max(job.Attempts, 1), 8));
            job.LastError = exception.Message.Length > 1000
                ? exception.Message[..1000]
                : exception.Message;
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        var cancellation = _cancellation;
        var loop = _loop;

        _cancellation = null;
        _loop = null;

        if (cancellation is null)
        {
            return;
        }

        await cancellation.CancelAsync();

        if (loop is not null)
        {
            try
            {
                await loop;
            }
            catch (OperationCanceledException)
            {
            }
        }

        cancellation.Dispose();
        GC.SuppressFinalize(this);
    }
}
