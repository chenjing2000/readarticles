using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace ReadArticles.Infrastructure;

public sealed class AppLogger
{
    private const long DefaultMaxFileBytes = 2 * 1024 * 1024;
    private const long DefaultMaxTotalBytes = 20 * 1024 * 1024;
    private const int DefaultRetentionDays = 14;
    private const int MaxStackTraceLength = 8192;
    private readonly object sync = new();
    private readonly string directory;
    private readonly long maxFileBytes;
    private readonly long maxTotalBytes;
    private readonly int retentionDays;

    public static AppLogger Default { get; } = new(AppPaths.LogDirectory);

    public AppLogger(
        string directory,
        long maxFileBytes = DefaultMaxFileBytes,
        int retentionDays = DefaultRetentionDays,
        long maxTotalBytes = DefaultMaxTotalBytes)
    {
        this.directory = Path.GetFullPath(directory);
        this.maxFileBytes = Math.Max(256, maxFileBytes);
        this.retentionDays = Math.Max(1, retentionDays);
        this.maxTotalBytes = Math.Max(this.maxFileBytes, maxTotalBytes);
    }

    public void Info(string eventName) => Write("INFO", eventName, null);
    public void Warning(string eventName, Exception? error = null) => Write("WARN", eventName, error);
    public void Error(string eventName, Exception? error = null) => Write("ERROR", eventName, error);
    public void Critical(string eventName, Exception? error = null) => Write("CRITICAL", eventName, error);

    private void Write(string level, string eventName, Exception? error)
    {
        try
        {
            string safeEvent = new string((eventName ?? "Unknown").Where(c =>
                char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.').Take(64).ToArray());
            if (safeEvent.Length == 0) safeEvent = "Unknown";

            string? stack = error is null ? null : new StackTrace(error, false).ToString();
            if (stack?.Length > MaxStackTraceLength) stack = stack[..MaxStackTraceLength];

            var entry = new
            {
                timestamp = DateTimeOffset.Now,
                level,
                eventName = safeEvent,
                exceptionType = error?.GetType().FullName,
                hresult = error?.HResult,
                stackTrace = stack
            };
            byte[] line = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry) + Environment.NewLine);
            if (line.LongLength > maxFileBytes) return;

            lock (sync)
            {
                Directory.CreateDirectory(directory);
                string file = Path.Combine(directory, DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                if (File.Exists(file) && new FileInfo(file).Length + line.LongLength > maxFileBytes)
                {
                    string previous = Path.Combine(directory, DateTime.Now.ToString("yyyy-MM-dd") + ".1.log");
                    if (File.Exists(previous)) File.Delete(previous);
                    File.Move(file, previous);
                }

                using (var stream = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.Read))
                    stream.Write(line, 0, line.Length);

                Prune(DateTime.Now.Date);
            }
        }
        catch
        {
            // Diagnostics are best-effort and must never interfere with reading or playback.
        }
    }

    private void Prune(DateTime today)
    {
        var files = new DirectoryInfo(directory).EnumerateFiles("*.log")
            .Where(file => file.Name.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var file in files.Where(file => (today - file.LastWriteTime.Date).TotalDays >= retentionDays))
        {
            try { file.Delete(); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        files = new DirectoryInfo(directory).EnumerateFiles("*.log").ToList();
        long total = files.Sum(file => file.Length);
        foreach (var file in files.OrderBy(file => file.LastWriteTimeUtc))
        {
            if (total <= maxTotalBytes) break;
            try
            {
                long length = file.Length;
                file.Delete();
                total -= length;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
