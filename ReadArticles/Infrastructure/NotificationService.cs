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
    // At most three active cards. The oldest waiting messages enter first;
    // each free slot admits the next queued item, displayed above older cards.
    public IReadOnlyList<NotificationItem> Visible => messages.Take(3).Reverse().ToArray();
    public event Action? Changed;

    public NotificationItem? Publish(string key, string message, NotificationSeverity severity)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("A stable notification key is required.", nameof(key));
        var notification = new NotificationItem(++nextId, key, message, severity);
        messages.Add(notification);
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

}
