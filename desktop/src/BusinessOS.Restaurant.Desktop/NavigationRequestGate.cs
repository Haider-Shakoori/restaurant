using System.Threading;

namespace BusinessOS.Restaurant.Desktop;

// UI-only latest-request-wins coordinator. No cancellation of database writes is attempted;
// navigation only publishes a completed view when its request is still the newest.
internal sealed class NavigationRequestGate
{
    private long _latestRequest;

    public long Begin() => Interlocked.Increment(ref _latestRequest);

    public bool IsCurrent(long requestId) =>
        requestId != 0 && Volatile.Read(ref _latestRequest) == requestId;
}
