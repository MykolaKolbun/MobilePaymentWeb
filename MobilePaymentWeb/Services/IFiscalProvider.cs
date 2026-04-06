using MobilePaymentWeb.Models.Fiscal;

namespace MobilePaymentWeb.Services
{
    public interface IFiscalProvider
    {
        Task<FiscalResponse> CreateReceiptAsync(FiscalRequest request);
    }
}
