using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace FinanceManager.Web.Infrastructure.Logging;

public sealed class FileLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly IOptionsMonitor<FileLoggerOptions> _optionsMonitor;
    private readonly IDisposable _onChange;
    private readonly object _writerLock = new();
    private FileLoggerOptions _options;
    private IExternalScopeProvider? _scopeProvider;
    private StreamWriter? _writer;
    private string _currentFilePath = string.Empty;
    private string _currentDateStamp = string.Empty;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();

    public FileLoggerProvider(IOptionsMonitor<FileLoggerOptions> optionsMonitor)
    {
        _optionsMonitor = optionsMonitor;
        _options = _optionsMonitor.CurrentValue;
        _onChange = _optionsMonitor.OnChange(o => { lock (_writerLock) { _options = o; RotateIfNeeded(force: true); } });
        RotateIfNeeded(force: true);
    }

    public ILogger CreateLogger(string categoryName)
        => _loggers.GetOrAdd(categoryName, name => new FileLogger(this, name));

    public void Dispose()
    {
        _onChange.Dispose();
        lock (_writerLock)
        {
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;
        }
    }

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopeProvider = scopeProvider;

    internal void Log(string category, LogLevel level, EventId eventId, string message, Exception? exception, bool includeScopes)
    {
        var now = _options.UseUtcTimestamp ? DateTimeOffset.UtcNow : DateTimeOffset.Now;
        var timestamp = now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);

        var sb = new StringBuilder(512);
        sb.Append(timestamp)
          .Append(" [").Append(level.ToString().ToUpperInvariant()).Append(']')
          .Append(' ').Append(category);

        if (eventId.Id != 0)
        {
            sb.Append(" (").Append(eventId.Id.ToString(CultureInfo.InvariantCulture)).Append(')');
        }

        if (includeScopes && _scopeProvider is not null)
        {
            _scopeProvider.ForEachScope((scope, state) =>
            {
                state.Append(" => ").Append(scope);
            }, sb);
        }

        if (!string.IsNullOrEmpty(message))
        {
            sb.Append(' ').Append(message);
        }

        if (exception is not null)
        {
            sb.AppendLine().Append(exception);
        }

        var line = sb.ToString();

        lock (_writerLock)
        {
            RotateIfNeeded(force: false);
            _writer!.WriteLine(line);
            _writer.Flush();
            if (_options.RollOnFileSizeLimit && _writer.BaseStream is FileStream fs && fs.Length >= _options.FileSizeLimitBytes)
            {
                RotateIfNeeded(force: true);
            }
        }
    }

    private void RotateIfNeeded(bool force)
    {
        var dateStamp = (_options.UseUtcTimestamp ? DateTime.UtcNow : DateTime.Now).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var targetPath = BuildFilePath(dateStamp);

        if (!force && string.Equals(_currentFilePath, targetPath, StringComparison.Ordinal))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        // Close existing writer
        _writer?.Flush();
        _writer?.Dispose();
        _writer = null;

        // If rolling by size, pick next available index
        var finalPath = targetPath;
        if (_options.RollOnFileSizeLimit && File.Exists(finalPath) && new FileInfo(finalPath).Length >= _options.FileSizeLimitBytes)
        {
            finalPath = NextSizedPath(targetPath);
        }

        var stream = new FileStream(finalPath,
            _options.Append ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.Read);

        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
        _currentFilePath = finalPath;
        _currentDateStamp = dateStamp;

        EnforceRetention();
    }

    private string BuildFilePath(string dateStamp)
    {
        var baseDir = AppContext.BaseDirectory;
        var raw = _options.PathFormat.Replace("{date}", dateStamp, StringComparison.OrdinalIgnoreCase);
        var full = Path.IsPathRooted(raw) ? raw : Path.GetFullPath(raw, baseDir);
        return full;
    }

    private static string NextSizedPath(string basePath)
    {
        var dir = Path.GetDirectoryName(basePath)!;
        var filename = Path.GetFileNameWithoutExtension(basePath);
        var ext = Path.GetExtension(basePath);

        int index = 1;
        string candidate;
        do
        {
            candidate = Path.Combine(dir, $"{filename}_{index:D3}{ext}");
            index++;
        } while (File.Exists(candidate) && new FileInfo(candidate).Length > 0);

        return candidate;
    }

    private void EnforceRetention()
    {
        try
        {
            var dir = Path.GetDirectoryName(_currentFilePath)!;
            var name = Path.GetFileNameWithoutExtension(_currentFilePath);
            var ext = Path.GetExtension(_currentFilePath);

            // Match all files for the same base (e.g., app-YYYYMMDD*)
            var basePrefix = name.Split('_')[0]; // strip size suffix
            var pattern = $"{basePrefix}*{ext}";
            var files = new DirectoryInfo(dir).GetFiles(pattern)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();

            for (int i = _options.RetainedFileCountLimit; i < files.Count; i++)
            {
                try { files[i].Delete(); } catch { /* ignore */ }
            }
        }
        catch
        {
            // ignore retention failures
        }
    }
}

internal sealed class FileLogger : ILogger
{
    private readonly FileLoggerProvider _provider;
    private readonly string _category;

    public FileLogger(FileLoggerProvider provider, string category)
    {
        _provider = provider;
        _category = category;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => default!;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        _provider.Log(_category, logLevel, eventId, message, exception, includeScopes: false);
    }
}