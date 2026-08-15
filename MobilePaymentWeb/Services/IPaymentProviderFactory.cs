namespace MobilePaymentWeb.Services
{
    public interface IPaymentProviderFactory
    {
        IPaymentProvider GetProvider(string facilityId);
    }
}
