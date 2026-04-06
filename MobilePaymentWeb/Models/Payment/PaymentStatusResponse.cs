
namespace MobilePaymentWeb.Models.Payment
{
    public class PaymentStatusResponse
    {
        public string? Pid { get; internal set; }
        public PaymentStatus Status { get; internal set; }
        public string? ErrorMessage { get; internal set; }
    }
}
