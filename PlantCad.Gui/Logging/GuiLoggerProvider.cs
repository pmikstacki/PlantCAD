using System;
using System.Collections.Concurrent;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using PlantCad.Gui.ViewModels.Tools;

namespace PlantCad.Gui.Logging
{
    public sealed class GuiLoggerProvider : ILoggerProvider
    {
        private readonly Func<LogsToolViewModel?> _logsVmAccessor;
        private readonly ConcurrentQueue<string> _buffer = new ConcurrentQueue<string>();

        public GuiLoggerProvider(Func<LogsToolViewModel?> logsVmAccessor)
        {
            _logsVmAccessor =
                logsVmAccessor ?? throw new ArgumentNullException(nameof(logsVmAccessor));
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new GuiLogger(categoryName, _logsVmAccessor, _buffer);
        }

        public void Dispose()
        {
            // nothing to dispose
        }

        private sealed class GuiLogger : ILogger
        {
            private readonly string _category;
            private readonly Func<LogsToolViewModel?> _logsVmAccessor;
            private readonly ConcurrentQueue<string> _sharedBuffer;

            public GuiLogger(
                string category,
                Func<LogsToolViewModel?> logsVmAccessor,
                ConcurrentQueue<string> sharedBuffer
            )
            {
                _category = category ?? string.Empty;
                _logsVmAccessor = logsVmAccessor;
                _sharedBuffer = sharedBuffer;
            }

            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull => Noop.Instance;

            public bool IsEnabled(LogLevel logLevel)
            {
                if (logLevel >= LogLevel.Warning)
                    return true;
                return false;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            )
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }
                if (formatter == null)
                {
                    throw new ArgumentNullException(nameof(formatter));
                }

                var message = formatter(state, exception);
                if (string.IsNullOrWhiteSpace(message) && exception is null)
                {
                    return;
                }

                var prefix = $"[{DateTime.Now:HH:mm:ss}] {logLevel, 11} {_category}: ";
                var payload =
                    message + (exception is null ? string.Empty : Environment.NewLine + exception);
                // Guard against extremely long payloads
                if (payload.Length > 8000)
                {
                    payload = payload.Substring(0, 8000) + "… (truncated)";
                }
                var text = prefix + payload;

                var vm = _logsVmAccessor();
                if (vm is null)
                {
                    _sharedBuffer.Enqueue(text);
                    return;
                }

                // Flush buffered messages on UI thread and append current message
                Dispatcher.UIThread.Post(
                    () =>
                    {
                        try
                        {
                            while (_sharedBuffer.TryDequeue(out var buffered))
                            {
                                vm.Append(buffered);
                            }
                            vm.Append(text);
                        }
                        catch (Exception ex)
                        {
                            // Avoid crashing the app due to logging during render; buffer and try later
                            _sharedBuffer.Enqueue($"[WARN] GUI log append failed: {ex.Message}");
                        }
                    },
                    DispatcherPriority.ApplicationIdle
                );
            }

            private sealed class Noop : IDisposable
            {
                public static readonly Noop Instance = new Noop();

                private Noop() { }

                public void Dispose() { }
            }
        }
    }
}
