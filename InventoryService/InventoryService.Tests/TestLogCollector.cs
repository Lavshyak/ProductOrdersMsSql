using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace InventoryService.Tests;

public sealed class TestLogCollector
{
    private readonly ConcurrentQueue<string> _entries = new();

    public void Add(string entry) => _entries.Enqueue(entry);

    public void Clear()
    {
        while (_entries.TryDequeue(out _))
        {
        }
    }

    public IReadOnlyList<string> Snapshot() => _entries.ToArray();
}

public sealed class TestLogCollectorProvider : ILoggerProvider
{
    private readonly TestLogCollector _collector;

    public TestLogCollectorProvider(TestLogCollector collector)
    {
        _collector = collector;
    }

    public ILogger CreateLogger(string categoryName) => new TestLogCollectorLogger(categoryName, _collector);

    public void Dispose()
    {
    }
}

internal sealed class TestLogCollectorLogger : ILogger
{
    private readonly string _categoryName;
    private readonly TestLogCollector _collector;

    public TestLogCollectorLogger(string categoryName, TestLogCollector collector)
    {
        _categoryName = categoryName;
        _collector = collector;
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        if (string.IsNullOrWhiteSpace(message) && exception is null)
        {
            return;
        }

        _collector.Add($"[{DateTime.UtcNow:O}] {logLevel,-11} {_categoryName} {message}{(exception is null ? string.Empty : Environment.NewLine + exception)}");
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}

