#if false
using System;
using Microsoft.Extensions.Logging;

namespace PlantCad.Gui.Services.Internal
{
    public sealed class GuiLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName)
        {
            return new GuiLogger(categoryName);
        }

        public void Dispose()
        {
            // Nothing to dispose
        }

        private sealed class GuiLogger : ILogger
        {
            private readonly string _category;

            public GuiLogger(string category)
            {
                _category = category ?? string.Empty;
            }

            public IDisposable BeginScope<TState>(TState state) where TState : notnull
            {
                return NoopDisposable.Instance;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return logLevel != LogLevel.None;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }
                if (formatter is null)
                {
                    throw new ArgumentNullException(nameof(formatter));
                }

                var message = formatter(state, exception);
                if (string.IsNullOrWhiteSpace(message) && exception is null)
                {
                    return;
                }

                var prefix = $"[{DateTime.Now:HH:mm:ss}] {logLevel} {_category}: ";
                var text = prefix + message;
                if (exception is not null)
                {
                    text += Environment.NewLine + exception;
                }

                // Append to GUI logs tool if available
                var logsVm = Services.ServiceRegistry.LogsTool;
                logsVm?.Append(text);
            }

            private sealed class NoopDisposable : IDisposable
            {
                public static readonly NoopDisposable Instance = new NoopDisposable();
                private NoopDisposable() { }
                public void Dispose() { }
            }
        }
    }
}
#endif
