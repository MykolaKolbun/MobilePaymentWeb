namespace MobilePaymentWeb.Models.Fiscal
{
    public class FiscalResponse
    {
        public bool IsSuccess { get; set; }
        public string? FiscalCode { get; set; }
        public byte[]? ReceiptPng { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
