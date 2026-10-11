namespace BusinessOS.Restaurant.Desktop;

/// <summary>
/// SQLite's provider executes portions of its async API synchronously,
/// especially BeginTransaction and busy-lock waits. WPF event handlers must
/// never call those methods on the dispatcher. Work passed here must operate
/// ONLY on detached scalar snapshots and services, not WPF controls.
/// </summary>
internal static class DesktopUiWork
{
    public static Task RunAsync(Func<Task> operation) =>
        Task.Run(operation);

    public static Task<T> RunAsync<T>(Func<Task<T>> operation) =>
        Task.Run(operation);

    /// <summary>
    /// Bound an operator-triggered operation without freezing the dispatcher.
    /// A timeout is an UNCERTAIN result: SQLite may have committed before it
    /// was observed, so callers must keep the action locked and ask staff to
    /// verify the current state before attempting another financial write.
    /// </summary>
    public static async Task RunBoundedAsync(
        Func<CancellationToken, Task> operation, TimeSpan timeout)
    {
        using var limit = new CancellationTokenSource(timeout);
        await Task.Run(() => operation(limit.Token), limit.Token)
            .WaitAsync(limit.Token).ConfigureAwait(false);
    }
}
