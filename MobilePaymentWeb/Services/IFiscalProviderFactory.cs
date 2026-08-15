namespace MobilePaymentWeb.Services
{
    public interface IFiscalProviderFactory
    {
        IFiscalProvider? GetProvider(string facilityId);
    }
}
