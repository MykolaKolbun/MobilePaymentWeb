namespace MobilePaymentWeb.Services.IPay.Models
{
    public class CreatePaymentRequest
    {
        /// <summary>Amount in UAH (or specified currency)</summary>
        public decimal Amount { get; set; }

        /// <summary>Currency code, e.g. "UAH"</summary>
        public string Currency { get; set; } = "UAH";

        /// <summary>Your internal order ID</summary>
        public string OrderId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>Payment description shown to the user</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Redirect URL on successful payment</summary>
        public string GoodUrl { get; set; } = string.Empty;

        /// <summary>Redirect URL on failed payment</summary>
        public string FailUrl { get; set; } = string.Empty;

        /// <summary>Server callback URL for payment result</summary>
        public string ResultUrl { get; set; } = string.Empty;

        /// <summary>Link lifetime in hours (0.1 = 6 minutes). Null = default.</summary>
        public double? Lifetime { get; set; }

        /// <summary>UI language, e.g. "uk", "en", "ru"</summary>
        public string? Lang { get; set; } = "uk";
    }
}
