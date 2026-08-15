using MobilePaymentWeb.Services.Fiscal;
using MobilePaymentWeb.Services.IPay;
using MobilePaymentWeb.Services.SkidataMobilePay;

namespace MobilePaymentWeb.Services
{
    public class FacilityOptions
    {
        public SkidataOptions Skidata { get; set; } = new();
        public PaymentOptions Payment { get; set; } = new();
        public FiscalOptions Fiscal { get; set; } = new();
        public DisplayOptions Display { get; set; } = new();
    }
}
