namespace MobilePaymentWeb.Models.Payment
{
    public class PaymentResponse
    {
        public string? Pid { get; set; }
        public string? PaymentUrl { get; set; }
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }
        public string RawXml { get; internal set; }
        public int? StatusCode { get; internal set; }
        public string? SaltResponse { get; internal set; }
        public string? SignResponse { get; internal set; }
    }
}
