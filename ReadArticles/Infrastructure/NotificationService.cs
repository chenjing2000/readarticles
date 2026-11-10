using System.Collections.ObjectModel;

namespace ReadArticles.Infrastructure;

public enum NotificationSeverity { Info, Warning, Error }

public sealed record NotificationItem(long Id, string Key, string Message, NotificationSeverity Severity);

/// <summary>UI-independent notification state. The host controls animation and auto-dismiss timing.</summary>
public sealed class NotificationService
{
    private readonly List<NotificationItem> messages = new();
    private long nextId;
    public ReadOnlyCollection<NotificationItem> Messages => messages.AsReadOnly();
    public IReadOnlyList<NotificationItem> Visible => messages.TakeLast(4).ToArray();
    public IReadOnlyList<NotificationItem> OverflowErrors =>
        messages.Where(m => m.Severity == NotificationSeverity.Error && !Visible.Any(v => v.Id == m.Id)).ToArray();
    public event Action? Changed;

    public NotificationItem? Publish(string key, string message, NotificationSeverity severity)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("A stable notification key is required.", nameof(key));
        // Repeated recoverable events should not flood the reading area.
        if (severity != NotificationSeverity.Error)
        {
            var duplicate = messages.FirstOrDefault(item => item.Key == key && item.Severity == severity);
            if (duplicate is not null) return duplicate;
        }
        var notification = new NotificationItem(++nextId, key, message, severity);
        messages.Add(notification);
        TrimNonErrors();
        Changed?.Invoke();
        return notification;
    }

    public bool Dismiss(long id)
    {
        int index = messages.FindIndex(n => n.Id == id);
        if (index < 0) return false;
        messages.RemoveAt(index);
        Changed?.Invoke();
        return true;
    }

    public void Clear()
    {
        messages.Clear();
        Changed?.Invoke();
    }

    private void TrimNonErrors()
    {
        // Keep every unacknowledged Error; only old transient messages can be evicted.
        while (messages.Count(m => m.Severity != NotificationSeverity.Error) > 4)
        {
            var victim = messages.FirstOrDefault(m => m.Severity == NotificationSeverity.Info)
                ?? messages.First(m => m.Severity == NotificationSeverity.Warning);
            messages.Remove(victim);
        }
    }
}
