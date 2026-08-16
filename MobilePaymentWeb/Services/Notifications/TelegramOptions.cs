namespace MobilePaymentWeb.Services.Notifications
{
    public class TelegramOptions
    {
        /// <summary>Master switch. When false, no provider is wired and Notify is a no-op.</summary>
        public bool Enabled { get; set; }

        /// <summary>Bot token from BotFather. Secret — supply via env var / user-secrets.</summary>
        public string BotToken { get; set; } = string.Empty;

        /// <summary>Target chat/group id for the global admin alerts.</summary>
        public string ChatId { get; set; } = string.Empty;

        /// <summary>Label shown in each alert so you know which deployment sent it.</summary>
        public string AppName { get; set; } = "MobilePaymentWeb";
    }
}
