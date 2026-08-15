namespace MobilePaymentWeb.Services.Fiscal
{
    public class FiscalOptions
    {
        public string Provider { get; set; } = "None";
        public string ApiUrl { get; set; } = "https://api.checkbox.in.ua/";
        public string LicenseKey { get; set; } = string.Empty;
        public string PinCode { get; set; } = string.Empty;
        public int TaxCode { get; set; } = 0;
    }
}
