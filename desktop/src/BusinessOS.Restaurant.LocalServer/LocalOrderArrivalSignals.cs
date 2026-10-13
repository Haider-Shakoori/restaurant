namespace BusinessOS.Restaurant.LocalServer;

/// <summary>
/// In-process signal emitted only when the LAN host durably accepts a new
/// waiter kitchen submission. Never raised on mutation-id replay or failure.
/// The desktop shell subscribes to produce an audible alert and toast.
/// </summary>
public static class LocalOrderArrivalSignals
{
    public static event Action<string>? KitchenOrderReceived;

    internal static void NotifyKitchenOrderReceived(string reference)
    {
        var listeners = KitchenOrderReceived;
        if (listeners is null) return;
        foreach (Action<string> listener in listeners.GetInvocationList())
        {
            try { listener(reference); }
            catch { /* A failed speaker/UI must never roll back an order. */ }
        }
    }
}
