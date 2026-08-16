using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace MobilePaymentWeb.Services.Notifications
{
    /// <summary>
    /// Background sender for Telegram admin alerts. Callers enqueue via <see cref="Notify"/>
    /// (non-blocking); a single background loop drains the queue at ~1 msg/s (Telegram's
    /// per-chat limit) and swallows all failures so alerting can never affect the app.
    /// </summary>
    public class TelegramNotifier : BackgroundService, ITelegramNotifier
    {
        private readonly TelegramOptions _options;
        private readonly IHttpClientFactory _httpFactory;

        private readonly Channel<string> _queue = Channel.CreateBounded<string>(
            new BoundedChannelOptions(100) { FullMode = BoundedChannelFullMode.DropWrite });

        private readonly ConcurrentDictionary<string, DateTime> _recent = new();
        private static readonly TimeSpan DedupeWindow = TimeSpan.FromMinutes(1);

        public TelegramNotifier(IOptions<TelegramOptions> options, IHttpClientFactory httpFactory)
        {
            _options = options.Value;
            _httpFactory = httpFactory;
        }

        public void Notify(string message)
        {
            if (!_options.Enabled || string.IsNullOrWhiteSpace(message))
                return;

            // Drop identical messages seen within the dedupe window (prevents flood loops).
            var now = DateTime.UtcNow;
            if (_recent.TryGetValue(message, out var last) && now - last < DedupeWindow)
                return;
            _recent[message] = now;

            _queue.Writer.TryWrite(message); // non-blocking; silently dropped if full
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.Enabled)
                return;

            await foreach (var message in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var http = _httpFactory.CreateClient();
                    await http.PostAsJsonAsync(
                        $"https://api.telegram.org/bot{_options.BotToken}/sendMessage",
                        new { chat_id = _options.ChatId, text = message, disable_web_page_preview = true },
                        stoppingToken);
                }
                catch
                {
                    // Best-effort: a Telegram/network failure must never surface to the app.
                }

                // Respect Telegram's ~1 msg/s per-chat rate limit.
                try { await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); }
                catch (OperationCanceledException) { break; }

                if (_recent.Count > 200)
                {
                    var cutoff = DateTime.UtcNow - DedupeWindow;
                    foreach (var kv in _recent)
                        if (kv.Value < cutoff) _recent.TryRemove(kv.Key, out _);
                }
            }
        }
    }
}
