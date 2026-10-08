namespace BusinessOS.Restaurant.Desktop;

public enum DesktopNoticeLevel { Info, Success, Warning, Error }

public sealed record DesktopNotice(
    long Id, DateTimeOffset CreatedAt, DesktopNoticeLevel Level, string Message, bool IsRead)
{
    public string Kind => Level.ToString();
    public string Heading => Level switch
    {
        DesktopNoticeLevel.Success => "Success",
        DesktopNoticeLevel.Warning => "Warning",
        DesktopNoticeLevel.Error => "Action needs attention",
        _ => "Information",
    };
    public string Icon => Level switch
    {
        DesktopNoticeLevel.Success => "✓",
        DesktopNoticeLevel.Warning => "!",
        DesktopNoticeLevel.Error => "×",
        _ => "i",
    };
    public string TimeLabel => CreatedAt.ToLocalTime().ToString("HH:mm");
}

// Bounded, operator-session-only UI memory. No SQLite, personal data cache,
// network requirement, or persisted notification history.
internal sealed class DesktopNoticeFeed
{
    private const int MaxHistory = 100;
    private readonly List<DesktopNotice> _history = new();
    private long _nextId;

    public IReadOnlyList<DesktopNotice> History => _history;
    public int UnreadCount => _history.Count(x => !x.IsRead);

    public DesktopNotice? Publish(DesktopNoticeLevel level, string? message, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;
        var normalized = message.Trim();
        if (normalized.Length > 500)
            normalized = normalized[..497] + "...";

        // Suppress repeat alerts even when other workspace failures interleave.
        // Do not hide a new failure after the cooldown, and keep separate
        // messages/levels independently visible to the operator.
        var lastMatching = _history.FirstOrDefault(notice =>
            notice.Level == level && notice.Message == normalized);
        if (lastMatching is not null &&
            now >= lastMatching.CreatedAt &&
            now - lastMatching.CreatedAt < TimeSpan.FromMinutes(2))
            return null;

        var notice = new DesktopNotice(++_nextId, now, level, normalized, false);
        _history.Insert(0, notice);
        if (_history.Count > MaxHistory)
            _history.RemoveAt(_history.Count - 1);
        return notice;
    }

    public void MarkAllRead()
    {
        for (var i = 0; i < _history.Count; i++)
            if (!_history[i].IsRead)
                _history[i] = _history[i] with { IsRead = true };
    }
}

// Thin bridge from existing desktop operational screens to the active shell.
// MainWindowViewModel unsubscribes when the operator switches or closes it.
internal static class DesktopNoticeEvents
{
    public static event Action<DesktopNoticeLevel, string>? Posted;

    public static void Publish(DesktopNoticeLevel level, string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            Posted?.Invoke(level, message);
    }
}
