using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Prometheus.Core.Storage;

namespace Prometheus.Client.Services;

internal sealed class ProjectHistory
{
    private const string Header = "Fecha,Revision,Origen,Accion,Detalle";
    private static readonly AsyncLocal<string?> Origin = new();
    private readonly object _queueLock = new();
    private readonly object _writeLock = new();
    private readonly List<string> _unsaved = new();
    private readonly Dictionary<string, List<string>> _pending = new(StringComparer.OrdinalIgnoreCase);

    internal static T WithOrigin<T>(string origin, Func<T> action)
    {
        var prior = Origin.Value;
        Origin.Value = origin;
        try { return action(); }
        finally { Origin.Value = prior; }
    }

    internal bool HasPending
    {
        get { lock (_queueLock) return _pending.Count > 0; }
    }

    internal void DiscardUnsaved()
    {
        lock (_queueLock) _unsaved.Clear();
    }

    internal void Add(string? projectPath, int revision, string action, string? details)
    {
        var row = string.Join(",", DateTimeOffset.Now.ToString("O"), revision.ToString(CultureInfo.InvariantCulture),
            Cell(Origin.Value ?? "Editor"), Cell(action), Cell(details ?? ""));
        lock (_queueLock)
        {
            if (projectPath == null) _unsaved.Add(row);
            else PendingFor(projectPath).Add(row);
        }
    }

    internal void AdoptUnsaved(string projectPath)
    {
        lock (_queueLock)
        {
            if (_unsaved.Count == 0) return;
            PendingFor(projectPath).InsertRange(0, _unsaved);
            _unsaved.Clear();
        }
    }

    internal void Flush()
    {
        lock (_writeLock)
        {
            string[] paths;
            lock (_queueLock) paths = _pending.Keys.ToArray();
            foreach (var path in paths) FlushPath(path);
        }
    }

    internal void FlushProject(string projectPath)
    {
        lock (_writeLock) FlushPath(projectPath);
    }

    internal void CopyTo(string sourceProject, string targetProject)
    {
        lock (_writeLock)
        {
            FlushPath(sourceProject);
            try
            {
                var from = HistoryPath(sourceProject);
                var to = HistoryPath(targetProject);
                if (File.Exists(from) && !File.Exists(to)) File.Copy(from, to);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    private void FlushPath(string projectPath)
    {
        List<string>? rows;
        lock (_queueLock)
        {
            if (!_pending.Remove(projectPath, out rows)) return;
        }

        try
        {
            var path = HistoryPath(projectPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read, bufferSize: 1);
            var previousLength = stream.Length;
            stream.Position = previousLength;
            var prefix = previousLength == 0 ? "\ufeff" + Header + Environment.NewLine : "";
            var bytes = Encoding.UTF8.GetBytes(prefix + string.Join(Environment.NewLine, rows) + Environment.NewLine);
            try
            {
                stream.Write(bytes);
                stream.Flush();
            }
            catch (IOException)
            {
                try { stream.SetLength(previousLength); } catch (IOException) { }
                throw;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            lock (_queueLock) PendingFor(projectPath).InsertRange(0, rows);
        }
    }

    private List<string> PendingFor(string projectPath)
    {
        if (!_pending.TryGetValue(projectPath, out var rows))
            _pending.Add(projectPath, rows = new());
        return rows;
    }

    private static string HistoryPath(string projectPath) =>
        Path.Combine(ProjectStorage.GetDataDirectory(projectPath), "historial.csv");

    private static string Cell(string value)
    {
        var text = value.Replace('\r', ' ').Replace('\n', ' ');
        if (text.TrimStart() is [ '=' or '+' or '-' or '@', .. ]) text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
