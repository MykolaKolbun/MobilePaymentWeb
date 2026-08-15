using MobilePaymentWeb.Services.IPay;
using MobilePaymentWeb.Services.Monobank;

namespace MobilePaymentWeb.Services
{
    public class PaymentProviderFactory : IPaymentProviderFactory
    {
        private readonly Dictionary<string, IPaymentProvider> _providers;

        public PaymentProviderFactory(
            Dictionary<string, FacilityOptions> facilities,
            ILogger<IPayClient> iPayLogger,
            ILogger<MonobankClient> monobankLogger)
        {
            _providers = facilities.ToDictionary(
                kv => kv.Key,
                kv => CreateProvider(kv.Value.Payment, iPayLogger, monobankLogger));
        }

        private static IPaymentProvider CreateProvider(
            PaymentOptions payment,
            ILogger<IPayClient> iPayLogger,
            ILogger<MonobankClient> monobankLogger)
            => payment.Provider switch
            {
                "Monobank" => new MonobankClient(payment.Token, monobankLogger),
                "IPay" or "" or null => new IPayClient(payment.MchId, payment.SignKey, iPayLogger, payment.Sandbox),
                _ => throw new InvalidOperationException($"Unknown payment provider '{payment.Provider}'")
            };

        public IPaymentProvider GetProvider(string facilityId)
        {
            if (_providers.TryGetValue(facilityId, out var provider))
                return provider;
            throw new KeyNotFoundException($"No payment provider configured for facility '{facilityId}'");
        }
    }
}
