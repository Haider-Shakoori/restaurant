namespace BusinessOS.Restaurant.Desktop;

/// <summary>
/// Bridges the in-page operational chrome (the Keys and Full screen buttons
/// that live inside the POS and Kitchen action rows) to the main window.
/// Static events keep pages constructible without a window reference (tests,
/// previews, future tablet-hosted surfaces) while the shell remains the single
/// owner of window state.
/// </summary>
public static class OperationalWorkspaceChrome
{
    public static event Action? FullScreenToggleRequested;

    public static event Action? ShortcutsRequested;

    public static void RequestFullScreenToggle() => FullScreenToggleRequested?.Invoke();

    public static void RequestShortcuts() => ShortcutsRequested?.Invoke();
}
