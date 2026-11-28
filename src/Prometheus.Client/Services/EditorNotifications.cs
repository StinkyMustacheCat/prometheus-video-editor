using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Prometheus.Client.Services;

internal sealed class EditorNotifications
{
    internal sealed record Entry(string Key, DateTimeOffset At, string Title, string Message,
        string? ActionLabel, Func<Task>? Action, int Count);

    private readonly List<Entry> _entries = new();
    internal IReadOnlyList<Entry> Entries => _entries;
    internal bool HasUnread { get; private set; }
    internal event Action? Changed;

    internal bool Add(string key, string title, string message, string? actionLabel = null, Func<Task>? action = null)
    {
        var now = DateTimeOffset.Now;
        var index = _entries.FindIndex(entry => entry.Key == key);
        var previous = index < 0 ? null : _entries[index];
        if (previous != null && previous.Message == message && now - previous.At < TimeSpan.FromSeconds(20)) return false;
        if (index >= 0) _entries.RemoveAt(index);
        _entries.Insert(0, new(key, now, title, message, actionLabel, action, (previous?.Count ?? 0) + 1));
        if (_entries.Count > 60) _entries.RemoveAt(_entries.Count - 1);
        HasUnread = true;
        Changed?.Invoke();
        return true;
    }

    internal void Remove(string key)
    {
        if (_entries.RemoveAll(entry => entry.Key == key) == 0) return;
        if (_entries.Count == 0) HasUnread = false;
        Changed?.Invoke();
    }

    internal void MarkRead()
    {
        HasUnread = false;
        Changed?.Invoke();
    }
}
