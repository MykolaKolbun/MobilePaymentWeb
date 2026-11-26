using MobilePaymentWeb.Models.Fiscal;

namespace MobilePaymentWeb.Services
{
    public interface IFiscalProvider
    {
        Task<FiscalResponse> CreateReceiptAsync(FiscalRequest request);

        /// <summary>Closes the fiscal shift if it has been open longer than maxAge — prevents the 24h ПРРО overrun.</summary>
        Task CloseShiftIfStaleAsync(TimeSpan maxAge);
    }
}
