using MobilePaymentWeb.Models.Payment;

namespace MobilePaymentWeb.Services
{
    public interface IPaymentProvider
    {
        Task<PaymentResponse> CreatePaymentAsync(PaymentRequest request);
        Task<PaymentStatusResponse> GetPaymentStatusAsync(string pid);
    }
}
