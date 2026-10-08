using System.Threading;

namespace BusinessOS.Restaurant.Desktop;

// WPF-only submission gate. A successful payment remains locked on its
// original page; reopening/refeshing creates a fresh intent and new gate.
internal sealed class DesktopSubmissionGate
{
    // 0 = ready; 1 = in flight; 2 = successful/locked until view refresh
    private int _state;

    public bool TryBegin() => Interlocked.CompareExchange(ref _state, 1, 0) == 0;

    public bool IsLocked => Volatile.Read(ref _state) == 2;

    public void Finish(bool lockOnSuccess)
    {
        var next = lockOnSuccess ? 2 : 0;
        if (Interlocked.CompareExchange(ref _state, next, 1) != 1)
            throw new InvalidOperationException("An action can only finish while in progress.");
    }
}
