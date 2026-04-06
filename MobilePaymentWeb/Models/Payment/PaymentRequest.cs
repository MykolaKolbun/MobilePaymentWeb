namespace MobilePaymentWeb.Models.Payment
{
    public class PaymentRequest
    {
        public decimal Amount { get; internal set; }
        public string Description { get; internal set; }
        public string Currency { get; set; }
        public double? Lifetime { get; internal set; }
        public string Lang { get; internal set; }
        public object Info { get; internal set; }
        public string SuccessUrl { get; set; } = string.Empty;
        public string FailUrl { get; set; } = string.Empty;
    }
}
