using MobilePaymentWeb.Services.Fiscal.CheckBox;

namespace MobilePaymentWeb.Services
{
    public class FiscalProviderFactory : IFiscalProviderFactory
    {
        private readonly Dictionary<string, IFiscalProvider> _providers;

        public FiscalProviderFactory(Dictionary<string, FacilityOptions> facilities, ILogger<CheckBoxFiscalClient> logger)
        {
            _providers = new Dictionary<string, IFiscalProvider>();
            foreach (var (key, facility) in facilities)
            {
                if (facility.Fiscal.Provider == "CheckBox")
                    _providers[key] = new CheckBoxFiscalClient(key, facility.Fiscal, logger);
            }
        }

        public IFiscalProvider? GetProvider(string facilityId)
        {
            _providers.TryGetValue(facilityId, out var provider);
            return provider;
        }
    }
}
