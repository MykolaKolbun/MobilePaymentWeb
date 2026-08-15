namespace MobilePaymentWeb.Services.IPay
{
    public class PaymentOptions
    {
        /// <summary>Payment gateway to use: "IPay" (default) or "Monobank".</summary>
        public string Provider { get; set; } = "IPay";

        // iPay
        public string MchId { get; set; } = string.Empty;
        public string SignKey { get; set; } = string.Empty;

        // Monobank — merchant token sent as the X-Token header
        public string Token { get; set; } = string.Empty;

        public bool Sandbox { get; set; } = true;
    }
}
