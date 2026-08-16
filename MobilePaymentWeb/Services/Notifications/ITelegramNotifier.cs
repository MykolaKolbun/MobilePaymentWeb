namespace MobilePaymentWeb.Services.Notifications
{
    public interface ITelegramNotifier
    {
        /// <summary>
        /// Best-effort, non-blocking alert. Never throws; safe to call from the request path.
        /// Dropped silently if alerting is disabled, the queue is full, or the message is a
        /// recent duplicate.
        /// </summary>
        void Notify(string message);
    }
}
