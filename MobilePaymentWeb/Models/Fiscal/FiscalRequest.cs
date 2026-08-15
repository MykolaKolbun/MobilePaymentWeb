namespace MobilePaymentWeb.Models.Fiscal
{
    public class FiscalRequest
    {
        public decimal Amount { get; set; }
        public string Description { get; set; } = string.Empty;
        public string PaymentType { get; set; } = "CARD";
        public string ExternalId { get; set; } = string.Empty;
    }
}
