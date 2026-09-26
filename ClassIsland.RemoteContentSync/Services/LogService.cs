namespace ClassIsland.RemoteContentSync.Services;

/// <summary>
/// 文件日志：写入插件配置目录下的 Logs/yyyy-MM-dd.log，并按保留天数清理。
/// </summary>
public sealed class LogService
{
    private readonly string _logDirectory;
    private readonly object _lock = new();
    private string _lastFile = "";

    public LogService(string logDirectory, int retentionDays = 14)
    {
        _logDirectory = logDirectory;
        RetentionDays = retentionDays;
        Directory.CreateDirectory(_logDirectory);
    }

    /// <summary>日志保留天数。</summary>
    public int RetentionDays { get; set; }

    public string LogDirectory => _logDirectory;

    public void Info(string message) => Write("INFO", message, null);

    public void Warn(string message) => Write("WARN", message, null);

    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        var file = Path.Combine(_logDirectory, DateTime.Now.ToString("yyyy-MM-dd") + ".log");
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}";
        if (exception != null)
        {
            line += Environment.NewLine + exception;
        }

        lock (_lock)
        {
            try
            {
                File.AppendAllText(file, line + Environment.NewLine);
                if (!string.Equals(_lastFile, file, StringComparison.OrdinalIgnoreCase))
                {
                    _lastFile = file;
                    Cleanup();
                }
            }
            catch
            {
                // 写日志本身失败时不能影响同步主流程
            }
        }
    }

    private void Cleanup()
    {
        try
        {
            var deadline = DateTime.Now.Date.AddDays(-RetentionDays);
            foreach (var path in Directory.EnumerateFiles(_logDirectory, "*.log"))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (DateTime.TryParseExact(name, "yyyy-MM-dd", null,
                        System.Globalization.DateTimeStyles.None, out var date) && date < deadline)
                {
                    File.Delete(path);
                }
            }
        }
        catch
        {
            // 清理失败可忽略
        }
    }
}
