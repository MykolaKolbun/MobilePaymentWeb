using Microsoft.Extensions.Logging;

namespace MobilePaymentWeb.Services.Notifications
{
    /// <summary>
    /// Forwards selected log entries to Telegram: <c>Error</c>/<c>Critical</c> from any
    /// category, plus <c>Warning</c> from our own <c>MobilePaymentWeb.*</c> code (e.g. the
    /// price-drift warning) — without the framework warning noise.
    /// </summary>
    public sealed class TelegramLoggerProvider : ILoggerProvider
    {
        private readonly ITelegramNotifier _notifier;
        private readonly string _appName;

        public TelegramLoggerProvider(ITelegramNotifier notifier, string appName)
        {
            _notifier = notifier;
            _appName = appName;
        }

        public ILogger CreateLogger(string categoryName)
            => new TelegramLogger(_notifier, _appName, categoryName);

        public void Dispose() { }

        private sealed class TelegramLogger : ILogger
        {
            private const string AppNamespace = "MobilePaymentWeb";
            private const string SelfNamespace = "MobilePaymentWeb.Services.Notifications";

            private readonly ITelegramNotifier _notifier;
            private readonly string _appName;
            private readonly string _category;

            public TelegramLogger(ITelegramNotifier notifier, string appName, string category)
            {
                _notifier = notifier;
                _appName = appName;
                _category = category;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
                // Never forward the notifier's own logs — avoids feedback loops.
                if (_category.StartsWith(SelfNamespace, StringComparison.Ordinal))
                    return;

                var send = logLevel >= LogLevel.Error
                    || (logLevel == LogLevel.Warning
                        && _category.StartsWith(AppNamespace, StringComparison.Ordinal));
                if (!send)
                    return;

                var text = formatter(state, exception);
                var msg = $"⚠️ {_appName} [{logLevel}] {_category}\n{text}";
                if (exception != null)
                    msg += $"\n{exception.GetType().Name}: {exception.Message}";
                if (msg.Length > 3500)
                    msg = msg[..3500] + "…";

                _notifier.Notify(msg);
            }
        }
    }
}
